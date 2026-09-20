# 数据访问共享契约：连接与分页

`Leistd.Data` 定义与持久化实现无关的数据访问契约：连接解析与归属让工作单元与配置、Secret 服务或多租户实现解耦；分页请求与结果让存储、用例与端点共用一套参数与返回形状。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 宿主要按运行时信息决定 DbContext 连哪个库 | 实现 `IConnectionStringResolver` 并注册；不注册时 DbContext 保持宿主配置的单连接行为 |
| 某个 DbContext 要钉在独立的命名连接上（如控制面库） | 在 DbContext 上标 `[ConnectionStringName("...")]`；未标记的走 `ConnectionStringNames.Default` |
| 共享库形态下要防「一个工作单元写了两个归属的数据」 | 实现 `IConnectionAffinityProvider` 返回归属标识；多租户侧已提供租户实现 |
| 列表查询要分页 | 查询接收 `PageRequest`、返回 `PagedResult<T>`；MVC 用 `[FromQuery]` 绑定，Minimal API 用带默认值的显式参数 |

## 安装

```bash
dotnet add package Leistd.Data
```

通常无需单独添加：工作单元、多租户与带列表查询的组件已传递引用它。

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

分页查询直接接收请求类型，排序字段按白名单解析：

```csharp
public async Task<PagedResult<Order>> GetPagedListAsync(PageRequest page, CancellationToken ct)
{
    var query = dbContext.Orders.AsNoTracking();
    var totalCount = await query.LongCountAsync(ct);

    query = page.Sorting switch
    {
        "createdAt" => query.OrderBy(x => x.CreatedAt),
        null or "" or "-createdAt" => query.OrderByDescending(x => x.CreatedAt),
        _ => throw new ArgumentException($"Unsupported sorting '{page.Sorting}'.", nameof(page))
    };

    var items = await query.Skip(page.Offset).Take(page.Limit).ToListAsync(ct);
    return new PagedResult<Order>(totalCount, items);
}
```

## 接口参考

| 成员 | 作用 |
| --- | --- |
| `IConnectionStringResolver.ResolveAsync(name, ct)` | 给定连接名返回非空连接字符串；解析不出抛异常 |
| `IConnectionAffinityProvider.AffinityKey` | 当前环境的逻辑归属标识，未注册实现时为 `null` |
| `ConnectionStringNameAttribute` | 标在 DbContext 上指定它使用的连接名 |
| `ConnectionStringNames.Default` | 未标记特性时使用的默认连接名 |
| `PageRequest` | 分页请求：`Offset`（≥0）、`Limit`（1–`MaximumLimit`，默认 `DefaultLimit`）、`Sorting`；边界以 DataAnnotations 表达，可被继承以追加筛选条件 |
| `PagedResult<T>` | 分页结果：`TotalCount` 与当前页 `Items`；`Empty` 为空结果 |

## 实现行为

解析器必须返回非空连接字符串。配置缺失、外部依赖不可达或凭据无法解析时必须抛出异常，不得回退到调用方未选择的数据库。租户感知解析见[多租户](./multi-tenancy.md)。

分页上限 `PageRequest.MaximumLimit` 是影响面封顶；越界只产生验证错误，由宿主的模型校验或 Minimal API 校验拒绝请求，类型本身不截断。

`AffinityKey` 表示逻辑归属，不等同于物理连接。共享库中不同租户可指向同一连接，工作单元仍必须拒绝跨归属写入。该值在每次获取 DbContext 时读取，实现不得执行 I/O；未注册时为 `null`。

## 注意事项

- 租户感知的 `IConnectionStringResolver` 由[多租户](./multi-tenancy.md)提供（本地直连控制库或远端回源）。**解析用的名字就是本 DbContext 的 `[ConnectionStringName]`**：租户按 `(租户, 连接名)` 逐行登记连接，一条都没登记即用本服务自己配置的库。连接串加密存放在控制库里，宿主须配置持久化的 Data Protection 密钥环。
- 解析器按请求解析，注册为 `Scoped`；`AffinityKey` 的实现必须避免 I/O，否则每次取 DbContext 都会付代价。
- **`Sorting` 不得原样拼进查询。** 它来自调用方，各查询只接受自己白名单里的排序字段，其余一律拒绝。
- **Minimal API 不要用 `[AsParameters]` 绑定 `PageRequest`。** 它把没有默认值的非空属性当成必填参数，属性初始化器不算默认值，省略 `offset` 的请求直接 400。声明 `int offset = 0, int limit = PageRequest.DefaultLimit, string? sorting = null` 这样的显式参数（可直接在参数上标 `[Range]`，由宿主的 `AddValidation()` 校验），再构造 `PageRequest`。
- **先计数再分页。** `TotalCount` 必须基于与当前页相同的筛选条件计算，分页参数只作用于取条目这一步。
