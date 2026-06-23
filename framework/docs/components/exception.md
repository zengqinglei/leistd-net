# 异常处理（`exception`）
> 提供一套语义化业务异常体系，并通过 ASP.NET Core 全局异常处理器将其统一转换为 RFC 7807 ProblemDetails 响应。

## 包

| 包 | 角色 | 何时引用 |
| --- | --- | --- |
| `Leistd.Exception.Core` | 业务异常类型定义（`BusinessException` 及各 HTTP 语义子类） | 任意需要抛出语义化异常的项目（含纯类库） |
| `Leistd.Exception.AspNetCore` | 全局异常处理器与 DI/中间件扩展，将异常转为 ProblemDetails | ASP.NET Core Web 应用 |

## 核心抽象

`BusinessException`（`Leistd.Exception.Core`）是抽象基类，继承自 `Leistd.Exception.CommonException`（定义于 `Leistd.Core`）。错误码由 `codePrefix + "00"` 组成（如前缀 `400` → 默认码 `40000`），处理器取错误码前 3 位作为 HTTP 状态码。

```csharp
public int Code { get; }            // 业务错误码，构造时由 codePrefix + "00" 生成
public string? Details { get; }     // 附加详情，默认 null
public BusinessException WithCode(string code);    // 用 codePrefix + code 重设 Code，返回 this（链式）
public BusinessException WithDetails(string details); // 设置 Details，返回 this（链式）
public string? GetStackTraceStr();  // 返回基类 StackTrace
```

预置子类（均接收 `message` 与可选 `innerException`，默认错误码见括号）：

```csharp
BadRequestException            // 普通异常（40000）
UnauthorizedException          // 未授权（40100）
ForbiddenException             // 禁止访问（40300）
NotFoundException              // 未找到（40400）
UnsupportedMediaTypeException  // 不支持媒体类型（41500）
UnprocessableEntityException   // 实体验证错误（42200）
InternalServerException        // 未知/系统异常（50000）
ServiceUnavailableException    // 服务不可用（50300）
```

`UnprocessableEntityException` 额外承载字段级校验错误（符合 `ValidationProblemDetails` 格式）：

```csharp
public Dictionary<string, string[]>? ValidationErrors { get; }
// 构造重载：单字段单错误 (field, error) / 单字段多错误 (field, errors[])
public UnprocessableEntityException WithErrors(Dictionary<string, string[]> errors); // 整体替换，返回 this
public UnprocessableEntityException AddError(string field, string error);            // 追加单错误，返回 this
public UnprocessableEntityException AddErrors(string field, params string[] errors); // 追加多错误，返回 this
```

## 能力实现

### `Leistd.Exception.AspNetCore`

DI 与中间件注册扩展（命名空间 `Leistd.Exception.AspNetCore`，类 `DependencyInjection`）：

```csharp
// 二选一：从配置节 "Leistd:GlobalException" 绑定 Options
services.AddGlobalExceptionHandler(IConfiguration configuration);
// 或：代码方式配置 Options
services.AddGlobalExceptionHandler(Action<GlobalExceptionOptions> configureOptions);

// 中间件，置于管线前端
app.UseGlobalExceptionHandler();
```

行为要点（`BusinessExceptionHandler`，`sealed`，由 DI 单例托管的 `IExceptionHandler`）：

- `GlobalExceptionOptions.Enable` 为 `false` 时直接跳过（默认 `false`，须显式开启）。
- 命中 `ExcludePatterns` 的请求路径不处理；模式支持精确匹配、`/**` 前缀通配与含 `*` 的通配转正则（大小写不敏感）。
- 非 `BusinessException` 会被映射为对应业务异常：`ValidationException` → `UnprocessableEntityException`；`CommonException` → `BadRequestException`；`OperationCanceledException`（内含 `TimeoutException`）→ `ServiceUnavailableException`，否则 → `BadRequestException`；`TimeoutException` / `HttpRequestException` → `ServiceUnavailableException`；其余 → `InternalServerException`。
- 日志分级：`InternalServerException` 走 `LogError`（含原始异常堆栈），其余走 `LogWarning`。
- 响应为 ProblemDetails，扩展字段含 `message`、`traceId`（取 `Activity.Current?.Id`，回退 `TraceIdentifier`）、`code`；校验异常使用 `ValidationProblemDetails`。
- 详情可见性由 `IsShowDetails` 决定：`true` 显示、`false` 隐藏、`null`（默认）按是否开发环境决定；显示时输出 `details` 或堆栈到 `stackTrace`。

## 最小可用示例

```csharp
using Leistd.Exception.AspNetCore;
using Leistd.Exception.Core;

var builder = WebApplication.CreateBuilder(args);

// 注册：开启并配置
builder.Services.AddGlobalExceptionHandler(options =>
{
    options.Enable = true;
    options.ExcludePatterns = new HashSet<string> { "/api/health/**" };
});

var app = builder.Build();
app.UseGlobalExceptionHandler();   // 置于管线前端

app.MapGet("/order/{id}", (int id) =>
{
    if (id <= 0)
        throw new BadRequestException("订单 ID 非法").WithDetails("id 必须为正整数");

    throw new NotFoundException($"订单 {id} 不存在");
});

app.Run();
```

抛出后客户端收到对应 HTTP 状态码（400 / 404 …）的 ProblemDetails，body 含 `code`、`message`、`traceId`。

## 依赖

`Leistd.Core`（`Leistd.Exception.Core` 引用，提供基类 `CommonException`）。

## 备注

- `GlobalExceptionOptions.Enable` 默认 `false`，不开启则处理器不生效。
- 配置节路径固定为 `Leistd:GlobalException`。
- HTTP 状态码取自错误码前 3 位，须落在 `[100, 600)`，否则回退 500；因此自定义 `WithCode` 时应保证前缀语义正确。
- 中间件设置了 `AllowStatusCode404Response = true`，并对客户端主动断开的 `OperationCanceledException` 及所有 `BusinessException` 抑制 diagnostics 日志（.NET 10 `SuppressDiagnosticsCallback`）。
