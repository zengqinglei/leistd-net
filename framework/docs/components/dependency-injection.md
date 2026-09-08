# 服务注册回调与 DynamicProxy 织入

`Leistd.DependencyInjection` 在容器构建前对服务注册执行约定回调；`Leistd.DependencyInjection.DynamicProxy` 再将回调声明的 Castle 拦截器织入服务代理。

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

## 注册

只需要执行回调、不需要 AOP：

```csharp
using Leistd.DependencyInjection.Extensions;

builder.Host.UseServiceProviderFactory(new ServiceRegistrationCallbackFactory());
```

需要根据回调结果织入 DynamicProxy 拦截器：

```csharp
using Leistd.DependencyInjection.DynamicProxy.Extensions;

builder.Host.UseServiceProviderFactory(new DynamicProxyServiceRegistrationCallbackFactory());
```

需要与 ASP.NET Core 开发环境默认校验对齐时，显式传入 `ServiceProviderOptions`：

```csharp
builder.Host.UseServiceProviderFactory(new DynamicProxyServiceRegistrationCallbackFactory(
    new ServiceProviderOptions
    {
        ValidateScopes = builder.Environment.IsDevelopment(),
        ValidateOnBuild = builder.Environment.IsDevelopment()
    }));
```

自定义工厂不会继承宿主的 `ServiceProviderOptions`；需要作用域和构建校验时应显式传入。未设置对应工厂时，注册回调不会执行。

## 使用

### 校验注册形式

工厂委托注册也会执行回调，此时 `ImplementationType` 为 `null`。仅依赖 `ServiceType` 的约定仍可工作；依赖实现类型的回调应跳过该描述符。

需要对开放泛型等无法织入的注册形式失败关闭时，使用 `AddRegistrationValidator`。校验器在回调改写描述符之前获取完整 `IServiceCollection`。

```csharp
using Leistd.DependencyInjection.Extensions;

builder.Services.AddRegistrationValidator(services =>
{
    foreach (var descriptor in services)
    {
        if (descriptor.ServiceType == typeof(IMyConvention) &&
            descriptor.ImplementationType is null &&
            descriptor.ImplementationInstance is null)
        {
            throw new InvalidOperationException(
                $"'{descriptor.ServiceType.FullName}' must be registered by implementation type.");
        }
    }
});
```

校验失败应直接抛出异常，阻止宿主启动。

### 用 DynamicProxy 织入拦截器

```csharp
using Leistd.DependencyInjection.Extensions;
using Leistd.DependencyInjection.DynamicProxy.Extensions;
using Microsoft.Extensions.DependencyInjection;

builder.Host.UseServiceProviderFactory(new DynamicProxyServiceRegistrationCallbackFactory());

builder.Services.OnServiceRegistered(context =>
{
    if (context.ImplementationType is { } implementationType &&
        typeof(IAuditable).IsAssignableFrom(implementationType))
    {
        context.AddInterceptor(typeof(AuditInterceptor));
    }
});

builder.Services.AddTransient<AuditInterceptor>();
builder.Services.AddScoped<IOrderService, OrderService>();
```

拦截器继承 `Leistd.DynamicProxy.Interceptors.BaseAsyncInterceptor`，可重写 `Order` 控制多个拦截器的执行顺序（数值越小越先执行）。

## 接口参考

| 成员 | 说明 |
| --- | --- |
| `IServiceCollection.OnServiceRegistered(action)` | 注册一个回调，构建 `IServiceProvider` 时对每个服务执行一次（含工厂委托注册，此时实现类型为 `null`） |
| `IServiceCollection.AddRegistrationValidator(validator)` | 注册一个校验器，构建 `IServiceProvider` 时在所有回调之前执行；入参为完整服务集合，用于发现回调只能跳过、无法让宿主失败的形态 |
| `IOnServiceRegisteredContext` | 公开 `ServiceType`、可空 `ImplementationType` 和扩展数据 |
| `ServiceRegistrationCallbackFactory` | 只执行回调，不做 AOP 织入 |
| `ServiceRegistrationCallbackFactory.OnRegistrationProcessed(...)` | 每个服务回调处理后的扩展点 |
| `DynamicProxyServiceRegistrationCallbackFactory` | 执行回调，并把 `AddInterceptor()` 收集到的拦截器织入服务代理 |
| `DynamicProxyRegistrationExtensions.AddInterceptor(context, type)` | 为当前服务追加拦截器类型；通常写作 `context.AddInterceptor(type)` |
| `DynamicProxyRegistrationExtensions.GetInterceptorTypes(context)` | 获取当前服务收集到的拦截器类型；通常写作 `context.GetInterceptorTypes()` |
| `IServiceCollection.EnsureSingleAuthoritative<TService, TImplementation>(expectedLifetime, reason)` | 注册期断言：`TService` 上不得已有别的实现、也不得是同一实现的不同生命周期，违反则抛 `InvalidOperationException`（`reason` 是给宿主看的一句话）。只用于「多个实现说不通」的**存储**类服务；业务编排型服务用 `TryAdd*` 表达「默认实现可被宿主替换」即可。边界见下节 |

### `EnsureSingleAuthoritative` 的支持边界

刻意收窄，不覆盖全部 `ServiceDescriptor` 形态：

| 已有的注册形态 | 判定 |
| --- | --- |
| 同一封闭类型、同一生命周期 | 通过——重复登记是幂等的 |
| 同一封闭类型、不同生命周期 | **冲突**——放行会让紧随其后的 `TryAdd*` 保留宿主那条错的 |
| 别的实现类型 | 冲突 |
| 实例注册（`AddSingleton<TService>(instance)`） | 按实例真实类型判定；其生命周期恒为单例，故只有期望生命周期也是单例时才通过 |
| 工厂注册（`ImplementationFactory`） | **冲突**——问不出实现身份，而「不确定」不能当成「没问题」 |
| keyed 注册 | 忽略——按键解析，不参与单服务解析 |
| 开放泛型（`typeof(IFoo<>)`） | 不在范围内——`TService` 是封闭类型，匹配不到那种描述符 |

它检查全部描述符，避免后注册的冲突实现被遗漏。

## 实现行为

- 校验器先于回调执行；回调遍历服务集合快照。
- 工厂委托注册也执行回调，但 `ImplementationType` 为 `null`。
- DynamicProxy 织入保留原 `Lifetime`，拦截器按 `BaseAsyncInterceptor.Order` 升序执行。
- 接口服务使用接口代理，其他服务使用类代理。

## 注意事项

- 使用 `[UnitOfWork]`、`[CorrelationId]` 等基于 AOP 的能力时，宿主必须使用 `DynamicProxyServiceRegistrationCallbackFactory`。
- 拦截器类型必须能被容器解析，否则代理创建时会抛异常。
- **`ValidateOnBuild` 覆盖被织入的服务**：描述符改写成工厂型后 Microsoft DI 看不到它的
  构造函数图，因此工厂在改写前用未改写的副本额外校验一次，把这块覆盖面补回来。
- **注册回调必须是纯函数**：只允许往 `context` 记拦截器，<b>不得往 `IServiceCollection`
  追加服务</b>。上面那次预校验看到的依赖图必须是完整的；回调里追加服务会让预校验把尚未
  注册的依赖误报成缺失。需要按类型批量注册时用显式的扩展方法
  （如 `AddDddDbContext<TDbContext>()`），不要写进回调。
- **键控注册可以参与织入**：`IOnServiceRegisteredContext.ServiceKey` 给出服务键，织入后
  仍是键控描述符，`GetKeyedService` 照常可取。仅按 `ServiceType` 判定的约定会同时命中
  同类型的键控与非键控注册。
- `Leistd.DependencyInjection` 不依赖 Castle；Castle 依赖只存在于 `Leistd.DependencyInjection.DynamicProxy` 与 `Leistd.DynamicProxy`。

## 相关

- [动态代理与拦截器（AOP）](./aop.md)
