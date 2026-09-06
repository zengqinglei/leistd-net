# 权限授权

权限授权将可执行操作定义为稳定名称，按用户和角色授予，并通过 `IPermissionChecker` 或 ASP.NET Core 策略进行判定。

## 何时使用

| 场景 | 组合 |
| --- | --- |
| 业务代码定义或检查功能权限 | `Leistd.Authorization.Core` |
| 控制器通过 `[Authorize(Policy = "...")]` 检查权限 | 追加 `Leistd.Authorization.AspNetCore` |
| 授予需运行时持久化 | 追加 `Leistd.Authorization.EntityFrameworkCore` |

本家族只回答“能否执行这类操作”。单个资源的判定见[资源实例授权](./authorization-resource.md)，集合可见范围见[数据范围](./authorization-data-scope.md)。

## 安装

```bash
dotnet add package Leistd.Authorization.Core
dotnet add package Leistd.Authorization.AspNetCore
dotnet add package Leistd.Authorization.EntityFrameworkCore
```

## 注册

```csharp
builder.Services.AddAuthorization();
builder.Services.AddPermissionAuthorization();
builder.Services.AddAuthorizationEfCore<AppDbContext>();

builder.Services.AddSingleton<
    IPermissionDefinitionProvider,
    OrdersPermissionDefinitionProvider>();
builder.Services.AddScoped<IPermissionSubjectProvider, CurrentPermissionSubjectProvider>();
```

`AddPermissionAuthorizationCore()` 只注册定义管理器与检查器。`AddPermissionAuthorization()` 在此基础上接入 ASP.NET Core 策略管道。`AddAuthorizationEfCore<TDbContext>()` 注册授予 Store 与 Manager。

EF Core 宿主还需映射表：

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigureAuthorization();
}
```

EF Core 实现通过 `IDbContextProvider<TDbContext>` 取上下文，因此宿主须先注册 `AddUnitOfWork()` 与 `AddUnitOfWorkEfCore()`。本家族不代为注册这些基础设施。

## 使用

### 定义权限

```csharp
public class OrdersPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetOrAddGroup("Orders", "订单管理");
        var orders = group.AddPermission("Orders", "订单管理");
        orders.AddChild("Orders.Read", "查看订单");
        orders.AddChild("Orders.Write", "编辑订单");

        var system = context.GetOrAddGroup(
            "System",
            "系统管理",
            MultiTenancySides.Host);
        system.AddPermission("System.Tenants", "租户管理");
    }
}
```

每个权限必须属于权限组，名称全局唯一。子权限继承父权限和组的多租户侧别；宿主侧权限在租户上下文中始终拒绝。

定义在 `PermissionDefinitionManager` 首次构造时加载并预计算祖先、子孙与有效状态。修改定义需重启进程；授予可运行时更改。

### 检查权限

```csharp
var granted = await permissionChecker.IsGrantedAsync(
    "Orders.Read",
    cancellationToken);
```

默认判定顺序为：

1. 权限必须已定义，且自身与所有祖先均启用。
2. 权限的 `MultiTenancySides` 必须与当前上下文匹配。
3. `IPermissionSubjectProvider` 必须返回当前主体。
4. 超级管理员直接允许；其余主体以用户授予与角色授予的并集判定。

未定义、未启用、侧别不匹配或无主体时都默认拒绝。同一 Scoped 作用域内的主体与授予快照只加载一次。批量检查的空集合 `AllGranted` 为 `false`。

### 使用 ASP.NET Core 策略

```csharp
[Authorize(Policy = "Orders.Read")]
[HttpGet("orders")]
public Task<IReadOnlyList<OrderDto>> GetOrders() => orderService.GetListAsync();

[Authorize(Policy = "Orders.Read|Orders.Export")]
[HttpGet("orders/export")]
public Task<FileResult> ExportOrders() => orderService.ExportAsync();
```

`|` 表示任一权限满足。只有每一段都是已定义权限时，`PermissionPolicyProvider` 才动态构建策略；否则交回 ASP.NET Core 默认提供器。宿主显式注册的同名策略优先。

### 授予与撤销

```csharp
await grantManager.GrantAsync(
    "Orders.Write",
    PermissionGrantProviderNames.Role,
    roleId);

await grantManager.RevokeAsync(
    "Orders",
    PermissionGrantProviderNames.Role,
    roleId);
```

授予子权限会补齐全部祖先；撤销父权限会清理全部子孙。授予是纯加法，行的存在即表示授予，不存在“拒绝”状态。

权限编辑页使用带版本的全量替换：

```csharp
var current = await grantStore.GetGrantsAsync(
    PermissionGrantProviderNames.Role,
    roleId);

await grantManager.ReplaceGrantsAsync(
    PermissionGrantProviderNames.Role,
    roleId,
    ["Orders", "Orders.Read"],
    expectedVersion: current.Version);
```

版本不匹配时抛 `PermissionGrantConcurrencyException` （409）且不写入。稳定读取在授予前后比对版本；持续不一致时抛 `UnstableGrantSnapshotException` （503）。

## 接口参考

| 类型 | 用途 |
| --- | --- |
| `IPermissionDefinitionProvider` | 定义权限组与权限树 |
| `IPermissionDefinitionManager` | 查询定义、祖先、子孙与有效状态 |
| `IPermissionSubjectProvider` | 提供当前用户、角色与超级管理员状态 |
| `IPermissionChecker` | 单个或批量检查权限 |
| `IPermissionGrantStore` | 读取主体授予、有效权限与版本 |
| `IPermissionGrantManager` | 授予、撤销、全量替换和永久删除主体的授予清理 |
| `PermissionSubject` | 当前用户 Id、角色 Id 和超级管理员标记 |
| `PermissionGrantSet` | 单个主体的授予集合与版本 |
| `SubjectPermissionGrants` | 用户与所有角色授予的快照及 `VersionToken` |

| 异常 | HTTP | 含义 |
| --- | --- | --- |
| `UndefinedPermissionException` | 400 | 尝试授予未定义或未启用的权限 |
| `PermissionGrantConcurrencyException` | 409 | 保存基于过期版本 |
| `UnstableGrantSnapshotException` | 503 | 连续重读仍无法取得一致快照 |

## 实现行为

- `PermissionGrantRecord` 与 `AuthorizationVersionRecord` 按当前租户过滤。宿主行与租户行使用分离的部分唯一索引，避免可空 `TenantId` 使宿主授予失去唯一性。
- `ReplaceGrantsAsync` 在一次 `SaveChangesAsync` 中原子替换，只在内容变化时递增版本。版本同时是 EF Core 并发令牌。
- 用户与角色授予的读取为常数数量的数据库往返，不按角色或权限逐条查询。
- `SubjectPermissionGrants.VersionToken` 组合用户版本与按 key 排序的角色版本，可用于判断客户端权限缓存是否过期。

## 注意事项

- `IPermissionSubjectProvider` **刻意没有默认实现**，未注册时 `DefaultPermissionChecker` 在
  DI 解析阶段直接失败（不是"静默拒绝"）。框架给不出正确的默认值，原因有三，缺一条都会
  变成看起来能用的错实现：
  - `PermissionSubject.RoleIds` 是**角色 Id**，而 claim 里通常只有角色**名**。拿名字充当 Id
    会让 `IPermissionGrantStore` 查不到任何授予，且不报错。
  - `IsSuperAdmin` 必须来自可信来源。若从 claim 读，被降权的超管在令牌过期前仍是超管。
  - **账号失效必须每请求判定**。登录时拒绝禁用与锁定账号，但已签发的 Cookie/Bearer 不会因此
    失效；主体解析是 RBAC 路径上的失效保障，跳过它等于"禁用用户"只挡新登录，已在线的会话
    照常调用受权限保护的接口。
  实现参照模板生成项目里的主体提供器：接业务的用户与角色模型，每请求查库。
- `IsSuperAdmin` 的来源必须可信；它会在定义、启用状态与多租户侧别校验通过后跳过 Store。
- 所有授予写入都经 `IPermissionGrantManager`，不直接写 DbContext，否则会绕过定义校验、祖先补齐与版本。
- 主体永久删除时调用 `RemoveProviderAsync`；软删除不清理授予。
- 权限管理 UI 从 `IPermissionDefinitionManager.GetGroups()` 构造，不在前端复制权限列表。

## 相关

- [多租户](./multi-tenancy.md)
- [资源实例授权](./authorization-resource.md)
- [数据范围](./authorization-data-scope.md)
- [审计](./auditing.md)
