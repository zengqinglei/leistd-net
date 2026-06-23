# 链路追踪

在分布式或多层调用的系统里，一次请求往往横跨多个服务、多个异步任务。当出现异常时，如果日志里没有一根贯穿始终的「线」，就很难把分散在各处的日志拼回同一次请求。链路追踪通过为每次请求生成一个全局唯一的 **TraceId（CorrelationId）**，并让它随上下文、日志、下游 HTTP 调用一路传递，从而把整条调用链的日志串联起来。

Leistd 以 `ICorrelationIdProvider` 为核心抽象，基于 `AsyncLocal` 在异步链路中保存当前 TraceId；并提供三个层面的接入点：ASP.NET Core 中间件（入口）、`HttpClient` 处理器（出口转发）、以及 `[CorrelationId]` 特性 + AOP 拦截器（无入口请求的后台任务/方法）。日志键名与 Java 版保持一致（`leistd.correlationId.traceId`），便于跨语言系统对齐。

## 何时使用

| 场景 | 接入方式 | 包 |
| --- | --- | --- |
| ASP.NET Core Web 应用，需从请求头读取/生成 TraceId 并写回响应 | `UseCorrelationId()` 中间件 | `Leistd.Tracing.AspNetCore` |
| 通过 `HttpClient` 调用下游服务，需把当前 TraceId 透传过去 | `AddCorrelationIdForwarding()` | `Leistd.Tracing.HttpClient` |
| 后台任务、消息消费等无 HTTP 入口的场景，需为方法自动开启 TraceId 作用域 | `[CorrelationId]` 特性 + AOP | `Leistd.Tracing.Core` |
| 业务代码只需读取/切换当前 TraceId | 注入 `ICorrelationIdProvider` | `Leistd.Tracing.Core` |

> Core 是抽象与核心实现所在包，AspNetCore 与 HttpClient 均传递引用 Core。Web 应用通常同时引用 AspNetCore（入口）与 HttpClient（出口）。

## 安装

```bash
# 核心：Provider、特性、AOP 拦截器（业务代码与后台任务）
dotnet add package Leistd.Tracing.Core

# ASP.NET Core 入口中间件（会传递引用 Core）
dotnet add package Leistd.Tracing.AspNetCore

# HttpClient 出口转发（会传递引用 Core）
dotnet add package Leistd.Tracing.HttpClient
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

### Web 应用（推荐）

在 `Program.cs` 注册并启用中间件：

```csharp
// 注册核心服务 + 从配置节 "Leistd:CorrelationId" 绑定 Options
builder.Services.AddCorrelationId(builder.Configuration);
// 或用委托配置
// builder.Services.AddCorrelationId(o => o.HttpHeaderName = "X-Correlation-Id,X-Request-Id");

var app = builder.Build();

// 尽量靠前注册：从请求头解析或生成 TraceId，写入上下文与日志 Scope
app.UseCorrelationId();
```

`AddCorrelationId` 内部调用 `AddCorrelationIdCore`，注册：
- `ICorrelationIdProvider` → `CorrelationIdProvider`（Singleton）
- `CorrelationIdInterceptor`（Transient）与 `IProxyGenerator`，并通过 `OnServiceRegistered` 回调为带 `[CorrelationId]` 特性的服务挂载 AOP 拦截器。

### 非 Web 宿主

若没有 ASP.NET Core 入口（如 Worker Service），直接注册核心服务：

```csharp
services.AddCorrelationIdCore(configuration);
// 或 services.AddCorrelationIdCore(o => o.Enable = true);
```

### HttpClient 出口转发

在配置具名/类型化 `HttpClient` 时追加转发处理器，把当前 TraceId 写入出站请求头：

```csharp
builder.Services.AddHttpClient<MyApiClient>()
    .AddCorrelationIdForwarding();
```

## 使用

### 注入 Provider 读取/切换 TraceId

```csharp
public class OrderService(ICorrelationIdProvider correlationId)
{
    public void DoWork()
    {
        var current = correlationId.Get(); // 可能为 null（上下文未初始化时）

        // 临时切换上下文，using 离开作用域时自动恢复为原值
        using (correlationId.Change(correlationId.Create()))
        {
            // —— 此作用域内 Get() 返回新生成的 TraceId ——
        }
    }
}
```

### 后台任务用特性自动开启作用域

对没有 HTTP 入口的方法/类标注 `[CorrelationId]`，AOP 拦截器会在进入时（若当前无 TraceId）自动生成并开启日志 Scope：

```csharp
[CorrelationId]
public class ReportJob
{
    public virtual async Task RunAsync()
    {
        // 进入时自动拥有 TraceId，整条链路的日志都会带上它
    }
}
```

> AOP 依赖动态代理，被拦截的方法需为 `virtual`，且类须经容器解析（Leistd 依赖注入扫描会自动挂载拦截器）。

## 接口参考

`Leistd.Tracing.Core` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `ICorrelationIdProvider` | 链路追踪核心抽象（`Leistd.Tracing.Core.Services`） |
| `ICorrelationIdProvider.Get()` | 返回当前上下文的 TraceId；上下文未初始化时返回 `null` |
| `ICorrelationIdProvider.Create()` | 生成新的 TraceId（32 位无连字符 UUID，`Guid.ToString("N")`） |
| `ICorrelationIdProvider.Change(correlationId)` | 切换当前上下文 TraceId，返回 `IDisposable`；`Dispose` 时恢复为切换前的值 |
| `CorrelationIdProvider` | 默认实现，基于 `AsyncLocal<string?>` 保存上下文（Singleton） |
| `[CorrelationId]` | 标注在方法或类上（`Leistd.Tracing.Core.Attributes`），触发 AOP 自动开启 TraceId 作用域 |
| `CorrelationIdConstants.TraceIdLogKey` | 日志 Scope 中的 TraceId 键名，常量值 `leistd.correlationId.traceId` |

DI 扩展方法：

| 方法 | 所在包 | 说明 |
| --- | --- | --- |
| `AddCorrelationIdCore(IConfiguration)` / `AddCorrelationIdCore(Action<CorrelationIdOptions>)` | Core | 注册 Provider、AOP 拦截器，并绑定 Options |
| `AddCorrelationId(IConfiguration)` / `AddCorrelationId(Action<CorrelationIdOptions>)` | AspNetCore | 内部调用 `AddCorrelationIdCore` 注册核心服务 |
| `UseCorrelationId()` | AspNetCore | 注册 `CorrelationIdMiddleware` 中间件 |
| `AddCorrelationIdForwarding()` | HttpClient | 在 `IHttpClientBuilder` 上挂载 `CorrelationIdDelegatingHandler` |

## 实现行为

### Leistd.Tracing.Core（核心）

- `CorrelationIdProvider` 用 `AsyncLocal<string?>` 保存当前 TraceId，可在异步调用链中传播；以 Singleton 注册。
- `Change` 返回的 `IDisposable` 仅恢复上一层值（保存 parent 并在 `Dispose` 时还原），可安全嵌套。
- `CorrelationIdInterceptor` 优先级 `Order = -1000`（最外层），确保 TraceId 作用域在 UnitOfWork 等其他拦截器之前建立，覆盖整条链路。
- 拦截器逻辑：若当前上下文**已有** TraceId 则不重复创建（直接放行）；否则 `Create()` 新 ID，同时开启 `ICorrelationIdProvider.Change` 与 `ILogger.BeginScope`。

### Leistd.Tracing.AspNetCore（入口中间件）

- `CorrelationIdMiddleware` 按 `Options.GetHttpHeaderNames()` 顺序从请求头读取首个非空 TraceId；都没有则调用 `Create()` 生成。
- 同时写入 Provider 上下文与日志 Scope（键 `TraceIdLogKey`）。
- 当 `SetResponseHeader = true` 时，通过 `Response.OnStarting` 把 TraceId 回写到尚不存在的同名响应头。
- `Options.Enable = false` 时中间件直接放行，不做任何处理。

### Leistd.Tracing.HttpClient（出口转发）

- `CorrelationIdDelegatingHandler` 在发送出站请求前，取当前 TraceId 写入所有配置的 Header（仅当请求中尚不包含该 Header 时追加）。
- 当前 TraceId 为 `null`/空，或 `Options.Enable = false` 时，不附加任何头，直接转发。

## 配置项 / Options

`CorrelationIdOptions`（`Leistd.Tracing.Core.Options`），绑定配置节 `Leistd:CorrelationId`：

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `Enable` | `true` | 是否启用链路追踪；`false` 时中间件与 HttpClient 处理器均不处理 |
| `HttpHeaderName` | `X-Correlation-Id` | TraceId 的请求/响应头名；支持逗号分隔多个，如 `X-Correlation-Id,X-Request-Id` |
| `SetResponseHeader` | `true` | 是否将 TraceId 回写到响应头 |

```json
{
  "Leistd": {
    "CorrelationId": {
      "Enable": true,
      "HttpHeaderName": "X-Correlation-Id,X-Request-Id",
      "SetResponseHeader": true
    }
  }
}
```

## 注意事项

- `Get()` 在上下文尚未初始化时返回 `null`，业务代码读取后需判空。
- `Change()` 返回的 `IDisposable` 必须 `using`/释放，否则上下文不会恢复，可能污染同一异步流后续逻辑。
- `[CorrelationId]` 走动态代理 AOP，被拦截方法需 `virtual` 且经容器解析；直接 `new` 出来的实例不会被拦截。
- TraceId 作用域具有「就近优先」语义：若链路上层已有 TraceId，中间件/拦截器都不会覆盖，保证一次请求只用一个 ID。
- 日志键名 `leistd.correlationId.traceId` 与 Java 版一致，但要在日志里看到它，需日志库（如 Serilog）启用 Scope 富化。

## 相关

- [组件总览](./README.md)
- [依赖注入](./dependency-injection.md)
