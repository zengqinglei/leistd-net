# 权限授权

权限授权用于在**声明式定义权限**的基础上，于运行时判断"当前用户是否有权执行某操作"。与基于角色的粗粒度控制不同，权限授权把每个可授权的操作抽象为一个具名权限（如 `Orders.Read`），支持按用户、按角色分别授予，并可与 ASP.NET Core 的策略（Policy）管道无缝集成，让 `[Authorize(Policy = "权限名")]` 直接生效。典型场景包括：后台管理系统的按钮级/接口级权限控制、多租户场景下超级管理员绕过检查、权限授予需要持久化并支持动态增删。

Leistd 通过 `IPermissionChecker` 提供统一的权限检查入口，业务代码只需注入接口做判断；权限的"有哪些"由 `IPermissionDefinitionProvider` 声明式定义，权限的"谁被授予了"由 `IPermissionGrantStore`/`IPermissionGrantManager` 负责存取。`Leistd.Authorization.AspNetCore` 把权限检查接入 ASP.NET Core 授权策略管道，`Leistd.Authorization.EntityFrameworkCore` 提供基于 EF Core 的授予持久化实现。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 需要细粒度、按操作划分的权限控制（而非仅角色判断） | 完整引入 Core + AspNetCore（+ EntityFrameworkCore） |
| 需要 `[Authorize(Policy = "权限名")]` 这类声明式接口/控制器权限校验 | `Leistd.Authorization.AspNetCore` |
| 权限授予需要持久化到数据库，支持运行时增删 | `Leistd.Authorization.EntityFrameworkCore` |
| 仅编写业务代码（定义权限 / 检查权限），不关心存储实现 | 只引用 `Leistd.Authorization.Core` 中的接口 |

## 安装

```bash
# 抽象 + 默认检查器（权限定义、IPermissionChecker）
dotnet add package Leistd.Authorization.Core

# ASP.NET Core 策略集成（[Authorize(Policy = "权限名")] 动态生效）
dotnet add package Leistd.Authorization.AspNetCore

# EF Core 授予存储（权限授予持久化到数据库）
dotnet add package Leistd.Authorization.EntityFrameworkCore
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

三层各自的 DI 扩展方法：

```csharp
// Core：注册 IPermissionDefinitionManager 与 IPermissionChecker（默认实现）
builder.Services.AddPermissionAuthorizationCore();

// AspNetCore：接入策略管道（内部已调用 AddPermissionAuthorizationCore）
builder.Services.AddPermissionAuthorization();

// EntityFrameworkCore：基于指定 DbContext 的授予存储（内部已调用 AddPermissionAuthorizationCore）
builder.Services.AddAuthorizationEfCore<AppDbContext>();
```

- `AddPermissionAuthorizationCore`：以 **Singleton** 注册 `IPermissionDefinitionManager`（`PermissionDefinitionManager`），以 **Scoped** 注册 `IPermissionChecker`（`DefaultPermissionChecker`）。Scoped 使一次请求内的多次权限检查共享同一份主体与授予快照。
- `AddPermissionAuthorization`：在调用 `AddPermissionAuthorizationCore` 的基础上，以 **Singleton** 注册 `IAuthorizationPolicyProvider`（`PermissionPolicyProvider`），以 **Transient** 注册 `IAuthorizationHandler`（`PermissionAuthorizationHandler`）。依赖调用方已注册 `IPermissionChecker` 与 `IPermissionDefinitionManager`，并已调用 ASP.NET Core 自带的 `AddAuthorization()`。
- `AddAuthorizationEfCore<TDbContext>`：在调用 `AddPermissionAuthorizationCore` 的基础上注册 `IPermissionGrantStore`（`EfCorePermissionGrantStore<TDbContext>`）与 `IPermissionGrantManager`（`EfCorePermissionGrantManager<TDbContext>`）。

EF Core 实体映射需在 `DbContext.OnModelCreating` 中显式应用：

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigureAuthorization();
}
```

业务代码还需自行实现并注册 `IPermissionSubjectProvider`（提供"当前用户是谁、属于哪些角色、是否超管"），`IPermissionChecker` 依赖它获取检查主体；本组件不提供默认实现。

## 使用

**第一步：定义权限**——实现 `IPermissionDefinitionProvider` 并注册到 DI（供 `PermissionDefinitionManager` 加载）：

```csharp
public class OrdersPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetOrAddGroup("Orders", "订单管理");

        // 每个权限都必须归属于某个组：权限管理界面按组分区渲染，游离权限无法被界面表达。
        var orders = group.AddPermission("Orders", "订单管理");
        orders.AddChild("Orders.Read", "查看订单");
        orders.AddChild("Orders.Write", "编辑订单");

        // 多租户宿主可声明侧别（默认 Both）：组的侧别是组内权限的继承默认值，
        // 子权限继承父权限；宿主侧权限在租户上下文内对任何主体（含超管）都不可用。
        var system = context.GetOrAddGroup("System", "系统管理", MultiTenancySides.Host);
        system.AddPermission("System.Tenants", "租户管理");   // 继承组的 Host 侧别
    }
}

builder.Services.AddSingleton<IPermissionDefinitionProvider, OrdersPermissionDefinitionProvider>();
```

**第二步：检查权限**——注入 `IPermissionChecker` 在业务代码中判断：

```csharp
public class OrderService(IPermissionChecker permissionChecker)
{
    public async Task<bool> CanReadAsync()
    {
        return await permissionChecker.IsGrantedAsync("Orders.Read");
    }
}
```

**第三步：授予与撤销**——写入统一走 `IPermissionGrantManager`，它会在写入时归一化权限树：

```csharp
// 授予子权限会自动补齐其全部祖先，运行时因此可以保持扁平查找。
await grantManager.GrantAsync("Orders.Write", PermissionGrantProviderNames.Role, roleId);

// 撤销父权限会级联清理其全部子孙，不会留下"子有父无"的悬空授予。
await grantManager.RevokeAsync("Orders", PermissionGrantProviderNames.Role, roleId);

// 权限管理界面保存：一次请求替换该主体的全部授予，并用版本号做乐观并发。
var current = await grantStore.GetGrantsAsync(PermissionGrantProviderNames.Role, roleId);
try
{
    await grantManager.ReplaceGrantsAsync(
        PermissionGrantProviderNames.Role,
        roleId,
        // 授予是纯加法：出现在列表里即授予，不在即不授予，没有"拒绝"这一态。
        ["Orders", "Orders.Read"],
        expectedRevision: current.Revision);
}
catch (PermissionGrantConcurrencyException)
{
    // 另一位管理员抢先保存：提示重新加载，不要静默覆盖对方的修改。
    throw new ConflictException("The permissions were changed by someone else.");
}
```

**第四步：驱动权限管理界面**——权限树完全由定义生成，前端不硬编码任何权限列表：

```csharp
foreach (var group in definitionManager.GetGroups())
{
    foreach (var permission in group.Permissions.Where(p => definitionManager.IsEffectivelyEnabled(p.Name)))
    {
        // permission.Children 递归即得到该组的完整权限树。
    }
}

// 下发给前端的当前用户有效权限：一次查询取回，用户与各角色的授予取并集。
var grants = await grantStore.GetGrantsForSubjectAsync(subject.UserId, subject.RoleIds);
var effective = grants.GetGrantedNames();

// grants.Revision 变化即表示权限已被改动，客户端据此判断本地缓存是否过期。
```

**第五步：控制器/接口声明式校验**——引入 `Leistd.Authorization.AspNetCore` 后，`[Authorize(Policy = "权限名")]` 直接生效。

策略名可以用 `|` 连接多个权限表示「任一满足」：

```csharp
// 「能配置某类主体的权限」蕴含「能读权限目录」。把读取做成一个可被单独扣掉的权限，
// 只会制造一个永远无用的状态——有 ManagePermissions 却打不开权限配置界面。
[Authorize(Policy = "App.Permissions|App.Roles.ManagePermissions|App.Users.ManagePermissions")]
public Task<IReadOnlyList<PermissionGroupDto>> GetDefinitionsAsync() => ...;
```

仅当 `|` 分隔后的每一段都是已定义权限时才按权限策略解析，否则整个策略名交回默认策略提供器，因此不会误判恰好含 `|` 的自定义策略名。

```csharp
// 权限名作为策略名：命中权限才放行，否则返回 403
[Authorize(Policy = "Orders.Read")]
[HttpGet("orders")]
public Task<IReadOnlyList<OrderDto>> GetOrders([FromQuery] OrderQuery query)
    => orderService.GetListAsync(query);
```

## 接口参考

`Leistd.Authorization.Core` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `IPermissionChecker` | 权限检查统一入口 |
| `IPermissionChecker.IsGrantedAsync(name, ct)` | 检查当前用户是否拥有指定权限，返回 `bool` |
| `IPermissionChecker.IsGrantedAsync(names, ct)` | 批量检查多个权限，返回 `MultiplePermissionGrantResult` |
| `MultiplePermissionGrantResult` | 批量检查结果，含 `Results` 字典、`AllGranted`（全部授予）、`AnyGranted`（至少一个授予） |
| `IPermissionDefinitionProvider` | 权限定义提供者，业务项目实现 `Define` 声明权限 |
| `IPermissionDefinitionContext` | 定义期上下文：`GetOrAddGroup`（可选 `side` 声明组侧别）、`GetPermissionOrNull`。**每个权限都必须归属于某个组**，权限管理界面按组分区渲染 |
| `IPermissionGroupDefinition` | 权限组：`Name`、`DisplayName`、`Side`（多租户侧别，组内权限的继承默认值）、`Permissions`、`AddPermission`、`GetPermissionOrNull` |
| `IPermissionDefinition` | 单个权限定义：`Name`、`DisplayName`、`Parent`、`Children`、`IsEnabled`、`Side`（多租户侧别，未显式指定时继承组/父权限）、`AddChild` |
| `IPermissionGrantManager.RemoveProviderAsync(providerName, providerKey, ct)` | 主体**永久删除**后清理其全部授予与授权版本，返回删除行数，幂等。与"替换为空集合"不同——后者是撤销语义，会保留并递增版本 |
| `UnstableGrantSnapshotException` | 稳定读取重试耗尽：取不到一致快照。属**读取失败**，与 `PermissionGrantConcurrencyException`（保存冲突，映射 409）不是一回事，调用方重试即可 |
| `IPermissionDefinitionManager` | 权限定义查询：`GetOrNull(name)`、`GetAll()`、`GetGroups()`、`IsEffectivelyEnabled(name)`、`GetAncestorNames(name)`、`GetDescendantNames(name)` |
| `IPermissionSubjectProvider` | 当前权限检查主体提供器，业务项目需自行实现 |
| `IPermissionSubjectProvider.GetCurrentSubjectAsync(ct)` | 获取当前主体，未登录/无法识别时返回 `null` |
| `PermissionSubject` | 检查主体记录：`UserId`、`RoleIds`、`IsSuperAdmin` |
| `PermissionGrantSet` | 单个主体的全部授予及其并发版本：`ProviderName`、`ProviderKey`、`PermissionNames`、`Revision`；`Empty(providerName, providerKey)` 构造空集合 |
| `SubjectPermissionGrants` | 检查主体的全部授予：`UserGrants`、`RoleGrants`、`Revision`、`GetGrantedNames()`（各来源取并集） |
| `IPermissionGrantStore.GetGrantsAsync(providerName, providerKey, ct)` | 取单个主体的全部授予与版本 |
| `IPermissionGrantStore.GetGrantsAsync(providerName, providerKeys, ct)` | 批量取同类型多个主体的授予与版本，返回顺序与入参一致；列表页用它避免按行的 N+1 |
| `IPermissionGrantStore.GetGrantsForSubjectAsync(userId, roleIds, ct)` | 一次取回主体的用户直授加全部角色授予 |
| `IPermissionGrantManager.GrantAsync(name, providerName, providerKey, ct)` | 授予单个权限 |
| `IPermissionGrantManager.RevokeAsync(name, providerName, providerKey, ct)` | 撤销单个权限并级联撤销其全部子孙 |
| `IPermissionGrantManager.ReplaceGrantsAsync(providerName, providerKey, permissionNames, expectedRevision, ct)` | 原子替换某主体的全部授予，返回新版本号 |
| `PermissionGrantConcurrencyException` | 乐观并发冲突：`ProviderName`、`ProviderKey`、`ExpectedRevision`、`ActualRevision`；宿主应映射为 HTTP 409 |
| `UndefinedPermissionException` | 试图授予未定义或已禁用的权限：`PermissionNames`；宿主应映射为 HTTP 400 |
| `PermissionPolicyNames.AnyOfSeparator` | 多权限策略名的分隔符 `\|`；权限名自身不得包含该字符 |
| `PermissionGrantProviderNames` | 授予对象类型常量：`User`、`Role` |

`Leistd.Authorization.AspNetCore` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `PermissionPolicyProvider : IAuthorizationPolicyProvider` | 把权限名当作策略名，动态构建携带 `PermissionRequirement` 的策略 |
| `PermissionAuthorizationHandler` | 将 `PermissionRequirement` 委托给 `IPermissionChecker` 校验 |
| `PermissionRequirement` | 授权需求，含 `PermissionNames`；多个权限按「任一满足」处理 |

`Leistd.Authorization.EntityFrameworkCore` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `PermissionGrantRecord` | 权限授予持久化实体：`Id`（Guid v7）、`TenantId`（可空，租户分区）、`PermissionName`、`ProviderName`、`ProviderKey`；实现 `ICreationAuditedObject`（审计拦截器填充）与 `IMultiTenant`（多租户落值拦截器填充 `TenantId`，全局过滤器使授予按租户分区）。**行的存在即授予**，没有表示效果的列 |
| `AuthorizationRevisionRecord` | 主体授权版本：`Id`、`TenantId`（可空）、`ProviderName`、`ProviderKey`、`Version`；实现 `IModificationAuditedObject` 与 `IMultiTenant`，版本随授予按租户独立演进 |
| `PermissionGrantRecordConfiguration` / `AuthorizationRevisionRecordConfiguration` | 两个实体的 EF Core 配置 |
| `EfCorePermissionGrantStore<TDbContext>` | `IPermissionGrantStore` 的 EF Core 实现 |
| `EfCorePermissionGrantManager<TDbContext>` | `IPermissionGrantManager` 的 EF Core 实现 |

## 实现行为

### PermissionPolicyProvider（动态策略生成）

- `GetPolicyAsync(policyName)` **先问 `DefaultAuthorizationPolicyProvider`**：宿主显式注册的同名策略优先，动态策略不会把它盖掉（例如宿主为某权限名注册了"权限 + MFA"的更严格策略）。
- 没有同名注册策略时，按 `PermissionPolicyNames.AnyOfSeparator`（`|`）拆分策略名：**每一段都是已定义权限**时才构建携带 `PermissionRequirement(names)` 的策略，多个权限按「任一满足」判定；只要有一段不是权限名（含空段），就返回 `null`，由 ASP.NET Core 以"策略不存在"在请求期报错——写错的策略名大声失败，不会因为"其中一段命中"被悄悄放行。
- 因此普通 `[Authorize]`、`[Authorize(Roles=...)]` 及显式注册的命名策略（如 "SuperAdmin"）不受影响。
- `GetDefaultPolicyAsync` / `GetFallbackPolicyAsync` 均直接委托给内部的 `DefaultAuthorizationPolicyProvider`。

### DefaultPermissionChecker（默认检查流程）

判定顺序：

1. **权限未定义或未启用一律拒绝**——`IsEffectivelyEnabled` 要求该权限自身与其全部祖先都处于启用状态，因此拼错的权限名、数据库残留的权限、被禁用分支下的权限都默认拒绝，超级管理员也不例外。
2. **侧别与当前多租户上下文不匹配一律拒绝**——宿主侧权限在租户上下文内对任何主体（含超管）不可用，反之亦然；当前侧别读自 `ICurrentTenant`（可选依赖，未注册的非多租户宿主视为 Host 侧，仅租户侧专属权限被拒）。
3. 通过 `IPermissionSubjectProvider` 取不到当前主体（未登录）返回 `false`。
4. `PermissionSubject.IsSuperAdmin` 为 `true` 时直接返回 `true`，**不读取授予记录**。
5. 否则查授予集合：用户与其全部角色的授予取并集，命中即允许，否则拒绝。

`DefaultPermissionChecker` 以 Scoped 注册：主体解析与授予读取在同一作用域（通常是一次 HTTP 请求）内**只发生一次**，之后同一作用域中的任意多次检查都是内存字典查找，不再回访数据库。`IsGrantedAsync(names, ct)` 对传入名称按 `StringComparer.Ordinal` 去重并过滤空白。

### 写时归一化（EfCorePermissionGrantManager）

所有写入都经过同一条流水线，因此单条授予、批量替换与种子数据得到一致的结果：

1. **校验**：权限必须已定义且启用，否则抛 `UndefinedPermissionException`，不会把无效权限写进存储。这是该规则的**唯一执行点**（管理器是写入方，覆盖应用服务、种子数据、后台任务等全部调用路径），调用方不必也不应重复判断，只需按需把它翻译成对外契约（如 HTTP 400）。
2. **向上补齐**：被授予权限的全部祖先一并授予。

这使运行时的权限检查可以保持扁平字典查找，不需要回溯定义树。唯一残留是绕过 Manager 直接写 `DbContext` 的数据不会被归一化。


### EF Core 存储行为

- `PermissionGrantRecord` 唯一性由**宿主行与租户行成对的带过滤唯一索引**保证（`TenantId` 为 NULL 的行用 `(PermissionName, ProviderName, ProviderKey)` 加 `IS NULL` 过滤，租户行用带 `TenantId` 前缀的索引加 `IS NOT NULL` 过滤）：一行即一次授予，不存在同一主体对同一权限出现两行的可能。可空列直接进唯一索引时 NULL 互不相等，宿主行会失去唯一性兜底，这正是拆成两个过滤索引的原因。授予与版本经全局查询过滤器按租户分区，Store 查询天然只见当前租户。**升级到本版本需要一次 EF 迁移**（新增 `TenantId` 列与索引重建；存量行 NULL 即宿主语义，行为不变）。
- `ReplaceGrantsAsync` 以一次 `SaveChangesAsync` 提交，是单事务操作；目标集合与现有记录做差异比对，只在真正发生变化时递增版本。
- `expectedRevision` 与存储中的当前版本不一致时抛 `PermissionGrantConcurrencyException`，且**不做任何写入**；传 `null` 表示跳过并发校验。
- `AuthorizationRevisionRecord.Version` 是**并发令牌**：EF 在 UPDATE 上带 `WHERE Version = @original`。仅靠「先读版本再内存比较」挡不住两个事务同时读到同一版本的情况，令牌把这段窗口交给数据库收口，落败方同样得到 `PermissionGrantConcurrencyException`。
- 每个读取方法的数据库往返次数是常数（版本、授予、版本各一次），与被检查的权限数量、主体所属角色数量和批量查询的主体数量都无关，因此不存在 N+1。
- **版本读两遍是必需的**：授予与版本若各查一次，中间夹进一次写入就会拼出"旧集合 + 新版本"——拿它去保存会被判为无冲突，乐观并发形同虚设；下发给前端则让旧权限带着新版本被缓存，此后永不刷新。两次版本一致才返回，不一致就重读；连续若干次仍不稳定时抛 `UnstableGrantSnapshotException`（读取失败，调用方重试即可），而不是复用保存冲突异常。
- `SubjectPermissionGrants.Revision` 由用户授予版本与各角色授予版本（按角色 Key 排序）拼接而成；角色成员变更会改变参与拼接的角色集合，因此无需为成员变更额外扇出写入即可反映在版本中。
- 只读查询均使用 `AsNoTracking()`。
- 字段长度约束：`PermissionName` 256、`ProviderName` 32、`ProviderKey` 128、`CreatorId` 64。

## 配置项 / Options

当前无配置项：三个 DI 扩展方法（`AddPermissionAuthorizationCore`、`AddPermissionAuthorization`、`AddAuthorizationEfCore<TDbContext>`）均无参数，也未暴露 Options 类。

## 注意事项

- `IPermissionSubjectProvider` 没有默认实现，必须由业务项目提供（通常基于 `ICurrentUser` 等安全组件），否则 `IPermissionChecker` 恒定返回"未授予"。
- `PermissionSubject.IsSuperAdmin` 为 `true` 时会**跳过存储层查询**直接判定为已授予，业务项目需自行保证该标志的正确来源（约定的 claim 类型见 `Leistd.Security.Claims.CustomClaimTypes.IsSuperAdmin`）。
- `AddPermissionAuthorization` 依赖调用方已执行 ASP.NET Core 原生的 `AddAuthorization()`；`PermissionPolicyProvider` 只在策略名命中已定义权限时接管，其余策略名回退到默认提供器，不会破坏既有的角色/命名策略。
- `EfCorePermissionGrantManager`/`EfCorePermissionGrantStore` 依赖调用方在 `OnModelCreating` 中执行 `modelBuilder.ConfigureAuthorization()`，否则 `PermissionGrantRecord` 不会被正确映射。
- 权限定义（`IPermissionDefinitionProvider`）在 `PermissionDefinitionManager` 构造时一次性加载，随后祖先链、子孙集合与有效启用状态都被**预计算并缓存**，因此运行期的权限检查与授予归一化都是字典查找。运行期新增/修改权限定义需重启进程；权限**授予**（谁拥有权限）则可随时通过 `IPermissionGrantManager` 动态增删，无需重启。
- **权限名全局唯一**：同名权限在任意组或任意层级重复注册，会在启动阶段抛 `InvalidOperationException` 而不是静默覆盖。
- **授予是纯加法，不存在"拒绝"**。有效权限是用户授予与各角色授予的并集，因此"他为什么有这个权限"只需找出哪个来源给了它，不必遍历全部来源确认没人拒绝。要收回某人的能力，调整其角色构成，而不是在权限位上做减法。资源实例授权是另一回事，那一层有自己的 `ResourceGrantEffect`，见[资源实例授权](./authorization-resource.md)。
- 本组件只负责"能否执行这类动作"。"能操作哪一条"见[资源实例授权](./authorization-resource.md)，"列表里有哪些条"见[数据范围](./authorization-data-scope.md)。

## 可执行参考实现

本文档的示例代码不是凭空写的：`framework/tests/Leistd.Authorization.Pipeline.Tests` 是一个真实的
ASP.NET Core 宿主（真实 Web 宿主 + TestServer + Sqlite + 真实 DI 装配），把三层授权串起来跑通了
设计文档中的标准执行链，并覆盖了以下负向场景：

- 缺功能权限时在第一层就被拦下，不会走到数据范围；
- 范围外的详情返回 404 而非 403，不泄漏资源存在性；
- 列表与详情共用同一个可见查询入口，同一个 Read 操作不会给出两个答案；
- 超管在集合与单实例上口径一致，但领域规则的拒绝对超管同样有效；
- 读取范围是整个组织、更新范围只有本人 —— 能看不等于能改；
- 领域规则的拒绝优先于所有者身份与 ACL 允许；
- 批量操作整体拒绝，而不是静默跳过越权项；
- 列表、总数与导出共用同一个范围入口，三者始终一致；
- ACL 以子查询合并进集合查询，显式拒绝会把资源从结果里移除。

改动本组件的公共行为时，请连同该工程一起更新——它是这些语义唯一的可执行事实来源。

## 相关

- [多租户](./multi-tenancy.md)
- [组件总览](./README.md)
- [资源实例授权](./authorization-resource.md)
- [数据范围](./authorization-data-scope.md)
- [依赖注入](./dependency-injection.md)
- [审计](./auditing.md)
