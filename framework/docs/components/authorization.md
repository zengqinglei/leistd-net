# 权限授权

权限授权将可执行操作定义为稳定名称，按用户和角色授予，并通过 `IPermissionChecker` 或 ASP.NET Core 策略进行判定。

## 何时使用

| 场景 | 组合 |
| --- | --- |
| 业务代码定义或检查功能权限 | `Leistd.Authorization.Core` |
| 控制器通过 `[Authorize(Policy = "...")]` 检查权限 | 追加 `Leistd.Authorization.AspNetCore` |
| 授予需运行时持久化 | 追加 `Leistd.Authorization.EntityFrameworkCore` |
| 权限管理界面：当前有效权限、定义树、按主体读写授予 | `MapPermissionManagement()`，宿主实现 `IPermissionSubjectDirectory` |
| 初始化时给管理员角色授予全部权限（只在从未写过时） | `IPermissionGrantSeeder` |

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

`AddPermissionAuthorizationCore()` 注册定义管理器、检查器、管理用例与首次授予（`configure` 给显示名翻译资源）。`AddPermissionAuthorization()` 在此基础上接入 ASP.NET Core 策略管道。`AddAuthorizationEfCore<TDbContext>()` 注册授予 Store 与 Manager。

EF Core 宿主还需映射表：

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigureAuthorization();
}
```

EF Core 实现通过 `IDbContextProvider<TDbContext>` 取上下文，因此宿主须先注册 `AddUnitOfWork()` 与 `AddUnitOfWorkEfCore()`。本家族不代为注册这些基础设施。

权限管理端点（主体是否存在、显示名由宿主回答）：

```csharp
builder.Services.AddScoped<IPermissionSubjectDirectory, RoleSubjectDirectory>();

app.MapGroup("/api/v1/permissions").MapPermissionManagement(options =>
{
    // 读自己已获授权
    options.CurrentPolicy = "App.CurrentUser";
    options.DefinitionsPolicy = "Orders.Permissions|Orders.Roles.ManagePermissions";
    options.GrantPolicies[PermissionGrantProviderNames.Role] = "Orders.Roles.ManagePermissions";
});
```

## 使用

### 定义权限

```csharp
public class OrdersPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetOrAddGroup("Orders", "订单管理");
        var orders = group.AddPermission("Orders", MultiTenancySides.Both, "订单管理");
        orders.AddChild("Orders.Read", "查看订单");
        orders.AddChild("Orders.Write", "编辑订单");

        var system = context.GetOrAddGroup("System", "系统管理");
        system.AddPermission("System.Tenants", MultiTenancySides.Host, "租户管理");
    }
}
```

每个权限必须属于权限组，名称全局唯一。**多租户侧别必填**，没有默认值：省略会静默落到 `Both`（"租户管理员也拿得到"），而宿主全局资源落成 `Both` 就是跨租户越权，且只在真的建了租户之后才暴露。判据是这条权限背后的数据带不带租户维度。子权限不声明时继承父权限；宿主侧权限在租户上下文中始终拒绝。

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

初始化时的首次授予（主体从未写过授予才写入，之后按普通主体管理）：

```csharp
await grantSeeder.SeedAllAsync(PermissionGrantProviderNames.Role, adminRoleId, MultiTenancySides.Tenant, ct);
```

要审计管理界面上的授予变更，处理 `PermissionGrantsReplacedEvent`（带主体显示名快照与新版本）。

## 接口参考

| 类型 | 用途 |
| --- | --- |
| `IPermissionDefinitionProvider` | 定义权限组与权限树 |
| `IPermissionDefinitionManager` | 查询定义、祖先、子孙与有效状态；`IsAvailableOn(name, side)` 为检查器与管理界面共用的侧别判据 |
| `IPermissionSubjectProvider` | 提供当前用户、角色与超级管理员状态 |
| `IPermissionChecker` | 单个或批量检查权限 |
| `IPermissionGrantStore` | 读取主体授予、有效权限与版本 |
| `IPermissionGrantManager` | 授予、撤销、全量替换和永久删除主体的授予清理 |
| `PermissionSubject` | 当前用户 Id、角色 Id 和超级管理员标记 |
| `PermissionGrantSet` | 单个主体的授予集合与版本 |
| `SubjectPermissionGrants` | 用户与所有角色授予的快照及 `VersionToken` |
| `IPermissionManagementService` | 管理用例：`GetCurrentAsync`、`GetDefinitionsAsync`、`GetGrantsAsync`、`ReplaceGrantsAsync` |
| `IPermissionSubjectDirectory` | 宿主实现：按授予对象确认主体存在并给出显示名 |
| `IPermissionGrantSeeder.SeedAllAsync(providerName, providerKey, side, ct)` | 从未写过授予时授予某侧别上的全部可用权限 |
| `PermissionGrantsReplacedEvent` | 管理用例整体替换授予后发布 |
| `PermissionErrorCodes` | 组件错误码，默认中英译文随包分发 |
| `MapPermissionManagement(configure)` | AspNetCore 包：`GET /current`、`GET /definitions`、按主体类型的 `GET/PUT /grants/{roles\|users}/{providerKey}`；`CurrentPolicy`、`DefinitionsPolicy` 必填（组件不套宿主默认策略），`GrantPolicies` 决定开放哪些主体类型 |

| 异常 | HTTP | 含义 |
| --- | --- | --- |
| `UndefinedPermissionException` | 400 | 尝试授予未定义或未启用的权限（`Permission:UndefinedPermission`） |
| `PermissionGrantConcurrencyException` | 409 | 保存基于过期版本（`Permission:ConcurrencyConflict`） |
| `UnstableGrantSnapshotException` | 503 | 连续重读仍无法取得一致快照 |

## 实现行为

- `PermissionGrantRecord` 与 `AuthorizationVersionRecord` 按当前租户过滤。宿主行与租户行使用分离的部分唯一索引，避免可空 `TenantId` 使宿主授予失去唯一性。
- `ReplaceGrantsAsync` 在一次 `SaveChangesAsync` 中原子替换，只在内容变化时递增版本。版本同时是 EF Core 并发令牌。
- 用户与角色授予的读取为常数数量的数据库往返，不按角色或权限逐条查询。
- `SubjectPermissionGrants.VersionToken` 组合用户版本与按 key 排序的角色版本，可用于判断客户端权限缓存是否过期。
- **管理用例与检查器同一判据**：定义树、授予状态与当前有效权限都只含当前侧别上可用（已定义、自身与祖先启用、侧别匹配）的权限；子节点同样过滤，整组都不可用时不下发空分组。超级管理员的当前权限是全部可用权限，版本标记固定为 `super-admin`。
- 管理用例不查权限（交给端点策略）；主体不存在返回 404（`Permission:SubjectNotFound`），当前身份不是权限主体返回 401（`Permission:SubjectUnavailable`），单次替换超过 500 项返回 422。显示名以定义里的 `DisplayName` 为词条键查 `LocalizationResource`，查不到回落到权限名。
- 首次授予带期望版本 0 写入，并发的第二次视为已播种。

## 注意事项

- `IPermissionSubjectProvider` 没有默认实现，未注册时 DI 解析直接失败。业务实现必须提供角色 Id、从可信来源判定 `IsSuperAdmin`，并在请求期反映账号禁用或锁定状态。
- `IsSuperAdmin` 的来源必须可信；它会在定义、启用状态与多租户侧别校验通过后跳过 Store。
- 所有授予写入都经 `IPermissionGrantManager`，不直接写 DbContext，否则会绕过定义校验、祖先补齐与版本。
- 主体永久删除时调用 `RemoveProviderAsync`；软删除不清理授予。
- 权限管理 UI 从定义树端点构造，不在前端复制权限列表。
- `IPermissionSubjectDirectory` 没有默认实现：映射管理端点的宿主必须注册，否则解析管理用例失败。它的 Key 格式判断也归宿主（格式不对返回 `null` 即 404）。

## 相关

- [多租户](./multi-tenancy.md)
- [资源实例授权](./authorization-resource.md)
- [数据范围](./authorization-data-scope.md)
- [审计](./auditing.md)
