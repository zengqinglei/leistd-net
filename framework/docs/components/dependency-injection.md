# 服务注册回调与 DynamicProxy 织入

标准 .NET DI 只能在注册服务时逐一配置。Leistd 将这类能力拆成两层：

- `Leistd.DependencyInjection`：只提供类似 ABP `OnRegistered` 的服务注册回调机制，不依赖 Castle / AOP。
- `Leistd.DependencyInjection.DynamicProxy`：在回调机制之上接入 Castle DynamicProxy，把回调中声明的拦截器织入服务代理。

## 何时使用

| 场景 | 包 |
| --- | --- |
| 构建容器前扫描已注册服务、按类型约定做集中处理 | `Leistd.DependencyInjection` |
| 按接口、特性、命名规则为一批服务统一挂载 AOP 拦截器 | `Leistd.DependencyInjection.DynamicProxy` + `Leistd.DynamicProxy` |
| 仅给个别服务加拦截器，且已知具体类型 | 可直接使用 `Leistd.DynamicProxy` 手工生成代理 |

## 安装

仅使用注册回调：

```bash
dotnet add package Leistd.DependencyInjection
```

需要自动织入拦截器：

```bash
dotnet add package Leistd.DependencyInjection.DynamicProxy
```

## 配置 Provider

只需要执行回调、不需要 AOP：

```csharp
using Leistd.DependencyInjection;

builder.Host.UseServiceProviderFactory(new ServiceRegistrationCallbackFactory());
```

需要根据回调结果织入 DynamicProxy 拦截器：

```csharp
using Leistd.DependencyInjection.DynamicProxy;

builder.Host.UseServiceProviderFactory(new DynamicProxyServiceRegistrationCallbackFactory());
```

未设置对应工厂时，即便调用了 `OnServiceRegistered`，回调也不会执行。

## 使用 DynamicProxy 织入

```csharp
using Leistd.DependencyInjection;
using Leistd.DependencyInjection.DynamicProxy;
using Microsoft.Extensions.DependencyInjection;

builder.Host.UseServiceProviderFactory(new DynamicProxyServiceRegistrationCallbackFactory());

builder.Services.OnServiceRegistered(context =>
{
    if (typeof(IAuditable).IsAssignableFrom(context.ImplementationType))
    {
        context.AddInterceptor(typeof(AuditInterceptor));
    }
});

builder.Services.AddTransient<AuditInterceptor>();
builder.Services.AddScoped<IOrderService, OrderService>();
```

拦截器继承 `Leistd.DynamicProxy.BaseAsyncInterceptor`，可重写 `Order` 控制多个拦截器的执行顺序（数值越小越先执行）。

## 接口参考

`Leistd.DependencyInjection`：

| 成员 | 说明 |
| --- | --- |
| `IServiceCollection.OnServiceRegistered(action)` | 注册一个回调，构建 `IServiceProvider` 时对每个可推断实现类型的服务执行一次 |
| `IServiceCollection.GetRegistrationActionList()` | 返回当前回调列表，不存在时创建并以单例注册 |
| `IOnServiceRegisteredContext` | 单个被回调服务的上下文 |
| `IOnServiceRegisteredContext.ServiceType` | 注册的服务类型 |
| `IOnServiceRegisteredContext.ImplementationType` | 实现类型 |
| `IOnServiceRegisteredContext.Items` | 扩展数据字典，供上层集成组件挂载自定义信息 |
| `ServiceRegistrationCallbackFactory` | 只执行回调，不做 AOP 织入 |
| `ServiceRegistrationCallbackFactory.OnRegistrationProcessed(context, services)`（`protected virtual`）| 每个服务回调处理完成后的扩展点；`DynamicProxy` 子类正是重写它完成拦截器织入，也可自行继承重写做自定义后处理 |

`Leistd.DependencyInjection.DynamicProxy`：

| 成员 | 说明 |
| --- | --- |
| `DynamicProxyServiceRegistrationCallbackFactory` | 执行回调，并把 `AddInterceptor()` 收集到的拦截器织入服务代理 |
| `IOnServiceRegisteredContext.AddInterceptor(type)` | 为当前服务追加拦截器类型 |
| `IOnServiceRegisteredContext.GetInterceptorTypes()` | 获取当前服务收集到的拦截器类型 |

## 实现行为

- 遍历服务集合快照，避免遍历过程中修改集合。
- 仅处理能从 `ImplementationType` 或 `ImplementationInstance` 推断实现类型的服务；纯 `ImplementationFactory` 注册会被跳过。
- `Leistd.DependencyInjection` 只执行回调，不关心 AOP。
- `Leistd.DependencyInjection.DynamicProxy` 在回调后读取拦截器类型，替换原服务描述符并保留原 `Lifetime`。
- 拦截器实例从容器解析，按 `BaseAsyncInterceptor.Order` 升序排序。
- 服务类型为接口时使用 `CreateInterfaceProxyWithTarget`，否则使用 `CreateClassProxyWithTarget`。

## 注意事项

- 使用 `[UnitOfWork]`、`[CorrelationId]` 等基于 AOP 的能力时，宿主必须使用 `DynamicProxyServiceRegistrationCallbackFactory`。
- 拦截器类型必须能被容器解析，否则代理创建时会抛异常。
- `Leistd.DependencyInjection` 不依赖 Castle；Castle 依赖只存在于 `Leistd.DependencyInjection.DynamicProxy` 与 `Leistd.DynamicProxy`。

## 相关

- [组件总览](./README.md)
- [动态代理与拦截器（AOP）](./aop.md)
