# 链路追踪

链路追踪在没有显式关联标识时复用 `Activity.TraceId`，否则沿用显式值，并在上下文、日志与下游 HTTP 调用间传递。

## 何时使用

| 场景 | 接入方式 | 包 |
| --- | --- | --- |
| ASP.NET Core Web 应用，需从请求头读取/生成 TraceId 并写回响应 | `UseCorrelationId()` 中间件 | `Leistd.Tracing.AspNetCore` |
| 通过 `HttpClient` 调用下游服务，需把当前 TraceId 透传过去 | `AddCorrelationIdForwarding()` | `Leistd.Tracing.HttpClient` |
| 后台任务、消息消费等无 HTTP 入口的场景，需为方法自动开启 TraceId 作用域 | `[CorrelationId]` 特性 + AOP | `Leistd.Tracing.Core` |
| 业务代码只需读取/切换当前 TraceId | 注入 `ICorrelationIdProvider` | `Leistd.Tracing.Core` |

## 安装

```bash
dotnet add package Leistd.Tracing.Core
dotnet add package Leistd.Tracing.AspNetCore
dotnet add package Leistd.Tracing.HttpClient
```

## 注册

### Web 应用

在 `Program.cs` 注册并启用中间件：

```csharp
builder.Services.AddCorrelationId(builder.Configuration);

var app = builder.Build();
app.UseCorrelationId();
```

`UseCorrelationId` 应尽量靠前，以便后续日志和中间件共享同一 TraceId。

### 非 Web 宿主

若没有 ASP.NET Core 入口（如 Worker Service），直接注册核心服务：

```csharp
services.AddCorrelationIdCore(configuration);
```

### HttpClient 出口转发

在配置具名/类型化 `HttpClient` 时追加转发处理器，把当前 TraceId 写入出站请求头：

```csharp
builder.Services.AddHttpClient<MyApiClient>()
    .AddCorrelationIdForwarding();
```

## 使用

### 读取和切换 TraceId

```csharp
public class OrderService(ICorrelationIdProvider correlationId)
{
    public void DoWork()
    {
        var current = correlationId.Get();

        using (correlationId.Change(correlationId.Create()))
        {
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
    }
}
```

> AOP 依赖动态代理，被拦截的方法需为 `virtual`，且类须经容器解析（Leistd 依赖注入扫描会自动挂载拦截器）。

## 接口参考

| 成员 | 说明 |
| --- | --- |
| `ICorrelationIdProvider` | 链路追踪核心抽象（`Leistd.Tracing.Services`） |
| `ICorrelationIdProvider.Get()` | 返回当前上下文的 TraceId；上下文未初始化时返回 `null` |
| `ICorrelationIdProvider.Create()` | 有 Activity 时复用其 TraceId，否则用 `ActivityTraceId.CreateRandom()` 生成 W3C TraceId |
| `ICorrelationIdProvider.Change(correlationId)` | 临时切换当前关联标识，返回 `IDisposable`；释放时恢复原值，显式值优先于当前 Activity |
| `CorrelationIdProvider` | 默认实现，基于 `AsyncLocal<string?>` 保存上下文（Singleton） |
| `[CorrelationId]` | 标注在方法或类上（`Leistd.Tracing.Attributes`），触发 AOP 自动开启 TraceId 作用域 |
| `CorrelationIdConstants.TraceIdLogKey` | 日志 Scope 中的 TraceId 键名，常量值 `leistd.correlationId.traceId` |

| 方法 | 所在包 | 说明 |
| --- | --- | --- |
| `AddCorrelationIdCore(IConfiguration)` / `AddCorrelationIdCore(Action<CorrelationIdOptions>)` | Core | 注册 Provider、AOP 拦截器，并绑定 Options |
| `AddCorrelationId(IConfiguration)` / `AddCorrelationId(Action<CorrelationIdOptions>)` | AspNetCore | 内部调用 `AddCorrelationIdCore` 注册核心服务 |
| `UseCorrelationId()` | AspNetCore | 注册 `CorrelationIdMiddleware` 中间件 |
| `AddCorrelationIdForwarding()` | HttpClient | 在 `IHttpClientBuilder` 上挂载 `CorrelationIdDelegatingHandler` |

## 实现行为

- `CorrelationIdProvider` 用 `AsyncLocal` 保存显式切换值；`Change` 可嵌套，释放时恢复上一层值。
- `CorrelationIdInterceptor` 的 `Order` 为 `-1000`，在其他拦截器之前建立 TraceId 和日志作用域；已有 TraceId 时不覆盖。
- 入站中间件按 `HeaderNames` 顺序取首个合法关联标识，缺失时生成，并按配置回写响应头。
- 出站处理器只在请求尚无目标头时转发当前 TraceId。
- 入站与出站每次操作都读取 `IOptionsMonitor<CorrelationIdOptions>.CurrentValue`，配置重载对后续请求生效。

## 配置项（`Leistd:CorrelationId`）

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `Enabled` | `true` | 是否启用链路追踪；`false` 时中间件与 HttpClient 处理器均不处理 |
| `HeaderNames` | `["X-Correlation-Id"]` | TraceId 的请求/响应头名列表；按顺序读取，写入时遍历全部。空白项被删除，名称按大小写不敏感去重，最终为空时启动验证失败 |
| `IncludeInResponseHeaders` | `true` | 是否将 TraceId 回写到已配置的响应头 |

## 与 W3C Trace Context 的关系

`Activity.TraceId` 是 W3C 分布式追踪标识；`X-Correlation-Id` 是可自定义的应用关联标识。两者同时存在时：

- HTTP 入口存在 `Activity` 时，中间件以其 TraceId 选定本次请求标识；调用方应通过 `traceparent` 传递 W3C 上下文。
- 入站头与 `Activity` 不同时，原值仅记录到 `leistd.correlationId.inboundTraceId`。
- 没有 `Activity`（未接入 OpenTelemetry）时才采信入站头；缺失或形态非法则生成新的。
- `Get()` 的优先级为显式 `Change()`、当前 `Activity.TraceId`、无；`Create()` 优先复用 `Activity.TraceId`。
- 中间件同步更新 `HttpContext.TraceIdentifier`，使异常响应、日志和响应头使用同一标识。

入站 `X-Correlation-Id` 是不透明的关联标识，最长 128 个字符，只接受 ASCII 字母、数字、连字符和下划线；它不要求是 W3C TraceId，也不会被改写大小写。非法值会被丢弃并生成新标识，防止日志或响应头注入。W3C 分布式追踪上下文通过 `traceparent` 传递。
异常响应中的 `traceId` 沿用本次选定的应用关联标识；没有请求 Activity 而选用自定义 ID 时，它不一定能直接用于 OpenTelemetry 查询。分布式链路检索应使用 `Activity.TraceId`，或先通过应用日志把关联 ID 与实际链路对应起来。

## 注意事项

- `Get()` 在上下文尚未初始化时返回 `null`，业务代码读取后需判空。
- `Change()` 返回的 `IDisposable` 必须 `using`/释放，否则上下文不会恢复，可能污染同一异步流后续逻辑。
- `[CorrelationId]` 走动态代理 AOP，被拦截方法需 `virtual` 且经容器解析；直接 `new` 出来的实例不会被拦截。
- 显式 `Change()` 的作用域值优先于当前 Activity；跨后台作业恢复关联标识时，即使执行线程已有 Activity 也应沿用恢复值。
- 日志键名 `leistd.correlationId.traceId` 是跨语言约定键，不要按本服务的习惯改写；要在日志里看到它，需日志库（如 Serilog）启用 Scope 富化。

## 相关

- [依赖注入](./dependency-injection.md)
