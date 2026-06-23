# 动态代理拦截器基类

横切关注点——事务边界、链路追踪、缓存、日志、权限校验——往往散落在每个业务方法的开头与结尾，重复且容易遗漏。AOP（面向切面编程）通过动态代理把这些逻辑收敛到**拦截器**里，由框架在方法调用前后自动织入，业务代码保持纯净。

Leistd 基于 [Castle DynamicProxy](https://github.com/castleproject/Core) 与 `Castle.Core.AsyncInterceptor` 构建拦截能力。`Leistd.DynamicProxy` 提供统一的拦截器基类 `BaseAsyncInterceptor`：它同时支持同步与异步方法的拦截，并约定了一个 `Order` 排序属性，让多个拦截器在同一服务上按确定顺序织入。框架内置的链路追踪、工作单元等组件都以它为基类编写拦截器。

## 何时使用

| 场景 | 做法 |
| --- | --- |
| 需要为服务方法织入事务、追踪、日志等横切逻辑 | 继承 `BaseAsyncInterceptor` 编写拦截器 |
| 一个服务上叠加多个拦截器，需控制执行先后 | 重写 `Order`（值越小越外层、越先执行） |
| 只想使用框架已内置的拦截能力（追踪 / 工作单元） | 无需直接引用本包，改用对应组件 |

> 本包只提供拦截器**基类**。把拦截器织入到具体服务上的注册与代理生成由 [依赖注入](./dependency-injection.md) 组件完成，本包不含 DI 扩展方法。

## 安装

```bash
# 编写自定义拦截器时引用（框架内置拦截器组件已传递引用，通常无需单独添加）
dotnet add package Leistd.DynamicProxy
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 使用

继承 `BaseAsyncInterceptor`，重写两个 `InterceptAsync` 重载（分别对应无返回值与有返回值的方法），在调用 `proceed` 前后插入横切逻辑。构造函数可正常注入依赖：

```csharp
using System.Diagnostics;
using Castle.DynamicProxy;
using Leistd.DynamicProxy;
using Microsoft.Extensions.Logging;

public class TimingInterceptor(ILogger<TimingInterceptor> logger) : BaseAsyncInterceptor
{
    // 越小越先执行（越靠外层）。默认 0；此处设为较晚执行
    public override int Order => 100;

    // 无返回值的方法
    protected override async Task InterceptAsync(
        IInvocation invocation,
        IInvocationProceedInfo proceedInfo,
        Func<IInvocation, IInvocationProceedInfo, Task> proceed)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await proceed(invocation, proceedInfo); // 调用被代理的原方法
        }
        finally
        {
            logger.LogInformation("{Method} 耗时 {Ms}ms",
                invocation.Method.Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    // 有返回值的方法
    protected override async Task<TResult> InterceptAsync<TResult>(
        IInvocation invocation,
        IInvocationProceedInfo proceedInfo,
        Func<IInvocation, IInvocationProceedInfo, Task<TResult>> proceed)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            return await proceed(invocation, proceedInfo);
        }
        finally
        {
            logger.LogInformation("{Method} 耗时 {Ms}ms",
                invocation.Method.Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
}
```

拦截器写好后，由 [依赖注入](./dependency-injection.md) 组件负责把它注册并织入目标服务（内部用 `IProxyGenerator` 生成接口/类代理，并按各拦截器的 `Order` 升序织入）。

## 接口参考

`Leistd.DynamicProxy` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `BaseAsyncInterceptor` | 异步拦截器抽象基类，继承自 `AsyncInterceptorBase`（`Castle.Core.AsyncInterceptor`），同时支持同步与异步方法的拦截 |
| `BaseAsyncInterceptor.Order` | `virtual int`，拦截器执行顺序；**数值越小越先执行（越靠外层）**，默认 `0` |
| `InterceptAsync(invocation, proceedInfo, proceed)` | 来自基类，需重写；拦截**无返回值**方法，调用 `proceed(...)` 执行原方法 |
| `InterceptAsync<TResult>(invocation, proceedInfo, proceed)` | 来自基类，需重写；拦截**有返回值**方法，返回原方法结果 |

## 配置项 / Options

本组件当前无配置项（无 Options 类、无 DI 扩展方法）。`Order` 通过在子类中重写属性来控制，不通过配置文件。

## 注意事项

- `Order` 语义是**数值越小越先执行（越靠外层）**，可为负数。内置 `CorrelationIdInterceptor`（链路追踪）取 `Order = -1000` 以确保位于最外层、最先初始化上下文。
- 必须重写两个 `InterceptAsync` 重载：有返回值的方法走泛型重载，无返回值的方法走非泛型重载，二者逻辑通常一致，需分别实现。
- 不要忘记在两个重载里都调用 `proceed(invocation, proceedInfo)`；不调用则原方法不会执行。
- 本包仅是拦截器基类，自身不会让任何服务“自动被拦截”。织入需配合 [依赖注入](./dependency-injection.md) 组件完成；类代理要求被拦截方法为 `virtual`，接口代理则无此限制。

## 相关

- [组件总览](./README.md)
- [依赖注入](./dependency-injection.md)
- [链路追踪](./tracing.md)
- [工作单元](./unit-of-work.md)
