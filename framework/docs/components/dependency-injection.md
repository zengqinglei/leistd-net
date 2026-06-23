# 依赖注入（`dependency-injection`）
> 在 `IServiceCollection` 构建 `IServiceProvider` 时统一回调每个已注册服务，并据此动态织入拦截器（类似 ABP 的 `OnRegistered`）。

## 包

| 包 | 角色 | 何时引用 |
| --- | --- | --- |
| `Leistd.DependencyInjection` | 服务注册回调机制 + 拦截器自动织入 | 需要在服务注册阶段统一处理所有服务（如按约定挂载 AOP 拦截器）时引用 |

## 核心抽象

### `IOnServiceRegistredContext`
单个被回调服务的上下文，回调内可读取类型并向其追加拦截器。

```csharp
Type ServiceType { get; }          // 注册的服务类型
Type ImplementationType { get; }   // 实现类型
List<Type> Interceptors { get; }   // 拦截器类型列表；向其 Add 即为该服务织入对应拦截器
```

由 `OnServiceRegistredContext`（record）实现，`Interceptors` 初始为空列表。

### `ServiceRegistrationActionList`
回调集合，本质是 `List<Action<IOnServiceRegistredContext>>`。以单例形式存放在 `IServiceCollection` 中，承载所有通过 `OnServiceRegistered` 注册的回调。

### `ServiceCollectionRegistrationExtensions`
`IServiceCollection` 扩展方法。

```csharp
IServiceCollection OnServiceRegistered(this IServiceCollection services, Action<IOnServiceRegistredContext> registrationAction)
```
注册一个服务注册回调，回调将在构建 `IServiceProvider` 时对每个服务执行一次；返回 `services` 以支持链式调用。

```csharp
ServiceRegistrationActionList GetRegistrationActionList(this IServiceCollection services)
```
返回当前的回调列表（不存在时创建并以单例注册），始终非空。

### `ServiceRegistrationCallbackFactory`
实现 `IServiceProviderFactory<IServiceCollection>`，是回调与拦截器织入的执行入口。需在宿主上注册为服务提供者工厂后生效。

```csharp
IServiceCollection CreateBuilder(IServiceCollection services)        // 原样返回 services
IServiceProvider  CreateServiceProvider(IServiceCollection services) // 执行所有回调后 BuildServiceProvider
```

## 能力实现

### `Leistd.DependencyInjection`

注册入口为 `IServiceCollection.OnServiceRegistered(...)`；执行入口为将 `ServiceRegistrationCallbackFactory` 设为宿主的服务提供者工厂（如 `UseServiceProviderFactory(new ServiceRegistrationCallbackFactory())`）。

行为要点（均有源码依据）：
- 构建 `IServiceProvider` 时，对服务集合的**快照副本**遍历，避免遍历中修改集合。
- 仅处理可实例化的实现类型：实现类型为 `null`、抽象类或接口时**跳过**（仅能从 `ImplementationType` 或 `ImplementationInstance` 推断类型，纯工厂注册因无法得知实现类型而被跳过）。
- 对每个服务依次执行所有回调；若回调向 `Interceptors` 添加了类型，则将该服务描述符**替换为装饰器版本**，保留原 `Lifetime`。
- 拦截器实例从容器解析（`GetRequiredService`），并按 `Order` 升序排序——仅 `BaseAsyncInterceptor` 提供 `Order`，其它拦截器按 `0` 处理。
- 服务类型为接口时用 `CreateInterfaceProxyWithTarget` 生成代理，否则用 `CreateClassProxyWithTarget`。
- 织入依赖容器中已注册的 `IProxyGenerator`（来自 AOP 组件）；缺失时代理创建会抛 `InvalidOperationException`。
- 原始实例按 `ImplementationInstance` → `ImplementationFactory` → `ActivatorUtilities.CreateInstance(ImplementationType)` 顺序创建，均不满足时抛 `InvalidOperationException`。

## 最小可用示例

```csharp
using Leistd.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// 1. 启用回调/拦截织入工厂
builder.Host.UseServiceProviderFactory(new ServiceRegistrationCallbackFactory());

// 2. 注册一个回调：为实现 IAuditable 的服务织入审计拦截器
builder.Services.OnServiceRegistered(context =>
{
    if (typeof(IAuditable).IsAssignableFrom(context.ImplementationType))
    {
        context.Interceptors.Add(typeof(AuditInterceptor));
    }
});

// 3. 拦截器与业务服务正常注册（拦截器需可被容器解析）
builder.Services.AddTransient<AuditInterceptor>();
builder.Services.AddScoped<IOrderService, OrderService>();

var host = builder.Build();

// 4. 解析到的是已织入拦截器的代理
var orderService = host.Services.GetRequiredService<IOrderService>();
```

## 依赖

- `Leistd.DynamicProxy`（AOP 组件，提供 `IProxyGenerator` 与 `BaseAsyncInterceptor`）

## 备注

- 源码注释明确该机制「类似 ABP 的 `OnRegistered`」。
- 回调与拦截织入仅在使用 `ServiceRegistrationCallbackFactory` 作为服务提供者工厂时触发；仅调用 `OnServiceRegistered` 而未设置该工厂不会生效。
- 纯 `ImplementationFactory` 注册（无 `ImplementationType`/`ImplementationInstance`）无法推断实现类型，会被遍历跳过，不会被织入。
- 织入的代理为 `WithTarget` 形式，作用于已创建的目标实例，不改变服务原本的生命周期。
