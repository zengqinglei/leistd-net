# 连接解析契约：连接名与连接归属

`Leistd.Data` 定义连接解析和数据归属契约，使工作单元与具体配置、Secret 服务或多租户实现解耦。该包不提供运行时实现。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 宿主要按运行时信息决定 DbContext 连哪个库 | 实现 `IConnectionStringResolver` 并注册；不注册时 DbContext 保持宿主配置的单连接行为 |
| 某个 DbContext 要钉在独立的命名连接上（如控制面库） | 在 DbContext 上标 `[ConnectionStringName("...")]`；未标记的走 `ConnectionStringNames.Default` |
| 共享库形态下要防「一个工作单元写了两个归属的数据」 | 实现 `IConnectionAffinityProvider` 返回归属标识；多租户侧已提供租户实现 |

## 安装

```bash
dotnet add package Leistd.Data
```

通常无需单独添加：`Leistd.UnitOfWork.EntityFrameworkCore` 与 `Leistd.MultiTenancy.Core` 已传递引用它。

## 使用

实现契约并注册；连接名由 DbContext 上的特性决定。

```csharp
[ConnectionStringName("Control")]
public class ControlDbContext(DbContextOptions<ControlDbContext> options) : DbContext(options);

public sealed class ConfigurationConnectionStringResolver(IConfiguration configuration) : IConnectionStringResolver
{
    public Task<string> ResolveAsync(string connectionStringName, CancellationToken cancellationToken = default)
    {
        var value = configuration.GetConnectionString(connectionStringName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Connection string '{connectionStringName}' is not configured.")
            : Task.FromResult(value);
    }
}

builder.Services.AddScoped<IConnectionStringResolver, ConfigurationConnectionStringResolver>();
```

## 接口参考

| 成员 | 作用 |
| --- | --- |
| `IConnectionStringResolver.ResolveAsync(name, ct)` | 给定连接名返回非空连接字符串；解析不出抛异常 |
| `IConnectionAffinityProvider.AffinityKey` | 当前环境的逻辑归属标识，未注册实现时为 `null` |
| `ConnectionStringNameAttribute` | 标在 DbContext 上指定它使用的连接名 |
| `ConnectionStringNames.Default` | 未标记特性时使用的默认连接名 |

## 实现行为

解析器必须返回非空连接字符串。配置缺失、外部依赖不可达或凭据无法解析时必须抛出异常，不得回退到调用方未选择的数据库。租户感知解析见[多租户](./multi-tenancy.md)。

`AffinityKey` 表示逻辑归属，不等同于物理连接。共享库中不同租户可指向同一连接，工作单元仍必须拒绝跨归属写入。该值在每次获取 DbContext 时读取，实现不得执行 I/O；未注册时为 `null`。

## 注意事项

- Framework 不提供 `IConnectionStringResolver` 的租户实现；Secret 来源、缓存与并发合并策略由宿主决定。
- 解析器按请求解析，注册为 `Scoped`；`AffinityKey` 的实现必须避免 I/O，否则每次取 DbContext 都会付代价。
