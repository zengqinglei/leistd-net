# 业务异常与全局异常处理

在 Web 应用里，抛错处处都有：参数非法、资源不存在、权限不足、上游超时……如果每个 Controller 都自己 `try/catch` 再拼装错误响应，会产生大量重复代码，且响应格式难以统一。Leistd 的做法是：业务层只管按语义**抛出**强类型异常，由一个全局处理器在管道末端统一**捕获**，转换成符合 [RFC 7807 ProblemDetails](https://datatracker.ietf.org/doc/html/rfc7807) 的标准错误响应。

`Leistd.Exception.Core` 提供一组面向 HTTP 语义的业务异常（`BadRequestException`、`NotFoundException`、`UnprocessableEntityException` 等），每个异常自带一个错误码，错误码前三位即对应 HTTP 状态码。`Leistd.Exception.AspNetCore` 提供 ASP.NET Core 的全局异常处理器，把这些异常（以及框架内置异常）映射为带 `code` / `traceId` / `message` 扩展字段的 ProblemDetails。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 领域/应用服务中按业务语义抛错（找不到、无权限、校验失败等） | 抛出 `Leistd.Exception.Core` 中对应异常 |
| 表单/实体字段校验，需返回逐字段错误列表 | 抛出 `UnprocessableEntityException` |
| Web API 需要把异常统一转换成标准错误响应 | 引用 `Leistd.Exception.AspNetCore` 并启用全局处理器 |
| 只在领域/类库层引用异常类型，不依赖 ASP.NET Core | 只引用 `Leistd.Exception.Core` |

> 业务异常类型在 `Leistd.Exception.Core`，可被任意层引用；全局处理器在 `Leistd.Exception.AspNetCore`，仅 Web 宿主项目需要。

## 安装

```bash
# 业务异常类型（领域/应用层引用）
dotnet add package Leistd.Exception.Core

# 全局异常处理器（Web 宿主项目引用，已传递引用 Core）
dotnet add package Leistd.Exception.AspNetCore
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

在 `Program.cs` 注册全局异常处理器，并把中间件接入请求管道：

```csharp
// 方式一：从配置节 Leistd:GlobalException 绑定 Options
builder.Services.AddGlobalExceptionHandler(builder.Configuration);

// 方式二：用委托直接配置 Options
builder.Services.AddGlobalExceptionHandler(options =>
{
    options.Enable = true;
    options.ExcludePatterns.Add("/api/health/**");
});

var app = builder.Build();

// 接入异常处理中间件（应尽量靠前）
app.UseGlobalExceptionHandler();
```

`AddGlobalExceptionHandler` 内部调用 `AddProblemDetails()`、绑定 `GlobalExceptionOptions`，并把 `BusinessExceptionHandler` 注册为 `IExceptionHandler`。`UseGlobalExceptionHandler` 包装 `UseExceptionHandler`，并对客户端主动断开（`OperationCanceledException`）与 `BusinessException` 抑制框架的诊断日志（由处理器自行记录）。

## 使用

业务代码只需 `throw` 语义对应的异常，无需关心响应格式：

```csharp
public class OrderService(IOrderRepository repo)
{
    public async Task<Order> GetAsync(long id)
    {
        var order = await repo.FindAsync(id);
        if (order is null)
            throw new NotFoundException($"订单 {id} 不存在");   // -> HTTP 404
        return order;
    }

    public async Task PayAsync(long id, decimal amount)
    {
        if (amount <= 0)
            // 链式覆盖错误码 + 附加 details
            throw new BadRequestException("金额必须大于 0")
                .WithCode("01")        // Code => 40001
                .WithDetails("amount<=0");
        // ...
    }
}
```

字段校验场景使用 `UnprocessableEntityException`，可逐字段累加错误，处理器会输出为 `ValidationProblemDetails`（HTTP 422）：

```csharp
throw new UnprocessableEntityException("email", "邮箱格式不正确")
    .AddError("password", "密码至少 8 位")
    .AddErrors("phone", "号码非法", "号码已被占用");
```

抛出后，全局处理器自动产出如下结构的响应（节选）：

```json
{
  "title": "Not Found",
  "status": 404,
  "detail": "订单 1 不存在",
  "code": 40400,
  "traceId": "00-...",
  "message": "订单 1 不存在"
}
```

## 接口参考

`Leistd.Exception.Core` 命名空间（业务异常均继承自 `BusinessException`）：

| 成员 | 说明 |
| --- | --- |
| `BusinessException` | 抽象基类，承载 `Code` / `Details`，是所有业务异常的父类 |
| `BusinessException.Code` | 错误码，构造时为「前缀 + 00」（如 `404` → `40400`） |
| `BusinessException.Details` | 附加详情，可选 |
| `BusinessException.WithCode(code)` | 链式覆盖错误码为「前缀 + code」，返回自身 |
| `BusinessException.WithDetails(details)` | 链式设置详情，返回自身 |
| `BadRequestException(message)` | 请求错误，错误码 `40000`（HTTP 400） |
| `UnauthorizedException(message)` | 未授权，错误码 `40100`（HTTP 401） |
| `ForbiddenException(message)` | 禁止访问，错误码 `40300`（HTTP 403） |
| `NotFoundException(message)` | 资源未找到，错误码 `40400`（HTTP 404） |
| `UnsupportedMediaTypeException(message)` | 不支持的媒体类型，错误码 `41500`（HTTP 415） |
| `UnprocessableEntityException(...)` | 实体校验失败，错误码 `42200`（HTTP 422），承载逐字段错误 |
| `InternalServerException(message)` | 服务器内部错误，错误码 `50000`（HTTP 500） |
| `ServiceUnavailableException(message)` | 服务不可用，错误码 `50300`（HTTP 503） |

`UnprocessableEntityException` 额外成员：

| 成员 | 说明 |
| --- | --- |
| `ValidationErrors` | `Dictionary<string,string[]>?`，符合 RFC 7807 的逐字段错误集合 |
| `WithErrors(dict)` | 整体替换错误集合，返回自身 |
| `AddError(field, error)` | 追加某字段的单条错误，返回自身 |
| `AddErrors(field, params errors)` | 追加某字段的多条错误，返回自身 |
| 构造 `(field, error)` / `(field, errors)` | 便捷构造单字段错误 |

> 业务异常的基类链为 `BusinessException` → `CommonException`（位于 `Leistd.Exception` 命名空间，由 `Leistd.Core` 提供）→ `System.Exception`。

`Leistd.Exception.AspNetCore` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `AddGlobalExceptionHandler(configuration)` | 从 `Leistd:GlobalException` 配置节绑定 Options 并注册处理器 |
| `AddGlobalExceptionHandler(configure)` | 用委托配置 Options 并注册处理器 |
| `UseGlobalExceptionHandler()` | 接入异常处理中间件 |
| `BusinessExceptionHandler` | `IExceptionHandler` 实现，执行异常到 ProblemDetails 的转换 |

## 实现行为

### Leistd.Exception.AspNetCore（全局处理器）

- **开关与排除**：`Options.Enable` 为 `false`（默认）时处理器直接放行（返回 `false`，交回框架）。`ExcludePatterns` 命中的路径同样放行；模式支持 `前缀/**` 与含 `*` 的通配匹配，匹配大小写不敏感。
- **异常归一化**：非 `BusinessException` 的异常会被映射——`System.ComponentModel.DataAnnotations.ValidationException` → `UnprocessableEntityException`；`CommonException` → `BadRequestException`；`OperationCanceledException`（内含 `TimeoutException`）/ `TimeoutException` / `HttpRequestException` → `ServiceUnavailableException`；其余 → `InternalServerException`。
- **状态码推导**：取 `Code` 的前 3 位作为 HTTP 状态码（须落在 100–599），否则回退 500。
- **响应体**：标准异常输出 `ProblemDetails`，`UnprocessableEntityException` 输出 `ValidationProblemDetails`；两者都在 `Extensions` 中写入 `message`、`traceId`（取 `Activity.Current?.Id`，否则 `TraceIdentifier`）、`code`。
- **详情可见性**：由 `IsShowDetails` 决定（`null` 时按是否开发环境）。可见时写入 `details` 或 `stackTrace`；不可见时仅在有 `Details` 的情况下写入 `details`。
- **日志**：`InternalServerException` 记为 `LogError`（含原始异常堆栈），其余业务异常记为 `LogWarning`。

## 配置项 / Options

`GlobalExceptionOptions`（配置节 `Leistd:GlobalException`）：

| 属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `Enable` | `bool` | `false` | 是否启用全局异常处理，**默认关闭，需显式开启** |
| `ExcludePatterns` | `HashSet<string>` | 空 | 跳过处理的路径模式，如 `/api/health/**` |
| `IsShowDetails` | `bool?` | `null` | 是否在响应中显示详情；`null` 时按是否开发环境自动决定 |

## 注意事项

- `Enable` 默认 **`false`**，注册后若不显式置为 `true`，处理器不会接管异常。
- `WithCode` 传入的是「码后缀」而非完整错误码——`new BadRequestException(...).WithCode("01")` 得到的 `Code` 是 `40001`，而非 `01`。
- 错误码内部用 `int.Parse(前缀 + 后缀)` 计算，传入非数字字符串会抛 `FormatException`。
- 中间件 `UseGlobalExceptionHandler` 应尽量靠近管道前端，以便捕获后续中间件抛出的异常。
- `CommonException` 位于 `Leistd.Exception` 命名空间（`Leistd.Core` 提供），与业务异常所在的 `Leistd.Exception.Core` 命名空间不同，引用时注意区分。

## 相关

- [组件总览](./README.md)
- [核心基础库](./core.md)
