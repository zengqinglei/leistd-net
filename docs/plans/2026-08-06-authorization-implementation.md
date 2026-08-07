# Leistd 授权闭环实施计划

> 依据：[端到端授权体系评估与目标设计](../assessments/2026-08-06-authorization-end-to-end-design.md)
> 基线分支：`feat/frontend-spartan-migration`
> 基线提交：`bbcc61878dd08f962b853e583f560dec8e23fd25`

## 交付原则

**一次交付，直接落最终态，不保留中间态和兼容层。**

- 公共 API、schema 和语义按目标形态直接改，不留过渡重载、不留 `[Obsolete]` 转发、不留"旧值兼容"分支。
- 被替代的实现整体删除，不降级为对新实现的转发。
- 破坏性变更集中在本次一并完成，并在 `docs/framework/versioning.md` 一次性说明。

## 决策基线

以下决策在实施前已锁定，实现与文档以此为准；与评估文档冲突处以本表为准，并同步回评估文档。

| # | 决策 | 结论 | 理由 |
| --- | --- | --- | --- |
| D1 | 三态授权 | **全量交付**：`Effect` 列、Checker 组合、Replace API、权限编辑器 UI、有效权限解释视图、Mock 一次做完 | 贵且不可逆的是 schema 与决策语义，UI 只是一个三态控件；分批做等于第二次破坏性变更。模板支持外部登录，角色成员可能来自外部身份源，无法靠"拆分角色"表达例外 |
| D2 | 父子权限 | **写时补齐**：授予子权限时在 `IPermissionGrantManager` 内补齐祖先，撤销父权限时级联清理子权限；运行时不做定义树回溯 | 用户可见语义与"读时前置条件"相同，但不破坏存量扁平授予、不需要 Options 逃生开关、每次检查省一次树遍历 |
| D3 | 授予存储形状 | **坍缩为一次取主体全量**：`GetGrantsForSubjectAsync(userId, roleIds)`，请求级缓存，三态组合全内存完成 | 同时解决请求级 memoization、`/current` 数据源、三态组合和 N+1；N+1 从"需测试证明"变为结构上不可能 |
| D4 | 并发与 revision | **保留** `AuthorizationRevisionRecord`，Replace 走乐观并发返回 409，前端 bootstrap 携带 revision | 一张两列表同时支撑 409、前端刷新与后续分布式缓存失效，避免第二次改造 |
| D5 | 新增包数量 | **3 个**：`Leistd.Authorization.Resource.Core`、`Leistd.Authorization.Resource.EntityFrameworkCore`、`Leistd.Authorization.DataScope.Core`。不做 `Resource.AspNetCore` 与 `Ddd.Authorization` | 资源检查是命令式、实体加载后执行，没有 Attribute/Policy 可桥接，403/404 映射属 `Leistd.Exception` 职责；`IRepository.GetQueryableAsync()` 已返回 `IQueryable`，DDD 适配层只是语法糖却引入跨 family 依赖争议 |
| D6 | 模板裁剪参数 | **只有** `IncludeIdentity` / `IncludeRoles`（含权限）。删除 `IncludePermissions`、`IncludeDataScopes`、`IncludeResourceAuthorization` 及 `roles-only` / `data-scope` / `resource-auth` / `full-auth` 场景 | "有角色但无功能权限"不是应该存在的最终态，它会让每个 Controller 永久携带两套授权模式的条件分支；DataScope/Resource 本次只交付 Framework 原语，模板无业务资源页面可承载其 UI |
| D7 | Admin 角色语义 | 种子时**幂等补齐当前全部权限定义**给 Admin 角色，使其成为可编辑、可撤权的普通角色；`PermissionSubject.IsSuperAdmin` 是系统唯一旁路 | 消除"代码里自动全权"的第二条旁路；幂等补齐使新增权限定义在重启后自愈 |
| D8 | 测试基座 | Framework 授权测试从 `Microsoft.EntityFrameworkCore.InMemory` 换为 **Sqlite 关系型 Provider** | InMemory 全内存求值，不可翻译的 DataScope Provider 也会绿灯、不强制唯一索引、无法证伪批量查询 |
| D9 | 用户创建侧授权 | `CreateUserInputDto` 改用 `RoleIds`，`RoleIds` 非空时**额外**要求 `App.Users.ManageRoles` | 评估文档只处理了 Update，`UserAppService.CreateAsync` 仍从输入直接赋角色且仅由 `Users.Create` 保护，是同一提权路径的变体 |
| D10 | 权限名前缀 | 统一 `App.*`，删除评估文档 §7.1 中的 `Authorization.*` 写法 | 现有 `PermissionConstant` 已是 `App.*`，两套前缀混用无收益 |
| D11 | 控制器命名 | 现有 OpenIddict 控制器 `AuthorizationController` 改名 `ConnectController`；新增 `PermissionController`、`RoleController` | 类名已被占用，且 `ConnectController` 与其 `~/connect/*` 路由一致 |
| D12 | Mock 范围 | 只复刻端点形状、401/403 与每个演示账号的固定权限集，**不复刻决策引擎** | 在 TS 里重写一遍三态与父子规则必然与后端漂移 |

## 交付范围

**做**：

- Framework 功能权限 RBAC 最终态（三态、定义校验、写时归一化、主体全量读取、Replace、revision）。
- Framework 资源实例授权原语与 EF 存储（`Resource.Core` + `Resource.EntityFrameworkCore`）。
- Framework 数据范围原语（`DataScope.Core`）。
- Template 后端角色 CRUD、权限定义/授予 API、用户角色独立管理、当前用户有效权限。
- Template 前端角色页、权限编辑器、permissionGuard、按钮裁剪、403 页、Mock。
- `IncludeRoles=false` 的真实裁剪。

**不做**：

- Resource / DataScope 的模板参数、页面、Dialog 与矩阵场景（仅 Framework 原语 + 测试 + 组件文档中的接入说明）。
- 分布式权限缓存（revision 已备好扩展点）。
- 组织树、部门、成员关系等业务模型。

## 实施顺序

一次交付不等于一个提交。以下顺序保证提权修复不被包工程阻塞。

### 1. P0 安全修复

- `CreateUserInputDto` / `UpdateUserInputDto`：`List<string> Roles` → `UpdateUserInputDto` 移除角色字段；`CreateUserInputDto` 改 `List<Guid> RoleIds`，非空时在应用服务内额外校验 `App.Users.ManageRoles`。
- 新增 `GET/PUT /api/v1/users/{id}/roles`，由 `App.Users.ManageRoles` 保护。
- 删除 `UserAppService.GetRolesByNamesAsync` 按名字解析角色的整条路径，改按 Id；角色名仅用于展示与筛选。
- 前端删除 `permission-service.ts`、`permission.model.ts`、`role.enum.ts`、`role-label-pipe.ts`、`role-guard.ts`、`super-admin-guard.ts`、`AuthService.hasRole`、`UserModel.isAdmin()`、`default-sidebar` 的 `superAdminOnly` 标志。
- 删除 `Api/Authorization/SuperAdminOnlyAttribute.cs`（0 引用，且是与权限并行的第二套机制）与 `Domain/Users/Specifications/UserScopeSpecifications.cs`（死代码，概念由 `DataScope.Core` 接管）。
- 修正 `SystemInitializer` 中"Admin 角色通过代码逻辑自动拥有所有权限"的日志。

### 2. Framework 功能权限最终态

公共契约目标形状：

```csharp
// 存储值：无记录即 Undefined，因此存储层只有两个取值
public enum PermissionGrantEffect { Granted = 1, Prohibited = 2 }

// 决策结果：三态
public enum PermissionGrantResult { Undefined = 0, Granted = 1, Prohibited = 2 }
```

- `IPermissionGrantStore` 删除 `IsGrantedToUserAsync` / `IsGrantedToRoleAsync` / `IsGrantedToAnyRoleAsync` / `IsGrantedToUserOrRolesAsync` / `GetGrantedPermissionsFor*Async`，改为 `GetGrantsForSubjectAsync(userId, roleIds, ct)` 与 `GetGrantsAsync(providerName, providerKey, ct)`。
- `IPermissionGrantManager` 删除两个转发读方法（读职责归 Store），新增 `ReplaceGrantsAsync(providerName, providerKey, grants, expectedRevision, ct)`；写时归一化祖先与级联清理子权限在此实现，单条 Grant/Revoke 与种子路径同样覆盖。
- `ReplaceGrantsAsync` 不自行 `SaveChangesAsync`，在调用方 UoW/事务内完成；现有 `GrantAsync`/`RevokeAsync` 的自 Save 一并去掉，统一约定。
- `DefaultPermissionChecker` 注入 `IPermissionDefinitionManager`：定义不存在或 `IsEnabled=false` 一律拒绝；注册从 `TryAddTransient` 改 **Scoped**，按请求缓存主体与授予集。
- 组合规则：任一 Prohibited → Deny；无 Prohibited 且至少一个 Granted → Allow；全 Undefined → Deny；`IsSuperAdmin` 旁路功能权限。
- `PermissionGrantRecord` 新增非空 `Effect` 列；唯一索引保持 `(PermissionName, ProviderName, ProviderKey)` 不变（Effect 是值不是标识，入索引会允许同主体同时存在 Allow 与 Deny）。
- 新增 `AuthorizationRevisionRecord(ProviderName, ProviderKey, Version)`；Manager 每次写入递增。Replace 传入 `expectedRevision` 不匹配时抛并发冲突。
- 当前用户对外 revision 由已取回的行计算：`hash(userVersion, sorted(roleId:roleVersion), sorted(roleIds))`，角色成员变更同样反映，无需扇出写。
- `IPermissionChecker` 对外仍返回 `bool` / `MultiplePermissionGrantResult`，三态只存在于授予层与解释 API。
- `PermissionPolicyProvider` 回退 `DefaultAuthorizationPolicyProvider` 的机制不变。
- 测试项目改用 Sqlite Provider，覆盖：定义缺失、定义禁用、父级写时补齐、父级撤销级联、显式拒绝优先、用户直授、角色继承、超管、并发 409、单次查询取回主体全量。

### 3. Template 后端 RBAC 闭环

- `AuthorizationController` → `ConnectController`（路由不变）。
- 新增 `RoleController`：`GET/POST/PUT/DELETE /api/v1/roles`，由 `App.Roles.*` 保护；静态角色不可删除，删除前检查用户关联与授予记录。
- 新增 `PermissionController`：
  - `GET /api/v1/permissions/current` —— 有效权限 + revision，仅要求已认证；
  - `GET /api/v1/permissions/definitions` —— 权限组/树，`App.Permissions.Default`；
  - `GET/PUT /api/v1/permissions/grants/{provider}/{key}` —— 查询与原子替换，`App.Roles.ManagePermissions` / `App.Users.ManagePermissions`。
- `PermissionConstant` 补 `App.Permissions.Default` 与 `App.Users.ManagePermissions`，`PermissionDefinitionProvider` 同步。
- 授予查询响应区分 `direct` / `inherited` / `prohibited` / `effective`，支撑"为什么有/没有权限"。
- `SystemInitializer`：Admin 角色每次初始化幂等补齐当前全部权限定义。
- 集成测试：仅 `Users.Update` 不能改角色、仅 `Users.Create` 不能带 `RoleIds`、仅 `Users.ManageRoles` 不能改资料、显式拒绝覆盖角色授予、并发 Replace 返回 409、静态角色不可删。

### 4. Template 前端 RBAC 闭环

- 新增 `AuthorizationService`（启动加载 current，持有 permission set 与 revision）、`permissionGuard`、`hasPermission` 入口、403 页面。
- 角色管理页 `/platform/roles`：Spartan Table + Dialog + Signal Forms，服务端分页。
- 角色权限编辑器：定义树 + 搜索 + 分组全选，**三态控件**（继承/允许/拒绝），选中子级自动补齐父级、取消父级清理子级，保存一次 Replace，409 提示重载。
- 用户页：资料与角色分配拆分；角色选项来自角色 API 按 Id 提交；按钮按权限裁剪。
- 旧门禁 9 处消费点替换：`app.routes.ts` → `permissionGuard`；`users.ts:200,218`、`user-edit-dialog.ts:214,221`、`user-table.ts` 的角色选项/标签 → 角色 API `displayName`（图标不再按角色名静态映射）；`workspace-dashboard.ts` 的 `RoleLabelPipe` → 后端 displayName；`login.ts:172`、`external-auth-callback.ts:91`、`user-menu.ts:179`、`profile-settings-dialog.html:79` 的 `isAdmin()` → 判据改为"是否具备任一平台权限"（不是改为 SuperAdmin，否则只是把角色门禁换成超管门禁）。
- Mock 按 D12 范围补齐新端点。

### 5. `IncludeRoles=false` 真实裁剪

除实体、映射、DI、DTO、API、前端页面外，还需覆盖：`PermissionSubjectProvider` 的 `IRepository<UserRole,Guid>` 注入、`GetUserPagedInputDto.roles` 筛选、`UserRolesQueryTests`、`AuthPrincipalFactory` 的 role claim、OpenIddict `Scopes.Roles` 种子、`userinfo` 端点的 `Claims.Role`、`user-table` 角色列、`_mock/data/user.ts`。`IncludeRoles=false` 时 `IsSuperAdmin` 保留。

### 6. Resource 与 DataScope 原语

- `Resource.Core`：`ResourceOperation`、`ResourceAuthorizationContext`、`IResourceAuthorizationHandler<T>`、`IResourceAuthorizationService`、`IResourceGrantStore`、`IResourceGrantManager`；Deny 优先，默认拒绝。
- `Resource.EntityFrameworkCore`：`ResourcePermissionGrantRecord`，唯一索引 `(ResourceName, ResourceKey, PermissionName, ProviderName, ProviderKey)`；批量 ACL 查询；资源删除后的幂等清理器。
- `DataScope.Core`：`DataScopeDefinition`、`DataScopeContext`、`DataOperation`、`IDataScopeProvider<TEntity>`；多角色允许范围 OR 并集，硬边界与显式 Deny 最后 AND。
- 测试放一个带 `OwnerId` + `OrganizationId` 的小领域，在 Sqlite 上验证 List / Count / Export 走同一范围入口的一致性、谓词可翻译（不得隐式客户端求值）、ACL 以 EXISTS/JOIN 合并进候选查询、水平越权被拦截。

### 7. 文档、打包与矩阵

- `framework/docs/components/authorization.md` 按新公共 API 重写；新增 `authorization-resource.md`、`authorization-data-scope.md`；更新 `framework/docs/components/README.md` 索引与依赖图。
- `docs/framework/versioning.md` 一次性记录本次破坏性变更：Store/Manager 公共接口重构、`Effect` 列、Checker 生命周期与拒绝语义。
- Template README 与场景断言同步。

## 验证

```powershell
dotnet build framework/Leistd.Framework.slnx -c Release
dotnet test  framework/Leistd.Framework.slnx -c Release
dotnet pack  framework/Leistd.Framework.slnx -c Release -o .tmp/local-feed
pwsh framework/build/test-package-consumption.ps1 -PackageIds Leistd.Authorization.Core,Leistd.Authorization.AspNetCore,Leistd.Authorization.EntityFrameworkCore,Leistd.Authorization.Resource.Core,Leistd.Authorization.Resource.EntityFrameworkCore,Leistd.Authorization.DataScope.Core
pwsh framework/build/check-docs-sync.ps1
pwsh framework/build/check-docs-api-drift.ps1
pwsh scripts/test-template-matrix.ps1 -Scenarios default,minimal,no-roles
```

浏览器验证：登录、角色创建、权限授予与显式拒绝、普通用户允许/拒绝路径、撤销后失效、并发保存 409、移动端布局。

## 完成标准

- 仅 `Users.Update` 的主体不能改角色；仅 `Users.Create` 的主体不能在创建时带 `RoleIds`；仅 `Users.ManageRoles` 的主体不能改普通资料。
- 一次权限检查等于一次主体查询加一次授予查询，同请求内多次检查不产生额外往返。
- 显式拒绝覆盖角色授予；定义缺失或禁用一律拒绝；父权限在写入时已补齐，运行时无树遍历。
- 角色权限保存一次请求完成，并发冲突返回 409 而非静默覆盖。
- 前端不存在任何以角色名或 SuperAdmin 作为平台访问安全语义的判断，`role.enum.ts`、`role-guard.ts`、`super-admin-guard.ts`、`permission-service.ts`、`SuperAdminOnlyAttribute` 均已删除且无替代转发层。
- `IncludeRoles=false` 的生成物不含 Role/UserRole、权限存储、角色页面、角色字段与 role claim。
- 三个新包均有组件文档、随包 API 无漂移、可从隔离本地源还原构建。
- DataScope 与 Resource 的一致性测试在关系型 Provider 上通过，不依赖 InMemory。
