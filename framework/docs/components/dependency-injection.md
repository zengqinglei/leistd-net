# 服务注册回调与拦截器织入

标准的 .NET 依赖注入只能在每个服务**注册时**逐一配置，无法在容器构建阶段对「所有已注册服务」做统一处理。当你想按约定批量挂载横切关注点——比如「凡是实现 `IAuditable` 的服务都自动织入审计拦截器」「带某特性的服务一律加事务拦截器」——就需要一个能在构建 `IServiceProvider` 前回调每个服务、并据此动态织入 AOP 代理的机制。

Leistd 的依赖注入组件提供了类似 Volo.ABP `OnRegistered` 的注册回调能力：你通过 `OnServiceRegistered` 注册一段回调，框架在构建容器时会对每个可实例化的服务执行一次回调，回调内可读取服务/实现类型并向其追加拦截器；随后框架自动把这些服务替换为织入了拦截器的代理。

## 何时使用

| 场景 | 是否适用 |
| --- | --- |
| 按约定（接口、特性、命名）为一批服务统一挂载 AOP 拦截器 | 适用 |
| 在容器构建阶段集中检查/装饰已注册服务 | 适用 |
| 仅给个别服务加拦截器，且已知具体类型 | 直接用 `Leistd.DynamicProxy` 手工生成代理更简单 |
| 纯工厂（`ImplementationFactory`）注册、无法推断实现类型的服务 | 不适用——会被跳过，无法织入 |

> 该机制只有在把 `ServiceRegistrationCallbackFactory` 设为宿主的服务提供者工厂后才会触发；详见[配置 Provider](#配置-provider)与[注意事项](#注意事项)。

## 安装

```bash
dotnet add package Leistd.DependencyInjection
```

本组件自带回调机制与拦截器织入，并通过项目引用依赖 AOP 组件 `Leistd.DynamicProxy`（提供 `IProxyGenerator` 与 `BaseAsyncInterceptor`）。本仓库模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

回调与织入由 `ServiceRegistrationCallbackFactory` 驱动，它实现 `IServiceProviderFactory<IServiceCollection>`，必须设为宿主的服务提供者工厂才会生效：

```csharp
var builder = Host.CreateApplicationBuilder(args);

// 启用回调 / 拦截织入工厂——这是整套机制的执行入口
builder.Host.UseServiceProviderFactory(new ServiceRegistrationCallbackFactory());
```

未设置该工厂时，即便调用了 `OnServiceRegistered`，回调也不会执行、拦截器也不会织入。

## 使用

通过 `IServiceCollection.OnServiceRegistered` 注册回调，在回调里按约定向 `context.Interceptors` 追加拦截器类型即可：

```csharp
using Leistd.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// 1. 启用回调 / 拦截织入工厂
builder.Host.UseServiceProviderFactory(new ServiceRegistrationCallbackFactory());

// 2. 注册回调：为实现 IAuditable 的服务织入审计拦截器
builder.Services.OnServiceRegistered(context =>
{
    if (typeof(IAuditable).IsAssignableFrom(context.ImplementationType))
    {
        // 向 Interceptors 追加类型即表示为该服务织入对应拦截器
        context.Interceptors.Add(typeof(AuditInterceptor));
    }
});

// 3. 拦截器与业务服务正常注册（拦截器需能被容器解析）
builder.Services.AddTransient<AuditInterceptor>();
builder.Services.AddScoped<IOrderService, OrderService>();

var host = builder.Build();

// 4. 解析得到的是已织入拦截器的代理
var orderService = host.Services.GetRequiredService<IOrderService>();
```

拦截器继承 `Leistd.DynamicProxy` 的 `BaseAsyncInterceptor`，重写 `Order` 可控制多个拦截器的执行顺序（数值越小越先执行）。

## 接口参考

`Leistd.DependencyInjection` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `IServiceCollection.OnServiceRegistered(action)` | 扩展方法。注册一个回调，构建 `IServiceProvider` 时对每个服务执行一次；返回 `services` 支持链式调用 |
| `IServiceCollection.GetRegistrationActionList()` | 扩展方法。返回当前回调列表，不存在时创建并以单例注册，始终非空 |
| `IOnServiceRegistredContext` | 单个被回调服务的上下文 |
| `IOnServiceRegistredContext.ServiceType` | 注册的服务类型 |
| `IOnServiceRegistredContext.ImplementationType` | 实现类型 |
| `IOnServiceRegistredContext.Interceptors` | 拦截器类型列表（`List<Type>`）；向其 `Add` 即为该服务织入对应拦截器 |
| `OnServiceRegistredContext` | 上下文的 `record` 实现，`Interceptors` 初始为空列表 |
| `ServiceRegistrationActionList` | 回调集合，本质为 `List<Action<IOnServiceRegistredContext>>`，以单例存放在 `IServiceCollection` 中 |
| `ServiceRegistrationCallbackFactory` | `IServiceProviderFactory<IServiceCollection>` 实现，回调与织入的执行入口 |
| `ServiceRegistrationCallbackFactory.CreateBuilder(services)` | 原样返回 `services` |
| `ServiceRegistrationCallbackFactory.CreateServiceProvider(services)` | 执行所有回调（并按需织入拦截器）后 `BuildServiceProvider` |

## 实现行为

### Leistd.DependencyInjection

构建 `IServiceProvider` 时（`CreateServiceProvider`），框架对服务集合的行为均有源码依据：

- 遍历服务集合的**快照副本**，避免遍历过程中修改集合。
- 仅处理可实例化的实现类型：实现类型只能从 `ImplementationType` 或 `ImplementationInstance` 推断；为 `null`、抽象类或接口时**跳过**——因此纯 `ImplementationFactory` 注册（无法得知实现类型）会被跳过。
- 对每个服务依次执行所有回调；若回调向 `Interceptors` 追加了类型，则将该服务描述符**替换为装饰器版本**，并保留原 `Lifetime`。
- 拦截器实例从容器解析（`GetRequiredService`），按 `Order` 升序排序——仅 `BaseAsyncInterceptor` 提供 `Order`，其它拦截器按 `0` 处理。
- 服务类型为接口时用 `CreateInterfaceProxyWithTarget`，否则用 `CreateClassProxyWithTarget`。
- 织入依赖容器中已注册的 `IProxyGenerator`（来自 `Leistd.DynamicProxy`）；缺失时代理创建会抛 `InvalidOperationException`。
- 原始实例按 `ImplementationInstance` → `ImplementationFactory` → `ActivatorUtilities.CreateInstance(ImplementationType)` 的顺序创建，均不满足时抛 `InvalidOperationException`。

## 配置项 / Options

当前无配置项（无 Options 类）。注册行为通过回调代码本身表达。

## 注意事项

- 回调与织入**仅在使用 `ServiceRegistrationCallbackFactory` 作为服务提供者工厂时触发**；只调用 `OnServiceRegistered` 而未设置该工厂不会生效。
- 纯 `ImplementationFactory` 注册（无 `ImplementationType` / `ImplementationInstance`）无法推断实现类型，会在遍历中被跳过，不会被织入。
- 织入的代理为 `WithTarget` 形式，作用于已创建的目标实例，不改变服务原本的生命周期。
- 拦截器类型必须能被容器解析（需自行 `Add*` 注册），否则 `GetRequiredService` 会抛异常。
- 源码注释明确该机制「类似 ABP 的 `OnRegistered`」，接口名 `IOnServiceRegistredContext` 沿用了该拼写（注意 `Registred`）。

## 相关

- [组件总览](./README.md)
- [动态代理与拦截器（AOP）](./aop.md)
