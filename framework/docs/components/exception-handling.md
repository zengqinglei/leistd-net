# 业务异常与全局异常处理

业务代码抛出带 HTTP 语义的强类型异常，Web 宿主统一转换为 RFC 9457 Problem Details，Controller 不编写重复的 `try/catch`。

## 何时使用

| 场景 | 组合 |
| --- | --- |
| Domain/Application 需表达 400、401、403、404、409、415、422、500 或 503 | `Leistd.ExceptionHandling.Core` |
| 字段校验需返回结构化错误 | `UnprocessableEntityException` |
| Web API 需统一错误响应 | 追加 `Leistd.ExceptionHandling.AspNetCore` |

Core 包不依赖 ASP.NET Core，可由任意业务层引用。

## 安装

```bash
dotnet add package Leistd.ExceptionHandling.Core
dotnet add package Leistd.ExceptionHandling.AspNetCore
```

## 注册

```csharp
builder.Services.AddGlobalExceptionHandler(builder.Configuration);

var app = builder.Build();
app.UseGlobalExceptionHandler();
```

委托配置版：

```csharp
builder.Services.AddGlobalExceptionHandler(options =>
{
    options.IncludeExceptionDetails = false;
    options.ExcludePatterns.Add("/api/health/**");
});
```

`AddGlobalExceptionHandler` 注册 Problem Details、Options 和 `BusinessExceptionHandler`。`UseGlobalExceptionHandler` 应尽量放在管道前部，以覆盖后续中间件。

## 使用

### 抛出业务异常

```csharp
var order = await store.FindAsync(id)
    ?? throw new NotFoundException("Order was not found")
        .WithCode("Order:NotFound")
        .WithData("OrderId", id);
```

`Message` 是英文诊断；`Code` 是稳定的机器契约和本地化资源键，`WithData` 提供具名占位参数。

未显式调用 `WithCode` 时，`Code` 由 HTTP 状态码生成通用值，如 `Error:BadRequest` 或 `Error:NotFound`，因此顶层 `code` 始终非空。已对外的错误码不应重命名。

### 表达字段校验错误

```csharp
throw new UnprocessableEntityException(
        "email",
        "Invalid email format")
    .AddError(new ValidationError(
        Field: "phone",
        Message: "Phone number already in use",
        Code: "User:PhoneAlreadyUsed"));
```

422 响应使用 Problem Details 的 Leistd `errors` 扩展：

```json
{
  "type": "urn:leistd:problem:validation-error",
  "status": 422,
  "code": "Error:UnprocessableEntity",
  "message": "Validation failed.",
  "errors": [
    {
      "detail": "号码已被占用",
      "field": "phone",
      "code": "User:PhoneAlreadyUsed"
    }
  ]
}
```

`detail` 是本地化消息，`field` 是字段路径，字段级 `code` 可空。`ConfigureApiValidation()` 也将 `[ApiController]` 的自动 400 校验转换为该数组契约。

### 错误响应与本地化

一般业务错误响应包含：

```json
{
  "type": "urn:leistd:problem:business-error",
  "title": "Not Found",
  "status": 404,
  "detail": "资源不存在",
  "code": "Order:NotFound",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "message": "资源不存在"
}
```

用户消息按以下顺序解析：

1. `Code` 对应的词条。
2. `MessageExposure` 允许时直出 `Message`（默认允许 4xx）。
3. HTTP 状态码通用错误码对应的词条。
4. HTTP 状态短语。

第 2 步排在通用词条**之前**是有意的：通用词条（"请求无效。"）说不出哪条规则没过，抛出点的消息说得出。
让通用词条盖住消息，等于"漏配一条词条"的代价是用户完全无从修正——原因只剩在日志里。
通用词条因此只在消息不该外露（默认下的 5xx）或压根没有消息时出场。

消息该不该给用户看，是宿主的一条统一策略，不由抛出点各自决定：

| `MessageExposure` | 效果 |
| --- | --- |
| `None` | 一律不直出，只用词条与状态短语。对外网关用 |
| `ClientErrors`（默认） | 只直出 4xx。4xx 说的是"你的输入哪里不对"，5xx 是内部诊断 |
| `All` | 5xx 也直出。仅内部系统适用 |

配置经 `IOptionsMonitor` 读取，改配置即时生效。`Message` 无论哪档都完整进日志。

本地化失败不会改写原异常的 `Code` 或 HTTP 语义。`traceId` 优先使用 `Activity.Current.TraceId`，否则使用 `HttpContext.TraceIdentifier`。

### 非业务异常映射

| 原始异常 | 对外语义 |
| --- | --- |
| `ValidationException` | 422 `UnprocessableEntityException` |
| `CommonException` | 400 `BadRequestException` |
| `TimeoutException` / `HttpRequestException` | 503 `ServiceUnavailableException` |
| 包含超时的 `OperationCanceledException` | 503 `ServiceUnavailableException` |
| 普通 `OperationCanceledException` | 400 `BadRequestException` |
| 其它未知异常 | 500 `InternalServerException` |

## 接口参考

| 类型 | HTTP | 默认错误码 |
| --- | --- | --- |
| `BadRequestException` | 400 | `Error:BadRequest` |
| `UnauthorizedException` | 401 | `Error:Unauthorized` |
| `ForbiddenException` | 403 | `Error:Forbidden` |
| `NotFoundException` | 404 | `Error:NotFound` |
| `ConflictException` | 409 | `Error:Conflict` |
| `UnsupportedMediaTypeException` | 415 | `Error:UnsupportedMediaType` |
| `UnprocessableEntityException` | 422 | `Error:UnprocessableEntity` |
| `InternalServerException` | 500 | `Error:InternalServer` |
| `ServiceUnavailableException` | 503 | `Error:ServiceUnavailable` |

`BusinessException` 提供 `StatusCode`、`Code`、`Details`、`LocalizationData`、`WithCode()`、`WithDetails()` 和 `WithData()`。`ValidationError` 表示抛出方的字段错误，`ErrorItem` 是处理器输出的字段契约。

| Web 入口 | 用途 |
| --- | --- |
| `AddGlobalExceptionHandler` | 注册全局异常处理 |
| `UseGlobalExceptionHandler` | 将处理器接入管道 |
| `ConfigureApiValidation` | 统一 `[ApiController]` 自动校验响应 |

## 配置项（`Leistd:GlobalException`）

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `Enabled` | `true` | 是否接管异常 |
| `ExcludePatterns` | 空 | 跳过处理的路径模式，支持 `前缀/**` 和 `*` |
| `IncludeExceptionDetails` | `false` | 是否输出业务详情和堆栈 |
| `MessageExposure` | `ClientErrors` | 词条未命中时 `Message` 的直出范围：`None` / `ClientErrors` / `All` |

## 注意事项

- `Message` 始终进入日志，不应承担机器身份；前端分支只使用稳定 `Code`。
- `IncludeExceptionDetails` 不在生产环境开启。验证错误数组不受该开关影响。
- `InternalServerException` 记录 Error，其它业务异常记录 Warning；原始异常堆栈只用于诊断。
- `CommonException` 位于 `Leistd.Exceptions`，属于 `Leistd.Core`；带 HTTP 语义的异常位于 `Leistd.ExceptionHandling`。

## 相关

- [核心基础库](./core.md)
- [本地化](./localization.md)
- [统一 API 响应](./response.md)
