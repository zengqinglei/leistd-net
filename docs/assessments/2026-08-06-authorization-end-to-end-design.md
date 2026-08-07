# Leistd 端到端授权体系评估与目标设计

> 日期：2026-08-06
> 基线分支：`feat/frontend-spartan-migration`
> 基线提交：`bbcc61878dd08f962b853e583f560dec8e23fd25`
> 范围：`Leistd.Security.*`、`Leistd.Authorization.*`、DDD 数据过滤能力、Fullstack Template 后端/前端/条件裁剪

## 1. 结论先行

Leistd 不应把 RBAC、数据范围 ABAC、资源实例授权合并为一个万能的“权限表”，也不应要求所有项目同时启用三者。最佳方案是建立三个边界清晰、可独立裁剪、执行时可组合的授权层：

1. **功能权限 RBAC** 回答“当前主体能否执行某类动作”，例如 `App.Users.Update`。这是所有后台管理项目的基础层。
2. **数据范围策略（集合级 ABAC）** 回答“列表、统计、导出和批量操作能看到哪些候选数据”，必须能翻译成数据库查询谓词。
3. **资源实例授权（规则 + ACL）** 回答“对这个已经定位的订单、文档或项目实例，能否执行当前操作”，用于所有者、成员关系、资源状态和临时分享等精细判断。

推荐的默认产品策略是：

- Framework 提供完整但可选的三层原语；Template 默认完成用户、角色、功能权限的管理闭环。
- 数据范围与资源实例授权作为独立可选 NuGet 包，由业务项目按需引用，不进模板参数，避免简单项目为未使用能力付出模型和 UI 成本。
- 不在通用 Framework 中内置“部门”“组织树”“本人/本部门”等业务概念；Framework 只定义策略、组合语义和查询扩展点，具体范围由业务项目注册。
- 不建设通用字符串表达式/XACML 规则引擎。策略优先使用类型安全的 C# Handler/Provider；这是比“完整 ABAC DSL”更适合 Leistd 当前体量的复杂度控制方式。
- 服务端始终是安全边界；前端路由、菜单和按钮裁剪只负责体验，不能替代 API、查询和资源实例检查。

架构总览见 [可编辑授权体系架构图](./2026-08-06-authorization-architecture.drawio) 和 [PNG 预览](./2026-08-06-authorization-architecture.drawio.png)。

## 2. 三类授权问题必须分开

| 层次 | 核心问题 | 典型输入 | 典型输出 | 最佳执行位置 |
| --- | --- | --- | --- | --- |
| 功能权限 RBAC | 能否执行这类动作？ | User、Role、Permission | Grant / Prohibit / Undefined | Controller Policy、Application Service |
| 数据范围 ABAC | 哪些数据可以成为候选集？ | 主体属性、资源属性、上下文、Scope Policy | `IQueryable` / Expression 谓词 | 查询构造阶段、数据库执行前 |
| 资源实例授权 | 能否操作这个具体实例？ | 主体、操作、已加载资源、关系、ACL | Allow / Deny | 资源加载后、领域状态变更前 |

一个完整的更新请求应执行：

```text
认证
  -> 功能权限检查（App.Orders.Update）
  -> 通过数据范围定位资源（避免水平越权）
  -> 对已加载实例执行资源授权（所有者/成员/状态/ACL）
  -> 领域不变量
  -> 持久化与审计
```

列表请求不应先加载全量数据再逐条调用资源授权；否则分页总数、排序、导出和性能都会错误。列表必须先把允许范围转换成 SQL 谓词。

## 3. 官方与业界设计基线

### 3.1 ABP 的启示

ABP 将权限定义、权限值提供者、权限管理模块和 Identity 分开。其内置用户、角色、客户端 Provider 可以共同参与判断，并使用 `Granted / Prohibited / Undefined` 三态，`Prohibited` 优先。最新版还把资源权限作为独立能力，提供用户/角色资源权限 Provider、资源权限管理器和可复用资源权限对话框。

对 Leistd 的直接启示：

- 保留“定义是什么”和“授予给谁”的分离，不把 Permission 变成 Role 实体的字段。
- Provider 应可扩展到 User、Role、Client，并具有明确的三态组合规则。
- 全局权限授予与资源实例授予使用不同存储和管理 API，避免大量 nullable 字段和含混索引。
- 权限管理 UI 应由元数据驱动，而不是前端硬编码权限树。

参考：

- [ABP Authorization](https://abp.io/docs/latest/framework/fundamentals/authorization)
- [ABP Permission Management](https://abp.io/docs/latest/modules/permission-management)
- [ABP Resource-Based Authorization](https://abp.io/docs/latest/framework/fundamentals/authorization/resource-based-authorization)
- [ABP Data Filtering](https://abp.io/docs/latest/framework/infrastructure/data-filtering)

### 3.2 Microsoft 的启示

ASP.NET Core 将 Role/Claim 都收敛到 Policy、Requirement、Handler。一个 Policy 的多个 Requirement 是 AND；同一 Requirement 的多个 Handler 可以表达不同成功路径。资源实例在 Attribute 执行时通常尚未加载，因此必须先加载资源，再显式调用授权服务。

EF Core 全局查询过滤适合软删除、租户隔离等稳定的强制边界，但业务数据范围通常与操作、角色和资源类型有关，不应全部塞进全局过滤器。

对 Leistd 的直接启示：

- 继续使用动态 Policy 作为功能权限与 ASP.NET Core 的桥接层。
- 资源实例授权采用命令式检查，不试图用 `[Authorize]` 在资源加载前完成。
- 数据范围单独形成可组合查询策略；租户隔离与软删除继续作为不可绕过或受控绕过的全局边界。

参考：

- [Policy-based authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0)
- [Role-based authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-10.0)
- [Resource-based authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0)
- [EF Core global query filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)

### 3.3 OWASP / NIST 的启示

OWASP 明确要求最小权限、默认拒绝、每次请求检查、服务端执行和水平越权防护，并指出复杂应用通常需要 ABAC/ReBAC 补足 RBAC 的对象级能力。NIST RBAC 仍适合稳定的岗位到功能映射，但不能单独表达资源所有权、组织关系和环境条件。

参考：

- [OWASP Authorization Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html)
- [NIST Role Based Access Control](https://csrc.nist.gov/projects/role-based-access-control)

## 4. 当前已经具备的能力

### 4.1 `Leistd.Security.*`

| 能力 | 现状 | 评价 |
| --- | --- | --- |
| 当前用户 | `ICurrentUser` 暴露 Id、用户名、角色和 Claims | 可直接作为授权主体上下文 |
| 当前客户端 | `ICurrentClient` 暴露 ClientId、ApiKeyId、CreatorId | 为 M2M/客户端权限预留了基础 |
| 主体切换 | `ICurrentPrincipalAccessor.Change` 使用 AsyncLocal 支持嵌套作用域 | 适合后台任务和测试 |
| ASP.NET Core 适配 | `HttpContextCurrentPrincipalAccessor` + `AddSecurity()` | 分层正确，Core 无 ASP.NET Core 依赖 |
| 超管 Claim | `CustomClaimTypes.IsSuperAdmin` | 已形成统一命名，但来源可信性由业务保证 |

源码依据：[`CurrentUser`](../../framework/components/security/Leistd.Security.Core/Users/CurrentUser.cs)、[`CurrentPrincipalAccessor`](../../framework/components/security/Leistd.Security.Core/Claims/CurrentPrincipalAccessor.cs)、[`Security DI`](../../framework/components/security/Leistd.Security.AspNetCore/DependencyInjection.cs)。

### 4.2 `Leistd.Authorization.*`

| 能力 | 现状 | 评价 |
| --- | --- | --- |
| 权限定义 | Group、Permission、Parent/Children、DisplayName、IsEnabled | 已有可驱动管理 UI 的树形骨架 |
| 定义扩展 | `IPermissionDefinitionProvider` | 业务模块可声明自身权限 |
| 主体适配 | `IPermissionSubjectProvider` | Framework 未绑定具体 Identity 模型，边界正确 |
| 授予主体 | User、Role | 已覆盖最常用 RBAC 路径 |
| 授予存储 | `PermissionGrantRecord` + 唯一索引 + 审计字段 | 基础持久化完整 |
| 批量检查 | `IsGrantedAsync(string[])` 与一次数据库批量查询 | 避免逐权限 N+1 |
| 超管旁路 | `PermissionSubject.IsSuperAdmin` | 已实现并有单元测试 |
| ASP.NET Core | 动态 `IAuthorizationPolicyProvider` + Handler | `[Authorize(Policy = permission)]` 已闭环 |
| 测试 | Checker、EF Store/Manager、模板用户/角色授予集成测试 | 主路径有证据，不是仅有文档 |

源码依据：[`DefaultPermissionChecker`](../../framework/components/authorization/Leistd.Authorization.Core/DefaultPermissionChecker.cs)、[`PermissionPolicyProvider`](../../framework/components/authorization/Leistd.Authorization.AspNetCore/PermissionPolicyProvider.cs)、[`EfCorePermissionGrantStore`](../../framework/components/authorization/Leistd.Authorization.EntityFrameworkCore/Stores/EfCorePermissionGrantStore.cs)、[`Authorization tests`](../../framework/tests/Leistd.Authorization.Tests/DefaultPermissionCheckerTests.cs)。

### 4.3 DDD 组件

| 能力 | 现状 | 评价 |
| --- | --- | --- |
| 数据过滤开关 | `IDataFilter<T>` 支持 AsyncLocal 嵌套 Enable/Disable | 可复用机制存在 |
| 全局过滤 | `BaseDbContext` 对 `ISoftDelete` 应用全局过滤 | 当前仅解决软删除 |
| 通用谓词应用 | `ApplyGlobalFilters<TInterface>` | 可作为租户等稳定边界的基础 |
| 查询扩展 | Repository 暴露 `IQueryable<TEntity>` | 能承载集合级授权谓词 |

当前没有 Tenant、Organization、Owner、DataScope 定义，也没有“所有查询自动应用业务授权范围”的契约。模板中的 [`UserScopeSpecifications`](../../template/backend/src/CompanyName.ProjectName.Domain/Users/Specifications/UserScopeSpecifications.cs) 只是未被消费的 Admin/本人辅助函数，不能视为数据范围能力。

### 4.4 Template 后端

已经存在：

- `User`、`Role`、`UserRole` 领域实体和 EF 映射。
- 用户分页、详情、创建、更新、启停、重置密码、软删除。
- 用户角色分配、默认 `Member` 角色和 `Admin` 角色初始化。
- 用户/角色功能权限定义，以及 Template 对 `IPermissionSubjectProvider` 的适配。
- Controller 上按权限名执行动态 Policy；普通用户、用户直授、角色授予、超管旁路已有集成测试。
- Auth token/cookie 主体包含角色和 `is_super_admin` Claim。

### 4.5 Template 前端

已经存在：

- Spartan UI 用户管理页、表格、编辑/重置密码 Dialog、Mock 和服务。
- 登录启动、`authGuard`、`roleGuard`、`superAdminGuard`。
- 用户页面可以展示、筛选和编辑角色。

这些页面当前体现的是“用户 CRUD + 静态角色选择”，尚未成为动态 RBAC 管理端。

## 5. 当前与目标的差距

### 5.1 P0：已存在的授权绕过与语义冲突

| 差距 | 当前证据 | 风险 | 目标修复 |
| --- | --- | --- | --- |
| `ManageRoles` 未真正保护角色分配 | `CreateUserInputDto` / `UpdateUserInputDto` 含 Roles，端点只检查 Create/Update；`UserAppService.CreateAsync` 直接由输入赋角色 | 持有用户编辑权限即可提权；**仅有 `Users.Create` 同样可以创建 Admin 角色用户**，是同一路径的变体 | 角色分配拆为独立端点并要求 `Users.ManageRoles`；更新 DTO 不含角色字段；创建 DTO 改 `RoleIds` 且非空时额外要求 `Users.ManageRoles` |
| 角色前端硬编码 | `Role.Admin/Operator/Member`；后端未初始化 Operator | 选择 Operator 会被后端拒绝，新增角色无法显示 | 角色选项来自 Role API，按 Id 提交，Name 只展示 |
| 平台前端按 `Admin` 角色放行，后端按 Permission 放行 | `app.routes.ts` 使用 `roleGuard`，用户 API 使用动态 Policy | 可见性、导航和 API 语义不一致 | 统一改为 permissionGuard；角色只是权限来源 |
| 前端权限服务指向不存在端点 | `/api/v1/auth/permissions` 在后端不存在且服务未被使用 | 无法驱动菜单/按钮 | 建立 Current Authorization Bootstrap API |
| “Admin 角色自动拥有全部权限”的日志不真实 | 只有 `IsSuperAdmin` 用户旁路；普通 Admin 角色测试初始为 403 | 运维和开发者产生错误安全预期 | 选择明确语义：种子授予 Admin 角色，或改名/日志说明它不是全权角色 |
| `IncludeRoles=false` 未移除角色模型和 UI | 大量角色代码受 `IncludeIdentity` 控制，modifier 仅排除 Permissions 目录 | “无角色”生成物仍带角色和角色选择 | 按目标裁剪矩阵重写条件和场景断言 |

### 5.2 P1：功能权限 Framework 不完整

| 差距 | 影响 | 目标能力 |
| --- | --- | --- |
| Checker 不校验权限是否已定义、是否启用 | 直接调用 Checker 时，数据库残留/拼错的权限可能生效；`IsEnabled` 形同虚设 | 检查定义存在且启用，定义缺失默认拒绝 |
| 父子权限未进入判定 | UI 树和运行时语义可能不同 | 在 `IPermissionGrantManager` 写入时补齐祖先、撤销时级联清理子级；运行时保持扁平查找 |
| 只有 Allow 记录 | 无法表达明确拒绝和继承例外 | 引入 `Granted / Prohibited / Undefined`，Deny 优先；三态在存储、决策、API、UI 与 Mock 中一次性完整交付 |
| 只有单项 Grant/Revoke | 权限页保存需要大量请求且部分失败难回滚 | 提供 Replace API，单事务更新目标主体授权；Manager 不自行 SaveChanges，由调用方 UoW 提交 |
| 无授权版本和缓存失效 | 增加缓存后容易产生旧权限；当前每次检查也会查用户与角色 | 授予读取坍缩为一次取主体全量 + 请求级缓存；`AuthorizationRevisionRecord` 支撑 409 与后续分布式缓存失效 |
| 检查器为 Transient 且主体解析打两次库 | 每次 `[Authorize(Policy=...)]` 求值和每次按钮级判断都重新取主体，单请求可达 6~8 次查询 | `IPermissionChecker` 改 Scoped，按请求缓存主体与授予集 |
| 缺少 Client Provider | `ICurrentClient` 已有但权限主体没有客户端 | 扩展 Provider，不强制模板默认启用 |
| 定义元数据不足 | UI 无法表达危险操作、适用主体、功能开关 | 增加 Group、Description、AllowedProviders、IsVisible、Feature/State Checker 元数据 |

### 5.3 P1：Template 管理闭环缺失

- 没有角色 CRUD Application Service、API 和页面。
- 没有权限定义查询、角色/用户授权查询、批量保存 API。
- 没有角色权限树、有效权限来源解释、直接用户权限例外页。
- 没有基于 Permission 的路由 Guard、菜单裁剪、按钮/操作列裁剪。
- Mock 没有复刻授权端点与 401/403/权限裁剪语义。
- 没有并发令牌，两个管理员同时编辑授权时会后写覆盖前写。
- 授权变更审计仅有记录创建字段，没有“谁把什么从什么改成什么”的业务审计视图。

### 5.4 P2：数据范围与资源实例授权完全缺失

- 没有集合级数据范围定义、分配、解析和查询谓词契约。
- 没有资源类型/操作定义，没有资源 Handler/Service。
- 没有资源 ACL 存储、批量查询、孤儿授权清理。
- 没有水平越权、批量操作、统计/导出范围一致性的测试基座。

## 6. 目标 Framework 设计

### 6.1 包与依赖边界

保持现有三包向后兼容，在命名空间内增强功能权限；新增能力独立成包，避免强制依赖：

```text
Leistd.Security.Core
       当前 User / Client / Principal；不依赖 Authorization

业务 Application Adapter
       将 ICurrentUser / ICurrentClient 适配为授权 Subject

Leistd.Authorization.Core
       功能权限定义、三态决策、Provider、管理抽象；不依赖 Security 具体实现

Leistd.Authorization.AspNetCore
       动态 Policy、Requirement/Handler、API 宿主适配

Leistd.Authorization.EntityFrameworkCore
       功能权限 Grant 存储

Leistd.Authorization.Resource.Core                  [可选]
       资源类型、操作、规则 Handler、决策服务、ACL 抽象
Leistd.Authorization.Resource.EntityFrameworkCore   [可选]
       ResourceGrantRecord、清理器、批量 ACL 查询

Leistd.Authorization.DataScope.Core                 [可选]
       数据范围定义、Assignment、组合器、IQueryable 策略抽象
```

这样满足”components 不依赖 ddd-struct”：DataScope Core 只使用 BCL 的 Expression/IQueryable。

**不新增两个包**（决策 D5）：

- `Resource.AspNetCore` —— 资源检查按 §6.5 是命令式的、在实体加载后执行，没有 Attribute/Policy 可桥接；403/404 与 ProblemDetails 映射属 `Leistd.Exception` 既有职责。
- `Ddd.Authorization` —— `IRepository<TEntity,TKey>.GetQueryableAsync()` 已返回 `IQueryable<TEntity>`，而 `IDataScopeProvider<TEntity>.ApplyAsync` 的入参出参正是它；适配层只是语法糖，却要引入一个横跨 components 与 ddd-struct 的新包。改为在组件文档中给出组合示例。

### 6.2 功能权限决策模型

存储值与决策结果分开：无记录即 Undefined，因此存储层只有两个取值。

```csharp
// 持久化到 PermissionGrantRecord.Effect
public enum PermissionGrantEffect
{
    Granted = 1,
    Prohibited = 2
}

// 决策结果
public enum PermissionGrantResult
{
    Undefined = 0,
    Granted = 1,
    Prohibited = 2
}
```

组合规则：

1. 主体不存在、定义不存在、定义禁用：Deny。
2. 超级管理员是否旁路由 Options 控制；默认旁路功能权限，但不能旁路租户隔离等硬边界。
3. 任一 Provider 返回 Prohibited，最终为 Deny。
4. 没有 Prohibited 且至少一个 Granted，最终为 Allow。
5. 全部 Undefined，默认 Deny。

直接用户授权与角色授权都参与同一规则。三态在管理 UI、有效权限解释 API 和 Mock 中一次性完整交付，不保留"底层三态、界面两态"的中间形态（决策 D1）。

**父子权限采用写时补齐，不做读时前置条件**（决策 D2）。授予子权限时在 `IPermissionGrantManager` 内补齐祖先，撤销父权限时级联清理子权限；归一化下沉到 Manager 而非 API，使单条授予与种子路径同样覆盖。用户可见语义与"父级是前置条件"一致，但不破坏存量扁平授予、不需要 Options 逃生开关，且每次检查省去一次定义树回溯。唯一残留是绕过 Manager 直接写 DbContext 的数据不会被归一化，需在组件文档中说明。

**授予读取坍缩为一次取主体全量**（决策 D3）。`IPermissionGrantStore` 只保留 `GetGrantsForSubjectAsync(userId, roleIds)` 与 `GetGrantsAsync(providerName, providerKey)`，检查器按请求缓存结果，三态组合全部在内存完成。这同时提供 `/current` 的数据源，并使"批量检查不出现 N+1"成为结构性保证而非测试约定。`IPermissionChecker` 对外仍返回 `bool`，三态只存在于授予层与解释 API。

**并发与 revision**（决策 D4）。新增 `AuthorizationRevisionRecord(ProviderName, ProviderKey, Version)`，Manager 每次写入递增，Replace 传入 `expectedRevision` 不匹配返回 409。对外暴露给前端的 revision 由已取回的行计算 `hash(userVersion, sorted(roleId:roleVersion), sorted(roleIds))`，角色成员变更同样反映，无需扇出写。`PermissionGrantRecord` 的唯一索引保持 `(PermissionName, ProviderName, ProviderKey)` 不变——`Effect` 是值不是标识，纳入索引会允许同一主体对同一权限同时存在 Allow 与 Deny 两行。

### 6.3 数据范围契约

Framework 不应硬编码 `All / Department / DepartmentAndChildren / Self`，而应允许业务定义 `DataScopeDefinition` 并注册可翻译的 Provider。建议核心抽象：

```csharp
public interface IDataScopeProvider<TEntity>
{
    string ResourceName { get; }

    ValueTask<IQueryable<TEntity>> ApplyAsync(
        IQueryable<TEntity> query,
        DataOperation operation,
        DataScopeContext context,
        CancellationToken cancellationToken = default);
}
```

必须约束 Provider：

- 返回可由数据库 Provider 翻译的查询，不允许隐式 `AsEnumerable()` 或先加载后过滤。
- 租户、软删除等硬边界始终与业务范围做 AND。
- 多角色的允许范围默认做 OR/并集；显式 Deny 和硬边界最后做 AND 排除。
- Read/Update/Delete/Export 可以使用不同策略，不能假设“能看就能改”。
- Count、List、Export、BatchUpdate 必须使用同一个范围入口。

典型业务项目可以注册：

```text
Order.Read:   All | Own | Organization | OrganizationAndDescendants | CustomOrganizations
Order.Update: Own | AssignedToMe | Organization
```

范围 Assignment 的通用持久化只保存 `ProviderName + ProviderKey + ResourceName + ScopeName + ScopeValue`；组织树展开、项目成员等业务关系仍由业务表和 Provider 负责，不复制到通用授权表。

### 6.4 资源实例授权契约

资源授权必须同时支持两种来源：

1. **规则 Handler**：所有者、项目成员、资源状态、时间、设备等 ABAC/ReBAC 规则。
2. **资源 ACL**：管理员或资源所有者对某个实例做 User/Role/Client 的显式允许/拒绝。

建议关键类型：

```text
ResourceOperation                 Read / Update / Delete / Share / 自定义
ResourceAuthorizationContext      Subject + ResourceName + ResourceKey + Operation + Resource
IResourceAuthorizationHandler<T>  领域规则
IResourceAuthorizationService     组合规则与 ACL，Deny 优先，默认拒绝
IResourceGrantStore               资源 ACL 批量查询
IResourceGrantManager             Replace/Grant/Revoke + 审计
```

资源 ACL 使用独立 `ResourcePermissionGrantRecord`：

```text
Id
ResourceName
ResourceKey
PermissionName / Operation
ProviderName (User / Role / Client)
ProviderKey
Effect (Granted / Prohibited)
CreationTime / CreatorId
```

唯一索引建议为 `(ResourceName, ResourceKey, PermissionName, ProviderName, ProviderKey)`。资源删除后通过本地/分布式事件调用 Cleaner；由于通用表无法对任意业务表建立外键，还需要幂等清理和周期性孤儿检查。

### 6.5 一次请求中的标准执行链

#### 列表、统计、导出

```text
RequirePermission("Orders.Read")
  -> repository.GetQueryableAsync()
  -> dataScope.ApplyAsync(query, Read)
  -> 追加业务筛选/排序/分页
  -> SQL
```

若资源 ACL 也赋予可见性，ACL 必须作为 SQL EXISTS/JOIN 合并进范围查询，不能逐行检查。

#### 详情

```text
RequirePermission("Orders.Read")
  -> scopedQuery.SingleOrDefault(id)
  -> 不可见统一返回 404（按 API 威胁模型可配置）
  -> resourceAuthorization.AuthorizeAsync(Read, entity)
```

#### 更新/删除

```text
RequirePermission("Orders.Update")
  -> scopedQuery.SingleOrDefault(id, Update)
  -> resourceAuthorization.AuthorizeAsync(Update, entity)
  -> domain method
  -> SaveChanges + audit
```

#### 创建

创建没有现成资源，先授权功能权限，再对父容器/租户/目标组织执行资源授权，并验证客户端提交的 OwnerId、OrganizationId 没有越界。

## 7. Template 目标闭环

### 7.1 后端用例与 API

权限名统一 `App.*` 前缀（决策 D10）。现有 OpenIddict 的 `AuthorizationController` 改名 `ConnectController`，新增 `PermissionController` / `RoleController`（决策 D11）。

| API | 用途 | 保护权限 |
| --- | --- | --- |
| `GET /api/v1/permissions/current` | 当前用户有效权限与 revision | 已认证 |
| `GET /api/v1/permissions/definitions` | 权限组/树/元数据 | `App.Permissions.Default` |
| `GET/PUT /api/v1/permissions/grants/{provider}/{key}` | 用户/角色功能权限查询与原子替换 | `App.Roles.ManagePermissions` 或 `App.Users.ManagePermissions` |
| `GET/POST/PUT/DELETE /api/v1/roles` | 角色 CRUD | 对应 `App.Roles.*` |
| `GET/PUT /api/v1/users/{id}/roles` | 查询/替换用户角色 | `App.Users.ManageRoles` |

数据范围 Assignment 与资源 ACL 本次只交付 Framework 原语，不进模板 API（决策 D6）。

关键约束：

- `UpdateUserInputDto` 移除角色字段；`CreateUserInputDto` 改 `RoleIds`，非空时额外要求 `App.Users.ManageRoles`（决策 D9）。
- Role API 使用 Guid Id 作为关联键，Name 是唯一且稳定的业务标识；用户赋权 DTO 不接受任意角色名，`GetRolesByNamesAsync` 整条按名解析路径删除。
- 批量替换接口带 `expectedRevision`，在一个事务内完成差异更新和业务审计，冲突返回 409。
- 删除角色前检查静态角色、用户关联和 PermissionGrant；静态角色不可删除，普通角色可选择拒绝或显式迁移用户。
- 有效权限响应区分 `direct`、`inherited`、`prohibited` 和 `effective`，让管理员能解释”为什么有/没有权限”。
- Admin 角色在初始化时幂等补齐当前全部权限定义，成为可编辑、可撤权的普通角色；`IsSuperAdmin` 是系统唯一旁路（决策 D7）。

### 7.2 前端页面

#### 角色管理 `/platform/roles`

- 服务端分页角色表：名称、显示名、静态/默认、用户数、有效权限数、更新时间。
- 新建/编辑使用 Spartan Dialog + Signal Forms。
- 行操作包含“成员”“配置权限”“编辑”“删除”；静态角色禁用删除并展示原因。
- 角色来源全部来自 API，不保留 `Role` enum。

#### 角色权限编辑器

- 使用 Group + Tree 展示定义，支持搜索、全选当前组、展开/折叠。
- 使用三态控件（继承 / 允许 / 拒绝），不用一个 checkbox 混淆来源。
- 选中子级时自动补齐父级，取消父级时清理子级；该归一化同时在后端 Manager 内执行，前端只是即时反馈。
- 页面明确展示 Direct、Inherited、Prohibited、Effective。
- 保存调用一次 Replace API，`expectedRevision` 冲突时提示重载。

#### 用户管理增强

- 用户基本资料、状态修改与角色分配拆开。
- 角色选择从后端动态加载；只有 `Users.ManageRoles` 才展示/启用入口。
- 可选“权限例外”Tab 管理用户直授/拒绝，并解释角色继承来源。
- Create、Update、ResetPassword、Delete 等按钮分别受对应权限裁剪。

#### 资源权限 Dialog 与数据范围 UI（本次不做）

模板没有任何业务资源页面（只有 users / roles / open-applications），资源 ACL Dialog 无宿主可嵌入；模板也不内置组织模型，数据范围选择器没有数据源。两者做出来都是空壳，因此本次只交付 Framework 原语与测试，在组件文档中给出业务项目的接入形态（决策 D6）。

未来业务项目落地时仍遵循原则：资源 Dialog 嵌入具体资源页面而非独立导航页；数据范围区嵌入角色/用户授权编辑器，没有任何 `DataScopeDefinition` 时整个区块自动隐藏。

### 7.3 前端权限消费

新增：

- `AuthorizationService`：启动时加载 current authorization，保存 permission set 和 revision。
- `permissionGuard`：路由按 permission，而不是按角色。
- `hasPermission` Directive/纯函数：菜单、按钮、表格操作列统一消费。
- 403 页面：区别于 401 登录跳转。
- 授权变更后刷新 current authorization；多标签页可用 BroadcastChannel 同步。

旧门禁整体删除，不保留对新实现的转发层：`role.enum.ts`（含 `ROLE_LABEL_MAP` / `ROLE_ICON_MAP`）、`role-label-pipe.ts`、`role-guard.ts`、`super-admin-guard.ts`、`permission-service.ts`、`permission.model.ts`、`AuthService.hasRole`、`UserModel.isAdmin()`、`default-sidebar` 的 `superAdminOnly` 标志。

现有 9 处消费点的替代判据：

| 位置 | 现状 | 替代 |
| --- | --- | --- |
| `app.routes.ts` | `roleGuard` + `data.role='Admin'` | `permissionGuard` |
| `users.ts:200,218`、`user-edit-dialog.ts:214,221`、`user-table.ts` | 静态 `ROLE_LABEL_MAP` / `ROLE_ICON_MAP` | 角色 API 的 `displayName`；图标不再按角色名静态映射 |
| `workspace-dashboard.ts` | `RoleLabelPipe` | 后端返回的 `displayName` |
| `login.ts:172`、`external-auth-callback.ts:91`、`user-menu.ts:179`、`profile-settings-dialog.html:79` | `isAdmin()` | "是否具备任一平台权限"。**不要**换成 `isSuperAdmin`，那只是把角色门禁换成超管门禁，仍不是权限语义 |

后端同样删除并行机制：`Api/Authorization/SuperAdminOnlyAttribute.cs`（全仓库 0 引用，且是与权限并行的第二套授权机制）、`Domain/Users/Specifications/UserScopeSpecifications.cs`（死代码，概念由 `DataScope.Core` 接管）。

前端隐藏不构成授权；API、数据范围和资源检查仍必须完整执行。

### 7.4 Mock 闭环

每个新端点同步补 `_mock/data`、`_mock/api`、`_mock/index.ts`。

Mock **只复刻端点形状、401/403 和每个演示账号的固定权限集，不复刻决策引擎**（决策 D12）。在 TS 里重写一遍三态组合与父子归一化必然与后端漂移，而漂移的 Mock 比没有 Mock 更危险。

因此复刻：

- 每个演示账号一组预置的有效权限集（含一个带显式拒绝的账号）。
- 路由/API 的 401、403。
- 静态角色不可删除、`expectedRevision` 冲突返回 409。
- 用户普通更新不能修改角色、创建带 `RoleIds` 需要 `ManageRoles`。
- 条件裁剪后不注册已移除端点。

不复刻：三态组合规则、写时祖先补齐、定义树遍历——这些以后端集成测试为唯一事实来源。

## 8. 条件裁剪设计

### 8.1 推荐参数

| 参数 | 依赖 | 默认 | 能力 |
| --- | --- | --- | --- |
| `IncludeIdentity` | 无 | true | 用户、登录、当前主体 |
| `IncludeRoles` | Identity | true | Role/UserRole、角色 CRUD、用户角色分配、功能权限定义与授予、管理页、permissionGuard |

**不新增 `IncludePermissions`**（决策 D6）。"有角色但无功能权限"不是应该存在的最终态：它会让模板生成的每个 Controller 永久携带两套授权模式的条件分支，换来一个几乎无人选择的配置。角色与功能权限是同一项能力，由 `IncludeRoles` 一个参数控制。

**不新增 `IncludeDataScopes` / `IncludeResourceAuthorization`**。这两层本次只交付 Framework 原语，模板没有可承载其 UI 的业务资源页面（见 §7.2）。业务项目按需引用可选包即可，不需要模板参数。

### 8.2 生成矩阵

| 场景 | Identity | Roles | 预期 |
| --- | --- | --- | --- |
| minimal | false | false | 无认证、角色、授权 UI/DI/表 |
| no-roles | true | false | 登录和用户资料；无 Role/UserRole、无权限存储、无角色页面与角色字段 |
| default | true | true | 完整 RBAC 管理闭环 |

每个场景必须同时裁剪：实体/映射、PackageReference、DI、Controller、DTO、前端路由/菜单/服务/组件、Mock、i18n、测试和 README。当前 `no-roles` 场景只裁掉 Permissions 目录而保留角色模型和页面，需要重构；真实裁剪面还包括 `PermissionSubjectProvider` 的 `IRepository<UserRole,Guid>` 注入、`GetUserPagedInputDto.roles` 筛选、`UserRolesQueryTests`、`AuthPrincipalFactory` 的 role claim、OpenIddict `Scopes.Roles` 种子、`userinfo` 端点的 `Claims.Role`、`user-table` 角色列和 `_mock/data/user.ts`。`IncludeRoles=false` 时 `IsSuperAdmin` 保留。

## 9. 能否不要数据范围 ABAC，只用资源实例授权

### 9.1 可以省略的场景

以下项目可以只使用 RBAC + 资源实例授权：

- 数据量小，主要通过精确 ID 打开单个资源，很少有后台列表、统计和导出。
- 资源天然通过一个 ACL/成员关系表查询，例如文档协作、项目成员制空间。
- 可见集合就是 `ResourceGrant` 或 Membership 的直接 SQL JOIN，不需要复杂组织层级。
- 产品接受每一种列表都显式按 ACL JOIN，且已有可靠索引和清理机制。

此时不引用 `Leistd.Authorization.DataScope.Core`，资源授权仍需同时提供“实例检查”和“按 ACL 生成候选查询”的能力。

### 9.2 不应省略的场景

以下通用后台/SaaS 场景不能仅靠逐实例授权：

- 列表分页、总数、聚合、导出、批量审批必须准确。
- 规则是“本人、本组织、下级组织、负责区域、项目组”等集合关系。
- 数据量大，不可能加载候选后逐条检查。
- 组织、负责人或状态频繁变化，若把关系展开成 ACL 会产生大量同步写和孤儿记录。
- 管理员拥有“全量”或分层范围，需要高效 SQL 谓词而不是每条 ACL。

### 9.3 复杂度是否真的降低

| 维度 | 数据范围策略 | 仅资源实例 ACL |
| --- | --- | --- |
| 单对象检查 | 中等 | 简单、直观 |
| 列表/分页 | 可直接翻译为 SQL | 必须额外 JOIN ACL；逐条检查不可用 |
| 所有者/组织变化 | 查询时读取真实关系 | 需要同步重写 ACL 或接受不一致 |
| ACL 数据规模 | 只保存少量 Scope Assignment | 可能按资源 x 主体 x 操作爆炸 |
| 解释性 | “来自角色的数据范围 + 业务关系” | “来自某条 ACL”，简单但数量巨大 |
| 通用后台适配 | 强 | 弱 |
| 文档分享/临时授权 | 弱，需要额外模型 | 强 |

所以，**去掉数据范围不会让复杂度消失，只会把集合查询复杂度转移到 ACL 物化、JOIN、同步和清理**。对简单系统它确实降低初始成本；对通用框架，强行只保留资源实例授权会牺牲列表正确性和可扩展性。

最终建议不是“所有项目必须 ABAC”，而是：

- Framework 提供可选 DataScope 原语。
- Template 默认不开启 DataScope，不制造组织模型。
- 业务一旦出现组织范围、复杂列表或批量操作，就启用数据范围策略。
- 资源授权始终用于实例最终裁决和临时 ACL，不承担所有集合授权职责。

## 10. 安全与一致性规则

1. 默认拒绝；权限名不存在、Provider 无结论、资源 Handler 无 Allow 都拒绝。
2. 前端不可作为安全边界；任何按钮隐藏都必须有对应服务端校验。
3. 详情/更新/删除统一防水平越权；不能只保护列表。
4. 租户隔离不可被普通数据范围或超管随意旁路；跨租户操作使用独立、可审计的系统能力。
5. 授权变更、角色变更、资源分享和显式拒绝必须记录业务审计日志。
6. 权限/范围缓存必须有 revision 或事件失效；不能依赖长生命周期 token 中的角色 Claim 保持实时一致。
7. 批量操作先在数据库范围内定位目标，再验证提交数量与实际授权数量一致，禁止静默跳过越权项。
8. 返回 403 还是 404 由威胁模型统一配置；资源存在性敏感时默认 404。

## 11. 交付形态

**一次交付，直接落最终态，不保留中间态和兼容层。** 因此本节描述的是三层各自的能力边界与可选性，而不是时间轴；实施顺序（含提交切分）见[实施计划](../plans/2026-08-06-authorization-implementation.md)。

| 层次 | 交付物 | 可选性 | 模板消费 |
| --- | --- | --- | --- |
| 功能权限 RBAC | `Authorization.Core` / `.AspNetCore` / `.EntityFrameworkCore` 的最终态：三态、定义校验、写时归一化、主体全量读取、Replace、revision | 由 `IncludeRoles` 控制 | 完整管理闭环（角色 CRUD、权限编辑器、用户角色分配、当前用户有效权限） |
| 资源实例授权 | `Authorization.Resource.Core` + `.EntityFrameworkCore`：操作定义、规则 Handler、决策服务、ACL 存储与幂等清理 | 独立可选包，业务项目按需引用 | 本次不消费；组件文档给出接入形态 |
| 数据范围 | `Authorization.DataScope.Core`：Scope 定义、Assignment、组合器、`IQueryable` 策略 | 独立可选包，业务项目按需引用 | 本次不消费；组件文档给出接入形态 |

无兼容层的具体含义：被替代的公共 API 直接改形状（`IPermissionGrantStore` / `IPermissionGrantManager` 的读方法重构），不留过渡重载与 `[Obsolete]` 转发；被替代的实现整体删除，不降级为转发；破坏性变更集中本次一并完成，在 `docs/framework/versioning.md` 一次性说明。

可选两层没有模板消费者，其正确性风险不在代码量而在缺少真实使用者。缓解办法是 Framework 测试中放一个带 `OwnerId` + `OrganizationId` 的小领域，在关系型 Provider 上验证 List / Count / Export 走同一范围入口、谓词可翻译、ACL 以 EXISTS/JOIN 合并进候选查询、水平越权被拦截——而不是只测契约形状。

## 12. 验收标准

### Framework

- 定义缺失、定义禁用、写时祖先补齐、父级撤销级联、显式拒绝优先、用户直授、角色继承、超管、并发 409 都有单元测试。
- 一次权限检查等于一次主体查询加一次授予查询；同请求内多次检查不产生额外往返。N+1 由存储 API 形状保证，不依赖测试约定。
- Resource Handler 的 Allow/Deny/Undefined 组合和 DataScope 多角色并集有确定语义。
- 测试运行在 **Sqlite 关系型 Provider** 上，而非 InMemory——后者全内存求值，不可翻译的 DataScope Provider 也会绿灯、不强制唯一索引、无法证伪批量查询。
- Core 包不依赖 ASP.NET Core、EF Core 或 DDD；components 不反向依赖 ddd-struct。

### Template 后端

- 用户 CRUD、角色 CRUD、角色分配、角色权限、用户权限例外形成完整 API 流程。
- 仅有 `Users.Update` 的主体不能改变角色；仅有 `Users.Create` 的主体不能在创建时携带 `RoleIds`；仅有 `Users.ManageRoles` 也不能更新普通资料。
- 普通用户、角色授予、用户直授、显式拒绝、超管、水平越权均有集成测试。
- 授权并发更新返回 409，不静默覆盖。
- Admin 角色初始化后持有当前全部权限定义，且可被撤权；系统内不存在第二条代码旁路。

### Template 前端

- 路由、菜单、页面按钮和 API 使用同一组 Permission 常量/契约。
- 角色与权限完全由 API 元数据驱动，没有硬编码业务角色列表。
- 不存在任何以角色名或 SuperAdmin 作为平台访问安全语义的判断；`role.enum.ts`、`role-guard.ts`、`super-admin-guard.ts`、`permission-service.ts` 已删除且无替代转发层。
- Mock 复刻端点形状与 401/403，独立运行不会掩盖 403，且不重复实现决策规则。
- Spartan 页面通过 lint、build、关键组件测试和桌面/移动端浏览器验证。

### 条件矩阵

- `minimal`、`no-roles`、`default` 全部生成、后端构建/测试、前端 lint/build。
- 生成物无残留条件标记、无已裁剪端点/菜单/PackageReference、README 与实际能力一致。

## 13. 最终决策建议

1. **功能权限管理闭环**是本次交付的核心，Framework 基础已存在，主要风险是 Template 的管理缺口与 `ManageRoles` 绕过（创建侧与更新侧各一处）。
2. **资源实例授权**作为独立组件交付，采用规则 Handler + ACL，不与功能权限 Grant 表混表；不做 AspNetCore 桥接包，资源检查是命令式的。
3. **数据范围**作为独立可选组件；不做通用规则 DSL，不内置组织模型，不做 DDD 适配包。
4. 三层与前两条一并在**同一次交付**中完成，直接落最终态，不保留中间态与兼容层。
5. 明确产品口径：简单项目可选 `RBAC + Resource Authorization`；通用后台、SaaS 和任何有复杂列表/批量操作的项目使用三层组合。
6. 复杂度控制的主要手段是**删除**而非新增抽象：3 个存储便利重载、2 个 Manager 转发读方法、9 处前端旧门禁、2 处后端死代码、1 个模板参数、3 个矩阵场景一并移除。
