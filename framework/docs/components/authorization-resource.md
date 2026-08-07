# 资源实例授权

资源实例授权回答"对这个**已经定位到的**订单、文档或项目，当前主体能否执行这次操作"。它与功能权限解决的是不同问题：功能权限判断"能否执行这类动作"（`Orders.Update`），资源实例授权判断"能否改**这一条**订单"。前者不知道数据，后者必须先把数据加载出来。典型场景包括：只有订单所有者能修改自己的订单、只有项目成员能查看项目文档、已归档的单据任何人都不能删除、把某份文档临时分享给指定同事。

本组件提供两类来源并把它们合并成一次裁决：**规则处理器**表达所有者、成员关系、资源状态、时间窗口等领域逻辑；**资源 ACL** 表达管理员或所有者对某个具体实例做出的显式允许/拒绝。任一来源拒绝即拒绝，否则任一来源允许即允许，全部无结论时默认拒绝。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 规则可以完全由领域属性推导（所有者、状态、成员表） | 只引入 `Leistd.Authorization.Resource.Core`，注册规则处理器 |
| 需要"把这份文档分享给张三"这类由人手工授予的实例权限 | 再引入 `Leistd.Authorization.Resource.EntityFrameworkCore` |
| 需要按可见性过滤**列表**，而不只是判断单条 | 用 `QueryGrantedResourceKeys` 把 ACL 合并进查询；集合关系（本人/本组织）请改用[数据范围](./authorization-data-scope.md) |
| 只需要"能否执行这类动作" | 不需要本组件，见[功能权限](./authorization.md) |

不适用的情况：如果可见范围是"本人的全部订单""本部门及下级部门的全部订单"这类**集合关系**，不要把它展开成一条条 ACL——资源或组织关系一变就要重写大量记录，还会留下孤儿。那属于数据范围的职责。

## 安装

```bash
# 抽象 + 决策服务（操作、规则 Handler、ACL 抽象）
dotnet add package Leistd.Authorization.Resource.Core

# EF Core ACL 存储（显式实例授予持久化）
dotnet add package Leistd.Authorization.Resource.EntityFrameworkCore
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

本家族没有 AspNetCore 包：资源检查是**命令式**的，必须在实体加载之后执行，`[Authorize]` 在实体加载前就已运行完毕，没有可桥接的对象；403/404 的响应映射由 `Leistd.Exception.AspNetCore` 负责。

## 配置 Provider

```csharp
// 只用领域规则（不需要显式 ACL）
builder.Services.AddResourceAuthorizationCore();

// 需要 ACL 时改用这个，内部已调用 AddResourceAuthorizationCore
builder.Services.AddResourceAuthorizationEfCore<AppDbContext>();

// 为每种资源注册规则处理器，同一资源可注册多个
builder.Services.AddResourceAuthorizationHandler<Order, OrderOwnerHandler>();
builder.Services.AddResourceAuthorizationHandler<Order, ArchivedOrderHandler>();
```

EF Core 实体映射需在 `DbContext.OnModelCreating` 中显式应用：

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigureResourceAuthorization();
}
```

依赖调用方已注册 `IPermissionSubjectProvider`（提供"当前主体是谁、属于哪些角色、是否超管"），本组件不提供默认实现。

## 使用

**第一步：让资源自报家门**（可选，能省去手工传资源名与 Key）：

```csharp
public class Order : IAuthorizableResource
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = default!;
    public OrderStatus Status { get; set; }

    // 建议额外持有一个字符串资源 Key 列，使集合查询可被数据库翻译。
    public string ResourceKey { get; set; } = default!;

    string IAuthorizableResource.ResourceName => "Orders";
}
```

**第二步：用规则处理器表达领域逻辑**——不给结论的处理器什么都不调用即可：

```csharp
public class OrderOwnerHandler : IResourceAuthorizationHandler<Order>
{
    public ValueTask HandleAsync(
        ResourceAuthorizationContext<Order> context,
        CancellationToken cancellationToken = default)
    {
        if (context.Resource.OwnerId == context.Subject.UserId)
        {
            context.Allow();
        }

        return ValueTask.CompletedTask;
    }
}

public class ArchivedOrderHandler : IResourceAuthorizationHandler<Order>
{
    public ValueTask HandleAsync(
        ResourceAuthorizationContext<Order> context,
        CancellationToken cancellationToken = default)
    {
        // 已归档的订单任何人都不能删除，即使有 ACL 或所有者身份。
        if (context.Operation == ResourceOperations.Delete &&
            context.Resource.Status == OrderStatus.Archived)
        {
            context.Deny();
        }

        return ValueTask.CompletedTask;
    }
}
```

**第三步：在应用服务中先加载、再裁决**：

```csharp
public class OrderAppService(
    IRepository<Order, Guid> orders,
    IResourceAuthorizationService resourceAuthorization)
{
    // 功能权限由控制器上的 [Authorize(Policy = "Orders.Update")] 先行把关。
    public async Task UpdateAsync(Guid id, UpdateOrderInput input, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"Order '{id}' was not found.");

        if (!await resourceAuthorization.IsGrantedAsync(order, ResourceOperations.Update, ct))
        {
            // 资源存在性敏感时改为 NotFoundException，由 API 威胁模型统一决定。
            throw new ForbiddenException("You cannot update this order.");
        }

        order.Update(input.Amount);
        await orders.UpdateAsync(order, ct);
    }
}
```

**第四步：把 ACL 合并进列表查询**——列表**不能**先加载候选再逐条裁决，否则分页总数、排序和导出都会错：

```csharp
public async Task<PagedResultDto<OrderDto>> GetSharedWithMeAsync(
    PagedRequestDto input,
    CancellationToken ct)
{
    var subject = await subjectProvider.GetCurrentSubjectAsync(ct);

    // 返回的是 IQueryable，数据库据此生成 IN/EXISTS 子查询。
    var grantedKeys = resourceGrantStore.QueryGrantedResourceKeys(
        "Orders",
        ResourceOperations.Read,
        subject!.UserId,
        subject.RoleIds);

    var query = (await orders.GetQueryableAsync(ct))
        .Where(order => grantedKeys.Contains(order.ResourceKey));

    var totalCount = await asyncExecuter.LongCountAsync(query, ct);
    var items = await asyncExecuter.ToListAsync(query.Skip(input.Offset).Take(input.Limit), ct);

    return new PagedResultDto<OrderDto>(totalCount, mapper.Map<List<OrderDto>>(items));
}
```

**第五步：分享与回收**——`ReplaceGrantsAsync` 一次替换该实例上的全部 ACL：

```csharp
await resourceGrantManager.ReplaceGrantsAsync("Orders", order.ResourceKey,
[
    new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, targetUserId, PermissionGrantEffect.Granted),
    new ResourceGrant(ResourceOperations.Update, PermissionGrantProviderNames.Role, reviewerRoleId, PermissionGrantEffect.Granted),
    // 显式拒绝优先于任何来源的允许，用于"这个人例外"。
    new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, blockedUserId, PermissionGrantEffect.Prohibited),
], ct);
```

**第六步：资源删除后清理 ACL**：

```csharp
await orders.DeleteAsync(order, ct);
await resourceGrantManager.RemoveResourceAsync("Orders", order.ResourceKey, ct);
```

## 接口参考

`Leistd.Authorization.Resource` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `ResourceOperations` | 内置操作名常量：`Read`、`Update`、`Delete`、`Share`；业务可自行扩展任意字符串 |
| `IAuthorizableResource` | 资源自述接口：`ResourceName`、`ResourceKey` |
| `ResourceAuthorizationDecision` | 判定结果：`Undefined`、`Allowed`、`Denied` |
| `ResourceAuthorizationContext<TResource>` | 判定上下文：`Subject`、`ResourceName`、`ResourceKey`、`Operation`、`Resource`、`Decision`、`Allow()`、`Deny()` |
| `IResourceAuthorizationHandler<TResource>.HandleAsync(context, ct)` | 领域规则处理器；同一资源可注册多个，全部执行 |
| `IResourceAuthorizationService.IsGrantedAsync(resource, resourceName, resourceKey, operation, ct)` | 对已加载实例裁决 |
| `IResourceAuthorizationService.IsGrantedAsync(resource, operation, ct)` | 资源实现 `IAuthorizableResource` 时的简化重载 |
| `ResourceGrant` | 单条 ACL：`Operation`、`ProviderName`、`ProviderKey`、`Effect` |
| `IResourceGrantStore.GetGrantsAsync(resourceName, resourceKey, ct)` | 取某实例上的全部 ACL |
| `IResourceGrantStore.GetEffectiveGrantsAsync(resourceName, resourceKey, userId, roleIds, ct)` | 取指定主体在某实例上每个操作的最终效果（拒绝优先） |
| `IResourceGrantStore.QueryGrantedResourceKeys(resourceName, operation, userId, roleIds)` | 集合级入口，返回可被数据库翻译的 `IQueryable<string>`，已排除显式拒绝 |
| `IResourceGrantManager.ReplaceGrantsAsync(resourceName, resourceKey, grants, ct)` | 原子替换某实例的全部 ACL |
| `IResourceGrantManager.RemoveResourceAsync(resourceName, resourceKey, ct)` | 幂等清理某实例的全部 ACL，返回删除行数 |

`Leistd.Authorization.Resource.EntityFrameworkCore` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `ResourcePermissionGrantRecord` | ACL 持久化实体：`Id`（Guid v7）、`ResourceName`、`ResourceKey`、`Operation`、`ProviderName`、`ProviderKey`、`Effect`；实现 `ICreationAuditedObject` |
| `ResourcePermissionGrantRecordConfiguration` | EF Core 实体配置 |
| `EfCoreResourceGrantStore<TDbContext>` | `IResourceGrantStore` 的 EF Core 实现 |
| `EfCoreResourceGrantManager<TDbContext>` | `IResourceGrantManager` 的 EF Core 实现 |
| `AddResourceAuthorizationEfCore<TDbContext>()` | 注册存储与管理器（Scoped），内部调用 `AddResourceAuthorizationCore()` |
| `ConfigureResourceAuthorization()` | 在 `OnModelCreating` 中应用实体映射 |

## 实现行为

- `DefaultResourceAuthorizationService` 的判定顺序：主体不可识别返回 `false`；`IsSuperAdmin` 直接返回 `true`（与功能权限口径一致）；依次执行全部规则处理器，任一 `Deny()` 立即判定拒绝；随后查 ACL，命中 `Prohibited` 判定拒绝、命中 `Granted` 视为一次允许；最终只有 `Decision == Allowed` 才放行，`Undefined` 按默认拒绝处理。
- `ResourceAuthorizationContext.Allow()` 不会覆盖已经发生的 `Deny()`，因此处理器的注册顺序不影响结果。
- 未注册 `IResourceGrantStore` 时服务仍可工作，只有规则处理器参与判定。
- `ResourcePermissionGrantRecord` 唯一索引为 `(ResourceName, ResourceKey, Operation, ProviderName, ProviderKey)`，同一主体对同一实例同一操作不会出现两条冲突记录；另有 `(ResourceName, Operation, ProviderName, ProviderKey)` 支撑集合查询、`(ResourceName, ResourceKey)` 支撑删除清理。
- `QueryGrantedResourceKeys` 在数据库内完成"允许集合减去拒绝集合"，不把候选拉到内存；返回结果已 `Distinct()`。
- `Effect` 以**字符串**持久化（`varchar(32)`），与功能权限授予表口径一致。
- 字段长度约束：`ResourceName` 128、`ResourceKey` 128、`Operation` 64、`ProviderName` 32、`ProviderKey` 128、`Effect` 32、`CreatorId` 64。

## 配置项 / Options

当前无配置项：两个 DI 扩展方法均无参数，也未暴露 Options 类。

## 注意事项

- **必须先加载资源再裁决**。在实体加载前用 `[Authorize]` 做实例级判断是做不到的，这也是本家族不提供 AspNetCore 包的原因。
- **列表不要逐条裁决**。请用 `QueryGrantedResourceKeys` 合并进查询；逐行调用 `IsGrantedAsync` 会让分页总数、排序和导出全部失真。
- **业务实体建议持有字符串资源 Key 列**。ACL 的 `ResourceKey` 是字符串，若业务主键是 `Guid` 并在查询里调用 `.ToString()`，能否翻译取决于数据库 Provider。
- **通用 ACL 表无法对任意业务表建立外键**。资源删除后需显式调用 `RemoveResourceAsync`，并安排周期性孤儿记录清理。
- **不要用 ACL 表达集合关系**。"本人/本部门/下级部门"请用[数据范围](./authorization-data-scope.md)，否则关系一变就要重写大量 ACL。
- `IPermissionSubjectProvider` 没有默认实现，必须由业务项目提供，否则所有判定都会因取不到主体而拒绝。

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
- [数据范围](./authorization-data-scope.md)
- [当前用户与身份信息](./security.md)
