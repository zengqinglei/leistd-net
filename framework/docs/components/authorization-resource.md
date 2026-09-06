# 资源实例授权

资源授权判断当前主体能否对某个已加载的实例执行操作。领域规则与 ACL 共同决策，拒绝优先，无结论时默认拒绝。

## 何时使用

| 场景 | 组合 |
| --- | --- |
| 按所有者、状态等领域属性判定 | `Leistd.Authorization.Resource.Core` + 规则处理器 |
| 支持“将这份文档分享给某人” | 追加 `Leistd.Authorization.Resource.EntityFrameworkCore` |
| ACL 需合并进列表查询 | `QueryGrantedResourceKeysAsync` / `QueryDeniedResourceKeysAsync` |

“本人的全部订单”或“本部门及下级部门”属于[数据范围](./authorization-data-scope.md)，不应展开成逐条 ACL。只检查操作类型时使用[功能权限](./authorization.md)。

## 安装

```bash
dotnet add package Leistd.Authorization.Resource.Core
dotnet add package Leistd.Authorization.Resource.EntityFrameworkCore
```

本家族没有 AspNetCore 包。实例判定必须在资源加载后执行，无法由加载前的 `[Authorize]` 策略取代。

## 注册

```csharp
builder.Services.AddResourceAuthorizationEfCore<AppDbContext>();
builder.Services.AddResourceAuthorizationHandler<Order, OrderOwnerHandler>();
builder.Services.AddResourceAuthorizationHandler<Order, ArchivedOrderHandler>();
```

仅使用领域规则时改为 `AddResourceAuthorizationCore()`。同一资源可注册多个处理器。

EF Core 宿主还需映射 ACL 与版本表：

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigureResourceAuthorization();
}
```

ACL Store/Manager 依赖 `IDbContextProvider<TDbContext>`，因此需先注册 `AddUnitOfWork()` 与 `AddUnitOfWorkEfCore()`。业务项目也必须提供 `IPermissionSubjectProvider`。

## 使用

### 定义资源与规则

```csharp
public class Order : IAuthorizableResource
{
    public string OwnerId { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public string ResourceKey { get; set; } = string.Empty;
    string IAuthorizableResource.ResourceName => "Orders";
}

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
```

处理器不给结论时什么都不调用。禁止某个资源状态下的操作时调用 `context.Deny()`。`Deny()` 不会被后续 `Allow()` 覆盖，处理器顺序不影响结果。

实例判定顺序为：

1. 无法识别主体时拒绝。
2. 执行所有规则处理器；任一 `Deny()` 立即拒绝。
3. 超级管理员允许。
4. 查询 ACL；`Prohibited` 优先于 `Granted`。
5. 只有最终结果为 `Allowed` 时允许。

超级管理员不会跳过领域规则。未注册 ACL Store 时，只使用规则处理器判定。

### 判定单个实例

```csharp
var order = await orders.FindAsync(id, ct)
    ?? throw new NotFoundException();

if (!await authorization.IsGrantedAsync(
        order,
        ResourceOperations.Update,
        ct))
{
    throw new ForbiddenException();
}
```

功能权限应在控制器上先行检查，实例授权在加载后检查。存在性敏感的 API 可在拒绝时统一返回 404，但应由宿主的威胁模型决定。

### 将 ACL 合并进列表

```csharp
var subject = await subjectProvider.GetCurrentSubjectAsync(ct);
var grantedKeys = await grantStore.QueryGrantedResourceKeysAsync(
    "Orders",
    ResourceOperations.Read,
    subject!.UserId,
    subject.RoleIds);

var query = dbContext.Set<Order>()
    .Where(order => grantedKeys.Contains(order.ResourceKey));
```

列表、详情、总数与导出应共用同一个查询授权入口。非超管的集合公式为：

```text
(数据范围 OR ACL 允许) AND NOT ACL 拒绝
```

`QueryGrantedResourceKeysAsync` 已在数据库中扣除 ACL 拒绝。与数据范围组合时，还需使用 `QueryDeniedResourceKeysAsync` 扣除“数据范围允许、ACL 明确拒绝”的部分。超管跳过数据范围与 ACL，但仍受租户和软删除边界约束。

集合入口不执行依赖资源状态的规则处理器。批量写操作应先按操作范围定位，再逐项执行实例判定；任一拒绝时拒绝整批。

### 替换与清理 ACL

```csharp
var current = await grantStore.GetGrantsAsync("Orders", order.ResourceKey);

await grantManager.ReplaceGrantsAsync(
    "Orders",
    order.ResourceKey,
    [
        new ResourceGrant(
            ResourceOperations.Read,
            PermissionGrantProviderNames.User,
            targetUserId,
            ResourceGrantEffect.Granted),
    ],
    current.Version,
    ct);
```

替换基于版本执行乐观并发。版本不匹配时抛 `ResourceGrantConcurrencyException` （409）且不写入；`null` 只应用于种子数据。

资源删除后必须清理 ACL：

```csharp
await grantManager.RemoveResourceAsync("Orders", order.ResourceKey, ct);
```

用户、角色或客户端永久删除时，同时清理功能权限与资源 ACL：

```csharp
await permissionGrantManager.RemoveProviderAsync(
    PermissionGrantProviderNames.User,
    userId,
    ct);
await grantManager.RemoveProviderAsync(
    PermissionGrantProviderNames.User,
    userId,
    ct);
```

主体标识被重用时，遗留 ACL 会重新生效。软删除主体可恢复，不调用清理入口。

## 接口参考

| 类型 | 用途 |
| --- | --- |
| `ResourceOperations` | 内置 `Read`、`Update`、`Delete` 和 `Share` 操作名 |
| `IAuthorizableResource` | 通过 `ResourceName` 和 `ResourceKey` 自述实例 |
| `IResourceAuthorizationHandler<TResource>` | 资源领域规则处理器 |
| `IResourceAuthorizationService` | 对已加载资源进行实例判定 |
| `IResourceGrantStore` | 读取 ACL、有效效果和可翻译的资源 key 查询 |
| `IResourceGrantManager` | 替换 ACL，清理资源或主体的 ACL |
| `ResourceGrant` / `ResourceGrantSet` | 单条 ACL 与某实例的完整 ACL 快照 |

| 异常 | HTTP | 含义 |
| --- | --- | --- |
| `InvalidResourceGrantException` | 400 | 主体或授予效果无效 |
| `ResourceGrantConcurrencyException` | 409 | 保存基于过期版本 |
| `UnstableGrantSnapshotException` | 503 | 连续重读仍无法获得一致快照 |

## 实现行为

- ACL 唯一键为 `(ResourceName, ResourceKey, Operation, ProviderName, ProviderKey)`，单个主体对同一实例操作只有一个效果。
- `QueryGrantedResourceKeysAsync` 在数据库内完成允许集合减拒绝集合，并返回去重的 `IQueryable<string>`。
- `ResourceAuthorizationVersionRecord.Version` 是 EF Core 并发令牌。全量替换只在 ACL 实际变化时递增版本。
- `RemoveProviderAsync` 保留资源版本行并推进受影响资源的版本，防止旧编辑快照将已删授予写回。
- `ResourceGrantEffect` 以字符串持久化，只有已定义的 `Granted` 会产生允许；非法值失败关闭。

## 注意事项

- 单实例必须先加载再判定，列表必须在数据库查询中合并 ACL，不逐行加载后判定。
- 同一个 Read 只判定一次；详情经统一可见查询定位后，不再重复调用实例判定。
- 影响读取可见性的规则必须有可翻译的等价谓词；否则只用于加载后的领域不变量。
- 业务实体应持有字符串资源 key；在查询中对 Guid 调用 `ToString()` 是否可翻译取决于 Provider。
- 通用 ACL 表无法对任意业务表建立外键。资源删除后调用 `RemoveResourceAsync`，并定期清理孤儿记录。
- `IPermissionSubjectProvider` 没有默认实现，且是 `DefaultPermissionChecker` 的必需构造参数：未注册时解析权限校验器直接失败，而不是判定为拒绝。

## 相关

- [功能权限](./authorization.md)
- [数据范围](./authorization-data-scope.md)
- [当前用户与身份信息](./security.md)
