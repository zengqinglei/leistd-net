# 业务异常与全局异常处理

`Leistd.ExceptionHandling.Core` 只定义与 HTTP 无关的业务失败契约；`Leistd.ExceptionHandling.AspNetCore` 在 API 边界将异常转换为 RFC 9457 Problem Details。业务层不决定 HTTP 状态码，Controller 也不编写重复的 `try/catch`。

## 何时使用

| 场景 | 选择 |
| --- | --- |
| 参数空值、范围、状态或技术失败 | 优先 .NET 内置异常，如 `ArgumentException`、`InvalidOperationException`、`HttpRequestException` |
| Domain/Application 需表达可预期、可恢复的业务规则失败 | `BusinessException` |
| 请求 DTO 校验 | DataAnnotations；`[ApiController]` 自动返回 400 |
| 内部调用执行 DataAnnotations 校验 | `System.ComponentModel.DataAnnotations.ValidationException` |
| Web API 统一错误响应 | `Leistd.ExceptionHandling.AspNetCore` |

## 安装

```bash
dotnet add package Leistd.ExceptionHandling.Core
dotnet add package Leistd.ExceptionHandling.AspNetCore
```

## 注册

```csharp
builder.Services.AddGlobalExceptionHandler(builder.Configuration, options =>
{
    options.MapCode("Order:NotFound", StatusCodes.Status404NotFound);
    options.MapCode("Order:VersionConflict", StatusCodes.Status409Conflict);
});
builder.Services.AddControllers().ConfigureApiValidation();

var app = builder.Build();
app.UseCorrelationId();
app.UseGlobalExceptionHandler();
```

`UseGlobalExceptionHandler` 应尽量靠近管道前端，并放在关联 ID 中间件之后。`ConfigureApiValidation` 让 MVC 自动模型校验经同一管道写出，字段名跟随宿主的 JSON 命名策略；请求体读不成 JSON 时只报对应字段，不回显解析器的异常消息（关闭了 `AllowInputFormatterExceptionMessages`）。未匹配的 `BusinessException` 默认为 400；资源不存在、并发冲突、显式的 422 语义等由宿主在 API 组合根按稳定错误码映射。

组件 Core 只拥有错误码与安全文案，不决定 HTTP。具有 ASP.NET Core 适配包的组件提供显式调用的 `*ExceptionMappings.Configure(options)`，登记其固定的非默认 HTTP 语义或安全技术异常映射；宿主在组合根调用，不必复制组件映射表。组件使用 `MapDefaultCode` / `MapDefaultException` 登记默认值，宿主的 `MapCode` 与对同一类型的 `MapException` 均可覆盖默认值，调用顺序不影响优先级。组件的 `Add*` 不隐式注册异常处理服务。默认 400 不需要映射。HTTP 状态属于 API 契约，只在组合根代码里声明，不从配置文件读取。

## 使用

| 位置 | 异常规范 |
| --- | --- |
| Domain | 业务不变量失败抛 `BusinessException(code, safeMessage)`；实体方法不知道 HTTP |
| Application | 用例规则失败抛 `BusinessException`；请求可达的输入校验用 DataAnnotations / `ValidationException` |
| Infrastructure | 传输、配置、解析、数据损坏使用 BCL 或专用技术异常；不把上游错误伪装成本地业务失败 |
| API | 认证/授权交给 ASP.NET Core；错误码到 HTTP 状态的映射放在组合根 |
| 启动/后台任务 | 使用 BCL/技术异常使启动失败或记录任务失败，不造一个伪 HTTP 异常 |

```csharp
if (order.Status == OrderStatus.Shipped)
{
    throw new BusinessException(
            "Order:AlreadyShipped",
            $"Order '{order.Id}' has already shipped.")
        .WithData("OrderId", order.Id);
}
```

`Code` 在构造时必填且不可变，同时是机器契约和本地化资源键。`Message` 必须是安全、可在未启用本地化时直接返回的默认文案；技术细节放入 `InnerException` 和日志。能帮用户修正操作的非敏感输入可以回显；密码、令牌、连接串以及登录等匿名场景中会帮助枚举账号的标识不得回显。`WithData` 只为资源文案的具名占位符传值。

## 默认映射与安全边界

| 异常 | 默认响应 |
| --- | --- |
| `BusinessException` | 错误码映射命中时使用配置状态，否则 400；返回本地化文案或安全 `Message` |
| `System.ComponentModel.DataAnnotations.ValidationException` | 400，类型 `validation-error` + `errors`；不带业务码 |
| `BadHttpRequestException`（Minimal API 绑定失败、请求体过大等框架判定的请求错误） | 处理器放行，由异常中间件按异常自带的状态码（400、413 等）写出标准问题详情；原始消息只进日志 |
| 请求被客户端取消 | 处理器放行，不伪造失败响应 |
| 其它异常 | 500，只有本地化标题与 `traceId`，不回显异常消息；原异常记 Error 日志 |

`BadHttpRequestException` 由 ASP.NET Core 自己判定并携带状态码；`UseGlobalExceptionHandler` 通过 `ExceptionHandlerOptions.StatusCodeSelector` 让异常中间件沿用它，而不是 .NET 10 默认的 500。处理器不猜测 `HttpRequestException` 就是 503、`ArgumentException` 就是 400。这些异常往往代表本地缺陷或基础设施失败，未经宿主显式决策时应安全地返回 500。

业务错误码（`code`）与公开文案（`detail`）只出现在 `BusinessException` 上。输入校验、未预期异常、上游故障这类协议层失败的契约就是 HTTP 状态码本身（RFC 9457 §4），响应只有本地化标题、`traceId` 与可选的 `errors`，不合成与状态码一一对应的错误码。

400 是客户端请求错误的广义默认；422 只在宿主明确要表达“请求内容语法成立，但无法按其指令处理”且客户端确实需要区分时，才通过 `MapCode` 显式使用。

日志以 `TraceId` 关联请求。异常、自动校验及可选响应信封共用请求入口选定的 `HttpContext.TraceIdentifier`；关联 ID 中间件会把本次选定的标识写入该属性，且不要求其具有 W3C 格式。未安装中间件时使用 ASP.NET Core 的原生请求 ID；请求标识为空时才回落到当前 Activity 的 TraceId。响应字段 `traceId` 因此是应用关联标识，不保证等于 OpenTelemetry 的 `Activity.TraceId`：未安装中间件或选用了非 W3C 自定义关联 ID 时，不能仅凭响应值直接检索分布式链路，应先查服务日志。预期的 4xx 记 Warning，记录已经确认安全的公开消息，不记录未经审查的原始异常对象；5xx 记 Error 并保留异常链与堆栈。客户端可将 5xx 响应中的 `traceId` 告知支持人员快速定位。

## 无响应体的错误状态码

所有失败响应只走一条管道：ASP.NET Core 的 `IProblemDetailsService`。异常处理器、自动模型校验、状态码页、`Results.Problem()` 与框架各中间件都经它写出；本组件在它唯一的自定义钩子（`ProblemDetailsOptions.CustomizeProblemDetails`）上统一补 `traceId`、按 `Title:{状态码}` 本地化框架给的默认标题，不另造写出路径。

有些失败框架只写状态码、不写响应体：生产环境的 Minimal API 请求体解析失败（400）、内容类型不符（415）、未匹配路由（404）、认证质询（401）、限流（429）等。其中请求体解析失败还与环境有关——`RouteHandlerOptions.ThrowOnBadRequest` 默认只在开发环境开启，开发环境抛 `BadHttpRequestException`，由异常中间件按其自带状态码写出；生产环境直接写 400。为这类响应补上响应体用 ASP.NET Core 标准的状态码页，只作用于 API 路径（页面与静态资源的 404 不该变成 JSON）：

```csharp
app.UseGlobalExceptionHandler();
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    api => api.UseStatusCodePages());
```

这类协议层失败的契约就是 HTTP 状态码本身：响应只有 `status`、本地化的 `title` 与 `traceId`，不合成业务错误码。业务错误码（`code`）只出现在 `BusinessException` 上。

## 响应与本地化

默认响应为 Problem Details：

```json
{
  "type": "urn:leistd:problem:business-error",
  "title": "Not Found",
  "status": 404,
  "detail": "订单不存在",
  "code": "Order:NotFound",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

注册 `IStringLocalizer` 时，处理器按 `Code` 查资源并用 `LocalizationData` 替换 `{Name}` 占位符；未命中或未启用本地化时回落到安全 `Message`。不增加 WithCode 扩展：BCL 异常不应被临时贴上业务身份。

`IncludeExceptionDetails` 仅用于调试时输出 `stackTrace`，生产环境保持 `false`。

## 接口参考

- `GlobalExceptionOptions.MapCode(code, status)`：精确映射业务错误码。
- `GlobalExceptionOptions.MapException<TException>(...)`：宿主将自定义异常映射为 `ExceptionDescriptor`。
- `GlobalExceptionOptions.MapDefaultCode` / `MapDefaultException<TException>`：组件 Web 适配层登记默认映射；宿主显式映射优先，普通业务项目不必调用。
- 需要完全自定义某类异常的处理时，按 ASP.NET Core 的方式再注册一个 `IExceptionHandler`（`AddExceptionHandler<T>()`，按注册顺序尝试，返回 `false` 交给下一个）。只需改状态码或描述时用 `MapException`：它的委托拿得到异常实例，可以按属性分支。
- 个别路径需要其他错误格式（如 Webhook 回调要求的固定响应体）时，在 `AddGlobalExceptionHandler` 之前注册该格式的 `IExceptionHandler`，由它按 `HttpContext.Request.Path` 判断并返回 `true`；其余请求返回 `false` 交给全局处理器。
- `IProblemDetailsWriter`（ASP.NET Core）：替换失败响应的序列化形状。默认输出 Problem Details；`Leistd.Response.AspNetCore` 的 `AddResponseWrapper()` 注册一个排在最前的写入器，把全部问题详情写成数字信封。

`ExceptionDescriptor` 携带 HTTP 状态、可选的业务错误码与公开消息、可选字段错误与日志级别；协议层失败只给状态码。类型映射给出业务码与消息时，消息同样按 `Code` 查宿主本地化资源，未命中时使用提供的安全默认消息。自定义扩展必须维持这些安全边界。

## 配置项（`Leistd:GlobalException`）

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `Enabled` | `true` | 是否接管异常 |
| `IncludeExceptionDetails` | `false` | 是否输出堆栈，仅用于调试 |

映射的状态码必须为 400–599，`MapCode` / `MapDefaultCode` 传入越界值时立即抛出。默认 400 不必显式登记。

## 注意事项

Core 包不依赖 ASP.NET Core。不提供 UserFriendlyException，也不提供 BadRequestException / NotFoundException 等 HTTP 命名异常，避免开发者在业务层同时选“异常类型”和“错误码”两套分类。

## 相关

- [本地化](./localization.md)
- [统一 API 响应](./response.md)
