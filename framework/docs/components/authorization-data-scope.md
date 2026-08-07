# 数据范围

数据范围回答"列表、统计、导出和批量操作能看到**哪些候选数据**"。它和功能权限、资源实例授权解决的是三个不同问题：功能权限判断"能否执行这类动作"，资源实例授权判断"能否操作这一条"，数据范围决定"集合里有哪些条"。典型场景是后台管理系统里的"本人的订单""本部门及下级部门的订单""我负责区域的客户"——这类**集合关系**必须在查询构造阶段翻译成 SQL 谓词，不能先把候选加载出来再逐条判断，否则分页总数、排序、导出和性能全都会错。

Framework 只定义策略、组合语义和查询扩展点，**不内置**"部门""组织树""本人/本部门"等业务概念：具体范围由业务项目注册 Provider 实现，组织展开、项目成员等关系仍留在业务表里，不复制到通用授权表。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 有后台列表、分页总数、聚合统计、导出或批量审批 | 引入本组件，所有集合入口统一走 `IDataScopeApplier` |
| 可见范围是"本人/本组织/下级组织/负责区域"等集合关系 | 引入本组件；用 ACL 物化这类关系会产生大量同步写与孤儿记录 |
| 数据量大，不可能加载候选后逐条检查 | 引入本组件 |
| 只按精确 ID 打开单个资源，几乎没有列表 | 不需要本组件，见[资源实例授权](./authorization-resource.md) |
| 只需要判断"能否执行这类动作" | 不需要本组件，见[功能权限](./authorization.md) |

去掉数据范围并不会让复杂度消失，只会把集合查询的复杂度转移到 ACL 的物化、JOIN、同步和清理上。

## 安装

```bash
dotnet add package Leistd.Authorization.DataScope.Core
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

本家族只有一个包，且只依赖 BCL 的 `Expression`/`IQueryable`，不依赖 EF Core，也不依赖 DDD 基座——`IRepository.GetQueryableAsync()` 返回的 `IQueryable<TEntity>` 可以直接交给本组件，不需要额外的适配包。

## 配置 Provider

```csharp
builder.Services.AddDataScopeCore();

// 业务实现：当前主体在某类资源的某个操作上被分配了哪些范围
builder.Services.AddScoped<IDataScopeAssignmentProvider, OrganizationScopeAssignmentProvider>();

// 每种范围一个 Provider，同一实体可注册多个
builder.Services.AddDataScopeProvider<Order, OwnOrderScopeProvider>();
builder.Services.AddDataScopeProvider<Order, OrganizationOrderScopeProvider>();
```

依赖调用方已注册 `IPermissionSubjectProvider`；`IDataScopeAssignmentProvider` 必须由业务项目提供，Framework 不规定范围分配存放在哪里（可以是角色配置表、组织架构，或外部策略服务）。

## 使用

**第一步：实现范围 Provider**——返回**谓词**而不是改写后的查询，这样多个范围才能取并集：

```csharp
public class OwnOrderScopeProvider(ICurrentUser currentUser) : IDataScopeProvider<Order>
{
    public const string Scope = "Own";

    public string ResourceName => "Orders";
    public string ScopeName => Scope;

    public ValueTask<Expression<Func<Order, bool>>?> BuildPredicateAsync(
        DataScopeContext context,
        CancellationToken cancellationToken = default)
    {
        var userId = context.Subject.UserId;
        return ValueTask.FromResult<Expression<Func<Order, bool>>?>(
            order => order.OwnerId == userId);
    }
}

public class OrganizationOrderScopeProvider : IDataScopeProvider<Order>
{
    public const string Scope = "Organization";

    public string ResourceName => "Orders";
    public string ScopeName => Scope;

    public ValueTask<Expression<Func<Order, bool>>?> BuildPredicateAsync(
        DataScopeContext context,
        CancellationToken cancellationToken = default)
    {
        // ScopeValue 的语义由本 Provider 解释；组织树展开属于业务职责。
        var organizationIds = context.Assignments
            .Where(x => x.ScopeName == Scope && x.ScopeValue is not null)
            .Select(x => x.ScopeValue!)
            .ToList();

        return ValueTask.FromResult<Expression<Func<Order, bool>>?>(
            order => organizationIds.Contains(order.OrganizationId));
    }
}

// 返回 null 表示"不施加任何限制"（全部可见），会短路整个并集。
public class AllOrderScopeProvider : IDataScopeProvider<Order>
{
    public string ResourceName => "Orders";
    public string ScopeName => "All";

    public ValueTask<Expression<Func<Order, bool>>?> BuildPredicateAsync(
        DataScopeContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<Expression<Func<Order, bool>>?>(null);
}
```

**第二步：实现分配来源**——范围按**操作**分别分配，因为"能看"不等于"能改"：

```csharp
public class OrganizationScopeAssignmentProvider(IRepository<RoleDataScope, Guid> scopes)
    : IDataScopeAssignmentProvider
{
    public async ValueTask<IReadOnlyList<DataScopeAssignment>> GetAssignmentsAsync(
        PermissionSubject subject,
        string resourceName,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var roleIds = subject.RoleIds.Select(Guid.Parse).ToList();
        var rows = await scopes.GetListAsync(
            x => roleIds.Contains(x.RoleId)
                 && x.ResourceName == resourceName
                 && x.Operation == operation,
            cancellationToken);

        return [.. rows.Select(x => new DataScopeAssignment(
            x.ResourceName, x.Operation, x.ScopeName, x.ScopeValue))];
    }
}
```

**第三步：所有集合入口统一走同一个范围**——列表、总数、导出必须共用，否则总数会和实际可见数据对不上：

```csharp
public class OrderAppService(
    IRepository<Order, Guid> orders,
    IDataScopeApplier dataScope,
    IQueryableAsyncExecuter asyncExecuter)
{
    public async Task<PagedResultDto<OrderDto>> GetPagedListAsync(
        GetOrderPagedInputDto input,
        CancellationToken ct)
    {
        var query = await orders.GetQueryableAsync(ct);

        // 先施加可见范围，再叠加业务筛选与排序分页。
        var scoped = await dataScope.ApplyAsync(query, "Orders", DataOperations.Read, ct);

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            scoped = scoped.Where(order => order.Code.Contains(input.Keyword));
        }

        var totalCount = await asyncExecuter.LongCountAsync(scoped, ct);
        var items = await asyncExecuter.ToListAsync(
            scoped.OrderBy(order => order.Code).Skip(input.Offset).Take(input.Limit), ct);

        return new PagedResultDto<OrderDto>(totalCount, mapper.Map<List<OrderDto>>(items));
    }

    // 批量操作先在范围内定位目标，再核对数量，禁止静默跳过越权项。
    public async Task ApproveManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var query = await orders.GetQueryableAsync(ct);
        var scoped = await dataScope.ApplyAsync(query, "Orders", DataOperations.Update, ct);

        var targets = await asyncExecuter.ToListAsync(scoped.Where(x => ids.Contains(x.Id)), ct);
        if (targets.Count != ids.Count)
        {
            throw new ForbiddenException("Some of the selected orders are out of your data scope.");
        }

        foreach (var order in targets)
        {
            order.Approve();
        }
    }
}
```

## 接口参考

`Leistd.Authorization.DataScope` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `DataOperations` | 内置操作名常量：`Read`、`Update`、`Delete`、`Export`；业务可自行扩展 |
| `DataScopeDefinition` | 范围定义元数据：`ResourceName`、`Operation`、`ScopeName`、`DisplayName`，供管理界面渲染 |
| `DataScopeAssignment` | 范围分配：`ResourceName`、`Operation`、`ScopeName`、`ScopeValue` |
| `DataScopeContext` | 解析上下文：`Subject`、`ResourceName`、`Operation`、`Assignments` |
| `IDataScopeProvider<TEntity>.ResourceName` / `.ScopeName` | 本 Provider 负责的资源与范围 |
| `IDataScopeProvider<TEntity>.BuildPredicateAsync(context, ct)` | 构造该范围的查询谓词；返回 `null` 表示不施加限制 |
| `IDataScopeAssignmentProvider.GetAssignmentsAsync(subject, resourceName, operation, ct)` | 业务实现：取当前主体在该资源该操作上的全部范围分配 |
| `IDataScopeApplier.ApplyAsync(query, resourceName, operation, ct)` | 把可见范围合并进查询，返回施加范围后的 `IQueryable<TEntity>` |
| `AddDataScopeCore()` | 注册 `IDataScopeApplier`（Scoped） |
| `AddDataScopeProvider<TEntity, TProvider>()` | 注册某实体的一种范围 Provider（Scoped） |

## 实现行为

`DefaultDataScopeApplier` 的组合规则：

1. 主体不可识别：返回空结果集（`Where(_ => false)`），默认拒绝而不是放行全部。
2. `IsSuperAdmin`：原样返回查询，不施加范围。
3. 没有任何分配：返回空结果集。
4. 分配了一个没有对应 Provider 的范围：跳过该分配，**不会**因此放宽范围。
5. 多个分配之间取**并集**（OR）：一个主体常常同时拥有多种范围（例如"本人"加"某几个组织"）。
6. 任一 Provider 返回 `null`：整体视为无限制，立即原样返回查询。
7. 合并两个独立 Lambda 时会把参数统一到同一个 `ParameterExpression` 上，否则合并结果无法被数据库翻译。

## 配置项 / Options

当前无配置项：两个 DI 扩展方法均无参数，也未暴露 Options 类。

## 注意事项

- **谓词必须可被数据库翻译**。不要在 `BuildPredicateAsync` 返回的表达式里调用只能客户端求值的方法，也不要先把候选加载到内存再过滤。请在**关系型** Provider 上编写测试：EF Core 的 InMemory Provider 全部在内存求值，不可翻译的谓词会静默通过，等于没有验证。
- **列表、总数、导出、批量必须共用同一个范围入口**，否则分页总数会与实际可见数据不一致。
- **读和写可以用不同范围**。`DataScopeAssignment` 带 `Operation` 维度，不要假设"能看就能改"。
- **硬边界不归本组件管**。租户隔离、软删除应通过 EF Core 全局查询过滤器始终生效，因此永远与业务范围做 AND，不会被这里的并集放宽。
- **不要把集合关系展开成资源 ACL**。组织或负责人一变就要重写大量记录并产生孤儿；反之，文档分享这类一次性授予也不适合做成范围。
- `IPermissionSubjectProvider` 与 `IDataScopeAssignmentProvider` 都没有默认实现，必须由业务项目提供。

## 可执行参考实现

本文档的示例代码不是凭空写的：`framework/tests/Leistd.Authorization.Pipeline.Tests` 是一个真实的
ASP.NET Core 宿主（真实 Web 宿主 + TestServer + Sqlite + 真实 DI 装配），把三层授权串起来跑通了
设计文档中的标准执行链，并覆盖了以下负向场景：

- 缺功能权限时在第一层就被拦下，不会走到数据范围；
- 范围外的详情返回 404 而非 403，不泄漏资源存在性；
- 在读取范围内但资源规则不放行 → 依然拿不到；
- 读取范围是整个组织、更新范围只有本人 —— 能看不等于能改；
- 领域规则的拒绝优先于所有者身份与 ACL 允许；
- 批量操作整体拒绝，而不是静默跳过越权项；
- 列表、总数与导出共用同一个范围入口，三者始终一致；
- ACL 以子查询合并进集合查询，显式拒绝会把资源从结果里移除。

改动本组件的公共行为时，请连同该工程一起更新——它是这些语义唯一的可执行事实来源。

## 相关

- [组件总览](./README.md)
- [功能权限](./authorization.md)
- [资源实例授权](./authorization-resource.md)
- [DDD 基座](../ddd-struct/ddd-struct.md)
