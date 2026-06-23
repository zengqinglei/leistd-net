# 统一响应（`response`）
> 为 ASP.NET Core 接口提供统一的 `{ code, message, data }` 响应结构与自动包装。

## 包

| 包 | 角色 | 何时引用 |
| --- | --- | --- |
| `Leistd.Response.Core` | 响应模型（`Result` / `Result<T>` / `ErrorResult`），不依赖 Web 框架 | 任何需要构造统一响应对象的项目（含类库） |
| `Leistd.Response.AspNetCore` | DI 扩展、自动包装过滤器、Controller 扩展方法 | ASP.NET Core Web 项目 |

## 核心抽象

均位于命名空间 `Leistd.Response.Core.Wrapper`，全部为 `record`。

```csharp
public record Result { int Code; string? Message; string? Details; }
```
统一响应基类。`Code` 为 0 表示成功，非 0 表示业务/错误码。`Details` 可写，用于附加详情。

```csharp
public static Result Result.Ok(string? message = null)
```
构造成功结果（`Code = 0`）。

```csharp
public static Result Result.Fail(int code, string message)
```
构造失败结果，指定错误码与消息。

```csharp
public record Result<T> : Result { T? Data; }
```
带数据的响应。`Data` 在失败时为 `null`。

```csharp
public static Result<T> Result<T>.Ok(T data, string? message = null)
public static Result<T> Result<T>.Fail(int code, string? message = null)  // new，隐藏基类同名方法
```
分别构造成功（携带 `Data`）与失败（不带 `Data`）结果。

```csharp
public record ErrorResult : Result { List<Dictionary<string, string>>? Errors; }
public static ErrorResult ErrorResult.Fail(int code, string message, List<Dictionary<string, string>> errors)
```
带结构化错误明细的失败结果，`Errors` 为键值对列表（适用于字段级校验错误）。

## 能力实现

### `Leistd.Response.AspNetCore`

命名空间 `Leistd.Response.AspNetCore`。

- **DI 注册**：`IServiceCollection.AddResponseWrapper()` —— 调用 `AddControllers` 并注册全局结果过滤器 `ResultWrapperFilter`。
- **自动包装**（`ResultWrapperFilter`，实现 `IAsyncResultFilter`）：对返回的 `ObjectResult` 自动包装为 `Result<object?>.Ok(...)`，仅当满足以下条件：
  - Action / Controller 未标注 `NoWrapAttribute`；
  - 返回值本身不是 `Result`（即不重复包装）；
  - HTTP 状态码为空或处于 `200`–`299`（非 2xx 不包装，由原始结果直接返回）。
- **禁用包装**：`Leistd.Response.AspNetCore.Attributes.NoWrapAttribute`，可标注在方法或类上（`AttributeUsage = Method | Class`）。
- **Controller 扩展方法**（`Leistd.Response.AspNetCore.Extensions.ControllerExtensions`，`this ControllerBase`）：
  - `OkResult<T>(T data, string? message = null)` —— 返回 200 + `Result<T>.Ok`。
  - `OkResult(string? message = null)` —— 返回 200 + `Result.Ok`。
  - `FailResult(int code, string message)` —— 返回失败结果，HTTP 状态码由错误码推导。
  - `FailResultWithErrors(int code, string message, List<Dictionary<string,string>> errors)` —— 返回 `ErrorResult`，HTTP 状态码由错误码推导。

> HTTP 状态码推导规则：取错误码字符串的前 3 位解析为整数，若落在 `100`–`599` 区间则用作 HTTP 状态码，否则回退为 `500`。例如错误码 `40001` 对应 HTTP `400`。

## 最小可用示例

```csharp
using Leistd.Response.AspNetCore;            // AddResponseWrapper
using Leistd.Response.AspNetCore.Attributes; // NoWrapAttribute
using Leistd.Response.AspNetCore.Extensions; // OkResult / FailResult
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddResponseWrapper();       // 注册全局自动包装
var app = builder.Build();
app.MapControllers();
app.Run();

[ApiController]
[Route("users")]
public class UserController : ControllerBase
{
    // 直接返回业务对象，过滤器自动包装为 { code:0, message:null, data:{...} }
    [HttpGet("{id}")]
    public IActionResult Get(int id) => Ok(new { Id = id, Name = "Alice" });

    // 显式构造失败响应：错误码 40001 -> HTTP 400
    [HttpGet("fail")]
    public IActionResult Fail() => this.FailResult(40001, "参数错误");

    // 跳过自动包装，原样返回
    [HttpGet("raw"), NoWrap]
    public IActionResult Raw() => Ok("plain text");
}
```

## 依赖

无 Leistd 内部组件依赖（仅依赖 ASP.NET Core 框架）。

## 备注

- `AddResponseWrapper` 内部调用 `AddControllers`，无需再单独调用；若已自行配置 controllers，注意避免重复注册导致行为叠加。
- 自动包装仅作用于 `ObjectResult`（如 `Ok(value)`）；返回 `StatusCodeResult`、`FileResult` 等非 `ObjectResult` 不会被包装。
- 非 2xx 响应不会被自动包装，需要统一错误结构时使用 `FailResult` / `FailResultWithErrors` 显式返回。
