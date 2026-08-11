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

EF Core 实体映射需在 `DbContext.OnModelCreating` 中显式应用（**并生成迁移**，见下）：

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

**第三步：先加载、再裁决**——本组件只回答"能不能"，加载与持久化用你自己的数据访问方式：

```csharp
public class OrderService(IOrderStore orders, IResourceAuthorizationService resourceAuthorization)
{
    // 功能权限由控制器上的 [Authorize(Policy = "Orders.Update")] 先行把关。
    public async Task UpdateAsync(Guid id, decimal amount, CancellationToken ct)
    {
        var order = await orders.FindAsync(id, ct)
            ?? throw new KeyNotFoundException($"Order '{id}' was not found.");

        if (!await resourceAuthorization.IsGrantedAsync(order, ResourceOperations.Update, ct))
        {
            // 资源存在性敏感时改为"未找到"，由 API 威胁模型统一决定。
            throw new UnauthorizedAccessException("You cannot update this order.");
        }

        order.Amount = amount;
        await orders.SaveAsync(order, ct);
    }
}
```

**第四步：把 ACL 合并进列表查询**——列表**不能**先加载候选再逐条裁决，否则分页总数、排序和导出都会错：

```csharp
var subject = await subjectProvider.GetCurrentSubjectAsync(ct);

// 返回 IQueryable，数据库据此生成 IN/EXISTS 子查询，不把候选拉到内存。
var grantedKeys = resourceGrantStore.QueryGrantedResourceKeys(
    "Orders",
    ResourceOperations.Read,
    subject!.UserId,
    subject.RoleIds);

var query = dbContext.Set<Order>().Where(order => grantedKeys.Contains(order.ResourceKey));

var totalCount = await query.LongCountAsync(ct);
var items = await query.Skip(offset).Take(limit).ToListAsync(ct);
```

> 与仓储、分页 DTO、异步执行器等 DDD 设施的组合写法见 [ddd-struct 文档](../ddd-struct/README.md)；本组件不依赖它们，示例刻意保持在 EF Core 与 BCL 的范围内，独立引用本包的项目可直接照搬。

**第五步：分享与回收**——`ReplaceGrantsAsync` 一次替换该实例上的全部 ACL，并用版本号做乐观并发：

```csharp
var current = await resourceGrantStore.GetGrantsAsync("Orders", order.ResourceKey);
await resourceGrantManager.ReplaceGrantsAsync("Orders", order.ResourceKey,
[
    new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, targetUserId, ResourceGrantEffect.Granted),
    new ResourceGrant(ResourceOperations.Update, PermissionGrantProviderNames.Role, reviewerRoleId, ResourceGrantEffect.Granted),
    // 显式拒绝优先于任何来源的允许，用于"这个人例外"。
    new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, blockedUserId, ResourceGrantEffect.Prohibited),
], current.Revision, ct);
// 版本不符时抛 ResourceGrantConcurrencyException：另一位管理员抢先保存了，
// 此时直接写入会把对方刚加的显式拒绝静默抹掉，而两次保存都显示成功。
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
| `ResourceGrantSet` | 某实例的完整 ACL 与版本：`ResourceName`、`ResourceKey`、`Grants`、`Revision` |
| `ResourceGrantConcurrencyException` | ACL 版本冲突：`ExpectedRevision`、`ActualRevision`（取自冲突后的重新读取），宿主通常映射为 HTTP 409 |
| `InvalidResourceGrantEffectException` | 写入了未定义的 `ResourceGrantEffect` |
| `InvalidResourceGrantSubjectException` | 写入了读取端无法识别的主体（非 User/Role 的 ProviderName、空标识） |
| `UnstableGrantSnapshotException` | 稳定读取重试耗尽：取不到一致快照。属**读取失败**，与 `ResourceGrantConcurrencyException`（保存冲突，映射 409）不是一回事，调用方重试即可 |
| `IResourceGrantStore.GetGrantsAsync(resourceName, resourceKey, ct)` | 取某实例上的全部 ACL 与当前版本，返回 `ResourceGrantSet` |
| `IResourceGrantStore.GetEffectiveGrantsAsync(resourceName, resourceKey, userId, roleIds, ct)` | 取指定主体在某实例上每个操作的最终效果（拒绝优先） |
| `IResourceGrantStore.QueryGrantedResourceKeys(resourceName, operation, userId, roleIds)` | 集合级入口，返回可被数据库翻译的 `IQueryable<string>`，已排除显式拒绝 |
| `IResourceGrantManager.ReplaceGrantsAsync(resourceName, resourceKey, grants, expectedRevision, ct)` | 原子替换某实例的全部 ACL，返回写入后的版本；`expectedRevision` 与存储不一致时抛 `ResourceGrantConcurrencyException`，传 `null` 跳过校验（仅限种子数据） |
| `IResourceGrantManager.RemoveResourceAsync(resourceName, resourceKey, ct)` | 幂等清理某实例的全部 ACL，返回删除行数 |

`Leistd.Authorization.Resource.EntityFrameworkCore` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `ResourcePermissionGrantRecord` | ACL 持久化实体：`Id`（Guid v7）、`ResourceName`、`ResourceKey`、`Operation`、`ProviderName`、`ProviderKey`、`Effect`；实现 `ICreationAuditedObject` |
| `ResourcePermissionGrantRecordConfiguration` | EF Core 实体配置 |
| `ResourceAuthorizationRevisionRecord` | 资源实例的 ACL 版本：`Id`、`ResourceName`、`ResourceKey`、`Version`；`Version` 为并发令牌 |
| `EfCoreResourceGrantStore<TDbContext>` | `IResourceGrantStore` 的 EF Core 实现 |
| `EfCoreResourceGrantManager<TDbContext>` | `IResourceGrantManager` 的 EF Core 实现 |
| `AddResourceAuthorizationEfCore<TDbContext>()` | 注册存储与管理器（Scoped），内部调用 `AddResourceAuthorizationCore()` |
| `ConfigureResourceAuthorization()` | 在 `OnModelCreating` 中应用实体映射 |

## 实现行为

- `DefaultResourceAuthorizationService` 的判定顺序：主体不可识别返回 `false`；依次执行全部规则处理器，任一 `Deny()` **立即拒绝**；随后 `IsSuperAdmin` 返回 `true`；否则查 ACL，命中 `Prohibited` 判定拒绝、命中 `Granted` 视为一次允许；最终只有 `Decision == Allowed` 才放行，`Undefined` 按默认拒绝处理。
- **超级管理员在领域规则之后才旁路**，与功能权限层不同：那一层没有领域不变量，只回答"能不能做这类事"；这一层的规则处理器表达的是资源状态本身不允许（已归档的订单谁都不能删），与"谁"无关。让超管跳过处理器会让上面那句承诺当场失效。
- `ResourceAuthorizationContext.Allow()` 不会覆盖已经发生的 `Deny()`，因此处理器的注册顺序不影响结果。
- 未注册 `IResourceGrantStore` 时服务仍可工作，只有规则处理器参与判定。
- `ResourcePermissionGrantRecord` 唯一索引为 `(ResourceName, ResourceKey, Operation, ProviderName, ProviderKey)`，同一主体对同一实例同一操作不会出现两条冲突记录；另有 `(ResourceName, Operation, ProviderName, ProviderKey)` 支撑集合查询、`(ResourceName, ResourceKey)` 支撑删除清理。
- `QueryGrantedResourceKeys` 在数据库内完成"允许集合减去拒绝集合"，不把候选拉到内存；返回结果已 `Distinct()`。
- **集合可见性要三者组合**：非超管为 `(数据范围 OR ACL 允许) AND NOT ACL 拒绝`；超管旁路数据范围与 ACL（含拒绝集合），只受租户、软删除这类硬边界约束。集合与单实例必须同一口径——实例判定已让超管跳过 ACL，集合这边若照减拒绝集合，就会出现"列表里看不见、按 ID 却打得开"。只用前一个入口减不掉"数据范围放行、ACL 显式拒绝"的那一份——而"分享给部门、排除这一个人"正是显式拒绝的唯一用途。拒绝集合由 `QueryDeniedResourceKeys` 单独给出，与 `QueryGrantedResourceKeys` 对称，判据同为"不是 `Granted` 即拒绝"。
- **集合入口答不了领域规则**：它只回答"哪些看得见/改得动"。批量操作必须在范围内取到目标后逐项执行实例授权，任一拒绝整批拒绝；只比对数量会漏掉已归档这类由资源状态决定的拒绝。
- **判定 fail-closed**：只有明确的 `Granted` 才允许。写成"是 `Prohibited` 就拒、否则放行"会让任何非法枚举值（`(ResourceGrantEffect)0`、越界数值、自定义 Store 返回的损坏值）静默变成允许。写入端另有 `Enum.IsDefined` 校验与数据库检查约束两道拦截。
- **全量替换带乐观并发**：唯一索引只防重复行，防不住"两人基于同一份旧快照各自保存"——被覆盖掉的往往正是显式拒绝，那个本该被排除的人会重新经由角色拿到访问权，且两次保存都显示成功。因此 ACL 与功能权限用同一口径：读取带回版本，保存回传版本，冲突抛异常而非静默覆盖。
- `Effect` 以**字符串**持久化（`varchar(32)`）。功能权限授予表没有这一列——那一层是纯加法，行的存在即授予；资源实例这一层保留 `ResourceGrantEffect`，因为"这一条例外"是真实诉求。
- 字段长度约束：`ResourceName` 128、`ResourceKey` 128、`Operation` 64、`ProviderName` 32、`ProviderKey` 128、`Effect` 32、`CreatorId` 64。

## 升级已有数据库

`ConfigureResourceAuthorization()` 只负责模型映射，不会自动改库。既有项目升级本组件后必须生成并应用一次迁移，否则第一次读取 ACL 就会因缺表失败：

- **新增表 `ResourceAuthorizationRevisions`**（`Id`、`ResourceName`、`ResourceKey`、`Version` + `(ResourceName, ResourceKey)` 唯一索引）。缺失时 `GetGrantsAsync` 直接抛错。
- **新增检查约束 `CK_ResourcePermissionGrants_Effect`**，限定 `Effect` 只能是 `Granted` / `Prohibited`。
- 存量数据若含非法 `Effect`，迁移会因约束失败。**先清理再加约束**：这些行在判定端一律按拒绝处理（读取端 fail-closed），因此清理时应确认它们本就该是拒绝，或直接删除。
- **必须为每个已有 ACL 的 `(ResourceName, ResourceKey)` 回填一行版本记录**（`Version = 1`）。不回填会留下"有 ACL、无版本行"的状态：版本是并发令牌，删除与替换靠它互相拦截，没有这一行时并发的 `RemoveResourceAsync` 与 `ReplaceGrantsAsync` 谁也拦不住谁，可能留下半套 ACL 或孤立版本。

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
