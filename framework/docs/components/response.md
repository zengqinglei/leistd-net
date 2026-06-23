# 统一 API 响应

Web API 通常需要一个一致的返回结构，让前端无论成功失败都能用同一套逻辑解析：业务状态码、提示消息、数据负载、字段级错误。如果每个接口各写各的，前端就要为每个接口适配不同形状，错误处理也无从统一。

Leistd 通过 `Result` / `Result<T>` 这套响应模型统一返回结构，并在 ASP.NET Core 侧提供一个结果过滤器，自动把控制器返回的普通对象包装成统一响应——业务代码可以照常 `return data`，框架负责包装，无需手写包装代码。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 只需要统一响应的数据模型（如在应用层/领域层构造返回结构、跨项目共享契约） | 只引用 `Leistd.Response.Core` |
| ASP.NET Core Web API 需要自动包装控制器返回值 | 引用 `Leistd.Response.AspNetCore` 并注册过滤器 |
| 个别接口（如文件下载、第三方回调、健康检查）不希望被包装 | 在 action 或 controller 上标注 `[NoWrap]` |
| 想显式构造成功/失败响应而非依赖自动包装 | 使用 `OkResult` / `FailResult` 等 Controller 扩展方法 |

## 安装

```bash
# 抽象与响应模型（可单独引用）
dotnet add package Leistd.Response.Core

# ASP.NET Core 集成（自动包装过滤器、Controller 扩展、NoWrap 特性）
dotnet add package Leistd.Response.AspNetCore
```

`Leistd.Response.AspNetCore` 已通过项目引用传递依赖 `Leistd.Response.Core`，Web 项目通常只需添加前者。

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

在 `Program.cs` 注册响应包装过滤器：

```csharp
// 内部调用 AddControllers 并注册全局结果过滤器 ResultWrapperFilter
builder.Services.AddResponseWrapper();
```

`AddResponseWrapper` 会把 `ResultWrapperFilter`（一个 `IAsyncResultFilter`）加入 MVC 过滤器管线。注册后，控制器返回的普通对象会被自动包装为 `Result<object?>`。

> `AddResponseWrapper` 已内部调用 `AddControllers`，无需再单独调用。

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
            // code 的前三位作为 HTTP 状态码（此处 400），整体作为业务码
            return this.FailResult(40001, "参数不合法");

        var order = service.Create(input);
        return this.OkResult(order, "下单成功"); // 包成 Result<Order>，HTTP 200
    }
}
```

## 接口参考

`Leistd.Response.Core.Wrapper` 命名空间（响应模型，均为 `record`）：

| 成员 | 说明 |
| --- | --- |
| `Result` | 统一响应基类型；含 `Code`、`Message`、可写的 `Details`。`Code = 0` 约定为成功 |
| `Result.Ok(message?)` | 构造成功响应，`Code = 0` |
| `Result.Fail(code, message)` | 构造失败响应，指定业务码与消息 |
| `Result<T>` | 带数据负载的响应，继承 `Result`，新增 `Data`（失败时为 `null`） |
| `Result<T>.Ok(data, message?)` | 构造带数据的成功响应，`Code = 0` |
| `Result<T>.Fail(code, message?)` | 构造带数据类型但无数据的失败响应（`new` 隐藏基类同名方法） |
| `ErrorResult` | 带字段级错误明细的失败响应，继承 `Result`，新增 `Errors` |
| `ErrorResult.Fail(code, message, errors)` | 构造含 `Errors`（`List<Dictionary<string,string>>`）的失败响应 |

`Leistd.Response.AspNetCore` 命名空间（ASP.NET Core 集成）：

| 成员 | 说明 |
| --- | --- |
| `AddResponseWrapper(services)` | 注册 `ResultWrapperFilter` 到 MVC 管线（`DependencyInjection` 扩展方法） |
| `NoWrapAttribute`（`[NoWrap]`） | 标注在 action 或 controller 上跳过自动包装；`AttributeUsage = Method \| Class` |
| `ControllerExtensions.OkResult<T>(data, message?)` | 返回 HTTP 200 的 `Result<T>` 成功响应 |
| `ControllerExtensions.OkResult(message?)` | 返回 HTTP 200 的无数据 `Result` 成功响应 |
| `ControllerExtensions.FailResult(code, message)` | 返回失败响应，HTTP 状态码由 `code` 前三位推导 |
| `ControllerExtensions.FailResultWithErrors(code, message, errors)` | 返回带 `Errors` 明细的 `ErrorResult` 失败响应 |

> Controller 扩展方法均为 `this ControllerBase` 扩展，调用时写作 `this.OkResult(...)`。

## 实现行为

### Leistd.Response.AspNetCore（自动包装过滤器）

- `ResultWrapperFilter` 仅包装满足以下全部条件的结果：结果为 `ObjectResult`、其 `Value` **不是** `Result`（避免重复包装）、且 HTTP 状态码为 `null` 或落在 **200–299** 区间（即只包装成功响应）。
- 命中包装时，原值被包成 `Result<object?>.Ok(value)`，状态码保留原值（无则取 200）；包装时输出一条 `Debug` 级日志。
- 标注了 `[NoWrap]`（通过 `EndpointMetadata` 检测）的接口直接放行，不做包装。
- `FailResult` / `FailResultWithErrors` 的 HTTP 状态码由业务 `code` 的**前三位**整数推导：取字符串前三位解析为整数，若落在 100–599 区间则采用，否则回退为 **500**。例如 `code = 40001` 对应 HTTP 400。

## 配置项 / Options

当前无配置项。过滤器的包装条件（仅包装 2xx、跳过已是 `Result` 的值）与 `FailResult` 的状态码推导规则均为源码固定行为，未提供 Options 配置。

## 注意事项

- 自动包装只作用于成功（2xx）的 `ObjectResult`。非 2xx 状态码，以及 `FileResult`、`StatusCodeResult`、`ContentResult` 等非 `ObjectResult` 不会被自动包装；如需失败响应的统一结构，请显式使用 `FailResult` / `FailResultWithErrors`。
- `FailResult` 依赖 `code` 前三位作为 HTTP 状态码，因此业务错误码需遵循「前三位为 HTTP 状态码」的约定，否则将回退为 500。
- `Result.Code = 0` 约定表示成功；失败时由调用方指定非 0 业务码。
- 返回值本身已是 `Result`（如自行调用 `OkResult` / `FailResult`）时不会被二次包装，可放心混用自动包装与显式构造。

## 相关

- [组件总览](./README.md)
- [依赖注入](./dependency-injection.md)
