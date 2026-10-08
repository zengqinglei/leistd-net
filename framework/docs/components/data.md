# 数据访问：连接、分页、过滤与查询

`Leistd.Data` 定义与持久化实现无关的数据访问契约：连接解析与归属让工作单元与配置、Secret 服务或多租户实现解耦；分页请求与结果让存储、用例与端点共用一套参数与返回形状；过滤状态与异步查询执行可脱离 DDD 使用。

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

工作单元、多租户与带列表查询的组件传递引用 `Leistd.Data`。它依赖 Core（复用作用域释放）和 DI 抽象，不依赖 EF 或 DDD；引用它的契约包同样传递这些依赖。EF 查询执行器由可选的 `Leistd.Data.EntityFrameworkCore` 分发。

## 注册

```csharp
using Leistd.Data;
using Leistd.Data.EntityFrameworkCore;

builder.Services.AddDataFilters();
builder.Services.AddDataEfCore();
```

`AddDataFilters()` 登记 `IDataFilter` 及泛型端口，`AddDataEfCore()` 登记 `IQueryableAsyncExecuter`。两者独立、幂等，默认实现为单例，保留宿主预先登记的实现；DDD 基础设施组合这两个入口。

过滤端口位于 `Leistd.Data.Filters`：默认启用，`Disable<TFilter>()` / `Enable<TFilter>()` 的嵌套作用域按进入逆序释放，恢复原状态。状态在当前异步流中生效，并行分支不相互修改。该组件只管理状态，查询 provider 负责实际过滤；标记接口与租户、软删除模型仍由各自组件或 DDD 提供。

`Leistd.Data.Querying.IQueryableAsyncExecuter` 接受可组合查询，经持久化 provider 异步执行列表、计数、唯一项与存在性判断，并传递取消令牌。EF 实现需要原生 EF 查询 provider，不将内存 IQueryable 伪装为异步查询，也不改变跟踪、连接或过滤规则。

## 使用

实现契约并注册；连接名由 DbContext 上的特性决定。

```csharp
using Microsoft.EntityFrameworkCore;

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

应用层用查询解析库（如 Dynamic LINQ）处理 `Sorting` 并追加唯一键；空白输入使用查询默认排序。下例接收已完成排序的查询，只执行计数和分页：

```csharp
using Microsoft.EntityFrameworkCore;

public async Task<PagedResult<Order>> GetPagedListAsync(
    IOrderedQueryable<Order> query, PageRequest page, CancellationToken ct)
{
    var totalCount = await query.LongCountAsync(ct);
    var items = await query.Skip(page.Offset).Take(page.Limit).ToListAsync(ct);
    return new PagedResult<Order>(totalCount, items);
}
```

业务请求可覆写默认值与校验边界：

```csharp
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

public sealed record OrderPageRequest : PageRequest
{
    [Range(1, 2000)]
    public override int Limit { get; init; } = 20;

    [AllowNull]
    public override string Sorting
    {
        get => string.IsNullOrWhiteSpace(field) ? $"{nameof(Order.CreatedAt)} desc" : field;
        init;
    }
}
```

## 接口参考

| 成员 | 作用 |
| --- | --- |
| `IConnectionStringResolver.ResolveAsync(name, ct)` | 给定连接名返回非空连接字符串；解析不出抛异常 |
| `IConnectionAffinityProvider.AffinityKey` | 当前环境的逻辑归属标识，未注册实现时为 `null` |
| `ConnectionStringNameAttribute` | 标在 DbContext 上指定它使用的连接名 |
| `ConnectionStringNames.Default` | 未标记特性时使用的默认连接名 |
| `PageRequest` | 分页请求：`Offset`（≥0）、`Limit`（1–`MaximumLimit`，默认 `DefaultLimit`）、`Sorting`；边界以 DataAnnotations 表达，virtual 属性可由派生 record 覆写默认值与校验特性 |
| `PagedResult<T>` | 分页结果：`TotalCount` 与当前页 `Items`；`Empty` 为空结果 |

## 实现行为

解析器必须返回非空连接字符串。配置缺失、外部依赖不可达或凭据无法解析时必须抛出异常，不得回退到调用方未选择的数据库。租户感知解析见[多租户](./multi-tenancy.md)。

`PageRequest` 的基础上限为 `MaximumLimit`。业务请求可覆写属性和校验特性；越界由宿主校验拒绝，类型本身不截断。用派生类型声明实例规则，不修改全局默认值。

`AffinityKey` 表示逻辑归属，不等同于物理连接。共享库中不同租户可指向同一连接，工作单元仍必须拒绝跨归属写入。该值在每次获取 DbContext 时读取，实现不得执行 I/O；未注册时为 `null`。

## 注意事项

- 租户感知的 `IConnectionStringResolver` 由[多租户](./multi-tenancy.md)提供，解析用的名字就是 DbContext 的 `[ConnectionStringName]`。
- 解析器按请求解析，注册为 `Scoped`；`AffinityKey` 的实现必须避免 I/O，否则每次取 DbContext 都会付代价。
- `Sorting` 通过表达式解析库处理，不拼接 SQL；必要的业务限制由应用层验证器执行。
- Minimal API 不要用 `[AsParameters]` 绑定 `PageRequest`：它把没有默认值的非空属性当成必填，省略 `offset` 的请求会 400。
  声明 `int offset = 0, int limit = PageRequest.DefaultLimit, string? sorting = null` 这样的显式参数（可标 `[Range]` 由 `AddValidation()` 校验），再构造 `PageRequest`。
- `TotalCount` 与当前页基于相同的筛选条件计算，分页参数只作用于取条目。
