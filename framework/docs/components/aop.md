# AOP / 动态代理（`aop`）
> 基于 Castle DynamicProxy 的异步拦截器基类，统一同步/异步方法的拦截入口并支持执行排序。

## 包
| 包 | 角色 | 何时引用 |
| --- | --- | --- |
| `Leistd.DynamicProxy` | 异步拦截器基类 | 需要编写自定义 AOP 拦截器（如日志、缓存、事务）时引用 |

## 核心抽象

### `BaseAsyncInterceptor`
命名空间 `Leistd.DynamicProxy`，继承自 Castle 的 `AsyncInterceptorBase`，是编写拦截器的基类，可同时拦截同步与异步方法。

```csharp
public abstract class BaseAsyncInterceptor : AsyncInterceptorBase
```
拦截逻辑通过重写基类 `AsyncInterceptorBase` 的 `InterceptAsync(...)` 方法实现（来自 `Castle.Core.AsyncInterceptor` 包），本类不额外定义拦截方法。

```csharp
public virtual int Order => 0;
```
拦截器执行顺序，数值越小越先执行，默认 `0`；子类可重写以控制多个拦截器的相对次序。

## 能力实现

### `Leistd.DynamicProxy`
该包仅提供抽象基类 `BaseAsyncInterceptor`，不包含 DI 注册扩展方法，也不内置代理生成或拦截器编排逻辑。拦截器的实例化、`Order` 的实际排序应用与代理对象的创建（`ProxyGenerator`）需由使用方自行接入 Castle DynamicProxy 或上层框架完成。

## 最小可用示例

```csharp
using Castle.DynamicProxy;
using Leistd.DynamicProxy;

// 1. 定义拦截器，重写 AsyncInterceptorBase 的拦截方法
public class LoggingInterceptor : BaseAsyncInterceptor
{
    public override int Order => 10;

    protected override async Task InterceptAsync(
        IInvocation invocation,
        IInvocationProceedInfo proceedInfo,
        Func<IInvocation, IInvocationProceedInfo, Task> proceed)
    {
        // 调用前
        await proceed(invocation, proceedInfo);
        // 调用后
    }

    protected override async Task<TResult> InterceptAsync<TResult>(
        IInvocation invocation,
        IInvocationProceedInfo proceedInfo,
        Func<IInvocation, IInvocationProceedInfo, Task<TResult>> proceed)
    {
        var result = await proceed(invocation, proceedInfo);
        return result;
    }
}

// 2. 通过 Castle ProxyGenerator 生成带拦截器的代理
var generator = new ProxyGenerator();
var proxy = generator.CreateInterfaceProxyWithTargetInterface<IMyService>(
    target: new MyService(),
    interceptors: new LoggingInterceptor().ToInterceptor());

await proxy.DoWorkAsync();
```

## 依赖
无 Leistd 组件依赖；运行依赖第三方包 `Castle.Core.AsyncInterceptor`（Castle DynamicProxy）。

## 备注
- `InterceptAsync` 的两个重载签名来自基类 `AsyncInterceptorBase`（`Castle.Core.AsyncInterceptor` 包），不是本组件定义的方法。
- `Order` 仅为约定字段，本组件不实现按 `Order` 排序拦截器的逻辑；是否生效取决于使用方/上层框架如何编排。
- 目标框架为 `net10.0`，启用了 `Nullable` 与 `ImplicitUsings`。
