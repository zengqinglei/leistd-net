# 链路追踪（`tracing`）
> 基于 TraceId（CorrelationId）的全链路标识：通过 AsyncLocal 在进程内传递、自动注入日志 Scope，并在 ASP.NET Core 入站与 HttpClient 出站之间透传。

## 包

| 包 | 角色 | 何时引用 |
| --- | --- | --- |
| `Leistd.Tracing.Core` | 核心抽象与 AOP：TraceId 提供者、`[CorrelationId]` 特性拦截器、Options | 任何需要 TraceId 上下文的项目 |
| `Leistd.Tracing.AspNetCore` | 入站中间件：从请求头读取/生成 TraceId，写入上下文、日志 Scope 与响应头 | Web/API 宿主 |
| `Leistd.Tracing.HttpClient` | 出站转发：`DelegatingHandler` 将当前 TraceId 写入下游请求头 | 通过 `HttpClient` 调用其他服务时 |

## 核心抽象

`ICorrelationIdProvider`（命名空间 `Leistd.Tracing.Core.Services`）—— 当前请求/调用链的 TraceId 上下文载体，实现 `CorrelationIdProvider` 用 `AsyncLocal<string?>` 存储，跨 `async` 流转。

```csharp
string? Get();
```
返回当前上下文的 TraceId；未设置时返回 `null`。

```csharp
string Create();
```
生成新的 TraceId，格式为 32 位无横杠的 `Guid`（`ToString("N")`）。

```csharp
IDisposable Change(string correlationId);
```
将当前上下文切换为指定 TraceId，返回的 `IDisposable` 在 `Dispose` 时恢复为切换前的值（支持嵌套/恢复）。

`CorrelationIdAttribute`（命名空间 `Leistd.Tracing.Core.Attributes`）—— 标注在类或方法上，使该服务被 `CorrelationIdInterceptor` 拦截，自动开启 TraceId 作用域（适用于无 HTTP 入口的场景，如后台任务、消息消费）。

`CorrelationIdConstants.TraceIdLogKey`（命名空间 `Leistd.Tracing.Core.Constants`）—— 日志 Scope 中的 TraceId 键名，常量值 `leistd.correlationId.traceId`（对应 Java 项目的同名键）。

`CorrelationIdOptions`（命名空间 `Leistd.Tracing.Core.Options`，配置节 `Leistd:CorrelationId`）：

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `Enable` | `true` | 是否启用链路追踪（中间件与 HttpClient Handler 均据此短路跳过） |
| `HttpHeaderName` | `X-Correlation-Id` | 请求/响应头名；支持逗号分隔多个，如 `X-Correlation-Id,X-Request-Id` |
| `SetResponseHeader` | `true` | 是否将 TraceId 回写到响应头 |

`GetHttpHeaderNames()` 将 `HttpHeaderName` 按逗号拆分（去空白、去空项）为字符串数组。

## 能力实现

### `Leistd.Tracing.Core`

DI 扩展（命名空间 `Leistd.Tracing.Core`）：

- `AddCorrelationIdCore(this IServiceCollection, IConfiguration)`：绑定 `Leistd:CorrelationId` 配置节并注册核心服务。
- `AddCorrelationIdCore(this IServiceCollection, Action<CorrelationIdOptions>)`：代码方式配置 Options 并注册。

行为要点：

- `ICorrelationIdProvider` 注册为 **Singleton**（`TryAddSingleton`），上下文隔离依赖 `AsyncLocal`，因此天然按异步调用链隔离、线程安全。
- 通过 `OnServiceRegistered`（来自 `Leistd.DependencyInjection`）扫描带 `[CorrelationId]` 的类或方法，自动挂载 `CorrelationIdInterceptor`（基于 Castle DynamicProxy）。
- `CorrelationIdInterceptor` 优先级 `Order = -1000`（最外层，先于 UnitOfWork 等拦截器）。逻辑：若当前上下文已有非空 TraceId 则**不重复创建**，直接放行；否则 `Create()` 新 ID，`Change(...)` 切换上下文并开启日志 Scope，调用结束后自动恢复。

### `Leistd.Tracing.AspNetCore`

DI 扩展（命名空间 `Leistd.Tracing.AspNetCore`）：

- `AddCorrelationId(this IServiceCollection, IConfiguration)` / `AddCorrelationId(this IServiceCollection, Action<CorrelationIdOptions>)`：内部调用 `AddCorrelationIdCore`，注册核心服务。
- `UseCorrelationId(this IApplicationBuilder)`：注册 `CorrelationIdMiddleware`。

`CorrelationIdMiddleware` 行为：`Enable=false` 时直接放行。否则按 `GetHttpHeaderNames()` 顺序从请求头取 TraceId，取到首个非空白值即用；都没有则 `Create()` 生成。随后 `Change(...)` 设置 Provider 上下文 + 开启日志 Scope；`SetResponseHeader=true` 时在 `Response.OnStarting` 阶段把 TraceId 写回响应头（仅当响应头尚不含该名时追加）。

### `Leistd.Tracing.HttpClient`

DI 扩展（命名空间 `Leistd.Tracing.HttpClient`）：

- `AddCorrelationIdForwarding(this IHttpClientBuilder)`：为具名/类型化 `HttpClient` 挂载 `CorrelationIdDelegatingHandler`。

`CorrelationIdDelegatingHandler` 行为：`Enable=false` 时直接转发。否则取当前 `Get()` 的 TraceId，若非空则按 `GetHttpHeaderNames()` 写入出站请求头（仅当请求头尚不含该名时追加），实现下游透传。

## 最小可用示例

```csharp
using Leistd.Tracing.AspNetCore;
using Leistd.Tracing.HttpClient;

var builder = WebApplication.CreateBuilder(args);

// 入站：注册核心服务（绑定 Leistd:CorrelationId 配置节）
builder.Services.AddCorrelationId(builder.Configuration);

// 出站：让该 HttpClient 透传当前 TraceId
builder.Services.AddHttpClient("downstream")
    .AddCorrelationIdForwarding();

var app = builder.Build();

// 中间件尽量靠前，确保整条管线都在 TraceId 作用域内
app.UseCorrelationId();

app.MapGet("/ping", (ICorrelationIdProvider provider) => new { traceId = provider.Get() });

app.Run();
```

无 HTTP 入口（如后台任务）时，在服务类或方法上加 `[CorrelationId]`，由拦截器自动开启作用域：

```csharp
using Leistd.Tracing.Core.Attributes;

[CorrelationId]
public class OrderJob
{
    public Task RunAsync() => /* 此调用链内 ICorrelationIdProvider.Get() 已有 TraceId */;
}
```

## 依赖

- `Leistd.Core`、`Leistd.DependencyInjection`（`Leistd.Tracing.Core` 依赖；后者提供 `OnServiceRegistered`、`BaseAsyncInterceptor` 等 AOP 基础设施）。

## 备注

- 日志 Scope 键 `leistd.correlationId.traceId` 与 Java 项目保持一致，便于 Serilog 等日志库统一提取并跨语言对齐。
- TraceId 默认格式为 32 位无横杠 Guid，并非 W3C TraceContext 的 `traceparent`；本组件不解析/生成 W3C Trace Context。
- `Change` 返回的 `IDisposable` 必须配合 `using` 释放，否则上下文不会恢复。
- 中间件与 HttpClient Handler 写头时均“仅当不存在才追加”，已存在的同名头不会被覆盖。
