# 统一 API 响应

统一 Web API 的返回结构，让前端用同一套逻辑解析成功与失败。`Result` / `Result<T>` 是响应模型，ASP.NET Core 侧的结果过滤器自动把控制器返回的普通对象包装成统一响应——业务代码照常 `return data`。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 只需要统一响应的数据模型（如在应用层/领域层构造返回结构、跨项目共享契约） | 只引用 `Leistd.Response.Core` |
| ASP.NET Core Web API 需要自动包装控制器返回值 | 引用 `Leistd.Response.AspNetCore` 并注册过滤器 |
| 个别接口（如文件下载、第三方回调、健康检查）不希望被包装 | 在 action 或 controller 上标注 `[NoWrap]` |
| 想显式构造成功/失败响应而非依赖自动包装 | 使用 `OkResult` / `FailResult` 等 Controller 扩展方法 |

错误形状二选一：要么全程抛业务异常，由[异常处理组件](./exception-handling.md)输出 RFC 9457 Problem Details；要么全程用 `FailResult` 输出信封。抛出的异常不经过本组件，两种混用会让同一个服务出现两种错误形状。

## 安装

```bash
# 抽象与响应模型（可单独引用）
dotnet add package Leistd.Response.Core

# ASP.NET Core 集成（自动包装过滤器、Controller 扩展、NoWrap 特性）
dotnet add package Leistd.Response.AspNetCore
```

`Leistd.Response.AspNetCore` 已通过项目引用传递依赖 `Leistd.Response.Core`，Web 项目通常只需添加前者。

## 注册

在 `Program.cs` 的 MVC 链上挂载：

```csharp
builder.Services.AddControllers()
    .AddJsonOptions(options => { /* 宿主自己的序列化配置 */ })
    .AddResponseWrapper();
```

`AddResponseWrapper` 把 `ResultWrapperFilter`（一个 `IAsyncResultFilter`）加入 MVC 过滤器管线。注册后，控制器返回的普通对象会被自动包装为 `Result<object?>`。

> 它是 `IMvcBuilder` 扩展而不是 `IServiceCollection` 扩展：MVC 由宿主组装，组件不替宿主调 `AddControllers()`。

## 使用

注册过滤器后，控制器可以直接返回业务对象，框架自动包装：

```csharp
[ApiController]
[Route("api/users")]
public class UserController(IUserService userService) : ControllerBase
{
    // 直接返回数据，过滤器自动包成 Result<object?>
    // 客户端收到：{ "code": 0, "message": null, "data": { ... } }
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAsync(long id)
        => Ok(await userService.GetAsync(id));

    // 不希望被包装的接口（如导出文件），标注 NoWrap
    [HttpGet("export")]
    [NoWrap]
    public IActionResult Export() => File(bytes, "text/csv", "users.csv");
}
```

也可以用 Controller 扩展方法显式构造响应，尤其是返回失败时：

```csharp
public class OrderController(IOrderService service) : ControllerBase
{
    [HttpPost]
    public IActionResult Create(CreateOrderInput input)
    {
        if (!ModelState.IsValid)
            // HTTP 状态码显式给出；信封式契约要求恒为 200 时这里传 200
            return this.FailResult(400, 40001, "参数不合法");

        var order = service.Create(input);
        return this.OkResult(order, "下单成功"); // 包成 Result<Order>，HTTP 200
    }
}
```

## 接口参考

`Leistd.Response.Wrappers` 命名空间（响应模型，均为 `record`）：

| 成员 | 说明 |
| --- | --- |
| `Result` | 统一响应基类型，包含 `Code` 与 `Message`；`Code = 0` 表示成功 |
| `Result.Ok(message?)` | 构造成功响应，`Code = 0` |
| `Result.Fail(code, message)` | 构造失败响应，指定业务码与消息 |
| `Result<T>` | 带数据负载的响应，继承 `Result`，新增 `Data`（失败时为 `null`） |
| `Result<T>.Ok(data, message?)` | 构造带数据的成功响应，`Code = 0` |
| `Result<T>.Fail(code, message?)` | 构造带数据类型但无数据的失败响应（`new` 隐藏基类同名方法） |
| `ErrorResult` | 带字段级错误明细的失败响应，继承 `Result`，新增 `Errors` |
| `ErrorResult.Fail(code, message, errors)` | 构造含 `Errors`（`IReadOnlyList<ErrorItem>`）的失败响应 |

`ErrorItem` 由 `Leistd.ExceptionHandling.Core` 定义（`Leistd.Response.Core` 已传递引用），是框架内唯一的字段错误形状：Problem Details 的 `errors`、本信封的 `errors`、服务客户端的 `RemoteServiceException.Errors` 用的都是它。

`Leistd.Response.AspNetCore` 命名空间（ASP.NET Core 集成）：

| 成员 | 说明 |
| --- | --- |
| `AddResponseWrapper(mvcBuilder)` | 把 `ResultWrapperFilter` 挂到宿主的 MVC 链（`IMvcBuilder` 扩展方法） |
| `NoWrapAttribute`（`[NoWrap]`） | 标注在 action 或 controller 上跳过自动包装；`AttributeUsage = Method \| Class` |
| `ControllerExtensions.OkResult<T>(data, message?)` | 返回 HTTP 200 的 `Result<T>` 成功响应 |
| `ControllerExtensions.OkResult(message?)` | 返回 HTTP 200 的无数据 `Result` 成功响应 |
| `ControllerExtensions.FailResult(statusCode, code, message)` | 返回失败响应，HTTP 状态码显式给出 |
| `ControllerExtensions.FailResultWithErrors(statusCode, code, message, errors)` | 返回带 `Errors` 明细的 `ErrorResult` 失败响应 |

> Controller 扩展方法均为 `this ControllerBase` 扩展，调用时写作 `this.OkResult(...)`。

## 实现行为

### Leistd.Response.AspNetCore（自动包装过滤器）

- `ResultWrapperFilter` 仅包装满足以下全部条件的结果：结果为 `ObjectResult`、其 `Value` **不是** `Result`（避免重复包装）、且 HTTP 状态码为 `null` 或落在 **200–299** 区间（即只包装成功响应）。
- 命中包装时，原值被包成 `Result<object?>.Ok(value)`，状态码保留原值（无则取 200）；包装时输出一条 `Debug` 级日志。
- 标注了 `[NoWrap]`（通过 `EndpointMetadata` 检测）的接口直接放行，不做包装。

## 注意事项

- 自动包装只作用于成功（2xx）的 `ObjectResult`。非 2xx 状态码，以及 `FileResult`、`StatusCodeResult`、`ContentResult` 等非 `ObjectResult` 不会被自动包装；如需失败响应的统一结构，请显式使用 `FailResult` / `FailResultWithErrors`。
- HTTP 状态码由调用方显式传入，框架不从业务码推导，业务错误码怎么编由宿主自己定。
- `Result.Code = 0` 约定表示成功；失败时由调用方指定非 0 业务码。
- 返回值本身已是 `Result`（如自行调用 `OkResult` / `FailResult`）时不会被二次包装，可放心混用自动包装与显式构造。
