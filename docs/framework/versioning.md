# Leistd 框架版本与发布规范

## 版本来源（VERSION 文件）

框架版本的**唯一来源**是仓库根的 **`VERSION`** 文件（基准 `x.y.z`），它存的是**最近一次已发布的正式版**。

- `framework/common.props` 在构建时自动读取 `VERSION` 作为 `VersionPrefix`，所有 `Leistd.*` 包同步该版本（无外部工具依赖）。
- 模板 `template/backend/Directory.Build.props` 的 `<LeistdFrameworkVersion>` 是字面值副本（生成项目需自包含），由发布流水线 `release.yml` 在发版时回写。

## 0.14.0 多租户（含破坏性变更）

新增 `multi-tenancy` 组件家族并贯通既有组件，详细用法见[多租户](../../framework/docs/components/multi-tenancy.md)；方案与决策记录见 [`docs/plans/2026-08-12-multi-tenancy.md`](../plans/2026-08-12-multi-tenancy.md)。

**新增包**

- `Leistd.MultiTenancy.Core`：`ICurrentTenant`（AsyncLocal 环境上下文）、`IMultiTenant` 标记接口、`MultiTenancySides`、解析链抽象、`ITenantStore` / `InMemoryTenantStore`、租户管理契约 `ITenantManager` / `TenantConfiguration` / `TenantPage`、租户异常（映射 404/403/409）。应用层只依赖本包即可完成租户管理。
- `Leistd.MultiTenancy.AspNetCore`：`UseMultiTenancy()` 中间件与默认解析链（Claim 定案 → 子域名 → `X-Tenant-Id` 头 → `tenant` 查询串）。子域名解析由 `MultiTenancyOptions.DomainFormat`（形如 `{0}.example.com`）开关，未配置时跳过；它排在头之前，因为子域名部署下域名是权威。
- `Leistd.MultiTenancy.EntityFrameworkCore`：`TenantRecord` 注册表、`EfCoreTenantStore`（直接读库，**不缓存**）、`EfCoreTenantManager`。**不含落值组件**——`TenantId` 的落值是 `BaseDbContext` 的职责（见下）。存储不缓存是刻意的：它的返回值带 `IsActive`，中间件据此放行——访问控制状态不能依赖尽力而为的缓存失效，否则停用/删除会出现"已提交但仍放行"的窗口，且删除之后无法补救（租户已软删，按 Id 找不到，重试失效走不到）。代价是每请求一次索引查找，与 `ActiveUserRequirement` 的用户判活同型。`CreateAsync` 的 `isActive` **无默认值**：创建后还要播种数据的场景必须传 `false`，否则匿名端点能进入尚无管理员的半成品租户。

**`Leistd.Ddd.Infrastructure`（破坏性）**

- `BaseDbContext.OnModelCreating` 改为 **sealed**，派生类改写新增的 `protected virtual void ConfigureModel(ModelBuilder)`（不再调用 `base`）。全局过滤器由基类在该方法之后套用——此前过滤器先于派生配置执行，导致经 `ApplyConfiguration` 才进入模型且无 `DbSet` 声明的实体（各组件的版本表）完全没有软删除与租户过滤器，是静默的隔离缺口。

- `ApplyGlobalFilters<TInterface>` 增加必填 `filterName` 首参，改用 EF 10 命名查询过滤器——软删除与租户过滤器在同一实体上 AND 叠加，此前二次调用会静默覆盖前一个过滤器。直接调用方需补过滤器名。
- `BaseDbContext` 新增租户全局过滤器（`MultiTenantFilterName`）；实体不实现 `IMultiTenant` 时无影响。
- **运行时语义修复**：新增实体的**环境值统一在"进入跟踪时"落定**——`BaseDbContext` 订阅 `ChangeTracker.Tracked` 与 `ChangeTracker.StateChanged`，对 `Added` 实体落 `TenantId` 与创建审计（`CreationTime` / `CreatorId`，经 `IAuditPropertySetter`）。

  此前两者都在保存时落值（分别由 `MultiTenantSaveChangesInterceptor` 与 `AuditSaveChangesInterceptor` 处理）。仓储在工作单元内不立即保存，新增与保存之间可以跨越 `ICurrentTenant.Change` / `ICurrentPrincipalAccessor.Change` 的边界：租户值会把数据静默落成宿主行（该租户看不见、宿主管理员看得见），`CreatorId` 会落成外层主体，均无任何报错。且在保存前 `CreatorId` 一直是 null，保存前读它的代码（领域事件、校验、导出）看到的都是空。与 Volo.ABP 的 `AbpDbContext.ChangeTracker_Tracked` → `SetCreationAuditProperties` 时机一致。

  **修改与删除审计仍在 `SavingChanges`**（`AuditSaveChangesInterceptor`）——`Modified` / `Deleted` 是状态迁移的结果，跟踪事件抓不到。

  不覆盖既有数据的三层护栏：`FromQuery` 跳过、状态必须是 `Added`、值已有则不动。

  **要求**：DbContext 必须继承 `BaseDbContext` **且**选用接收 `IServiceProvider` 的构造函数重载。只挂拦截器而不满足这两条时，创建审计与租户值静默不填充。

**`Leistd.MultiTenancy.EntityFrameworkCore`（破坏性）**

- **删除 `MultiTenantSaveChangesInterceptor`**，`AddMultiTenancyEfCore` 不再注册它，宿主也不需再 `AddInterceptors` 挂载它。落值移到 `BaseDbContext` 之后，它在正常路径上已是死代码（值已落定，保存时被 null 判断跳过），在边缘路径上则方向相反地有害：宿主上下文新增（`TenantId` 合法为 null）、提交却发生在 `Change(tenantId)` 作用域内时，它会把宿主数据盖成租户数据。调用方只需删掉挂载语句。

**`Leistd.Auditing.EntityFrameworkCore`（破坏性）**

- `AuditSaveChangesInterceptor` **不再处理 `Added` 状态**。仅挂载本拦截器、但 DbContext 不继承 `BaseDbContext` 的项目会失去创建审计填充——继承 `BaseDbContext` 并传入 `IServiceProvider` 即恢复。
- **运行时语义修复**：`EfCoreRepository.GetByIdAsync` 不再走 `FindAsync`（它绕过全局查询过滤器）——此前按 Id 能取出软删除行，多租户下将构成跨租户水平越权。依赖旧行为读取已删数据的调用方，改用 `IDataFilter.Disable<ISoftDelete>()` 显式表达。

**`Leistd.Security.Core`（破坏性）**

- `ICurrentUser` 新增 `Guid? TenantId`（读 `tenant_id` claim；常量 `CustomClaimTypes.TenantId`）。**自定义实现必须补该成员**，否则编译失败。

**`Leistd.Authorization.*`（破坏性）**

- 权限定义新增多租户侧别：`GetOrAddGroup` / `AddPermission` / `AddChild` 增加可选 `side` 参数（组默认 `Both`，权限继承组、子权限继承父）；`IPermissionGroupDefinition` / `IPermissionDefinition` 新增 `Side` 属性——自定义实现需补齐。
- 检查器判定顺序追加**侧别硬边界**：定义侧别与当前多租户上下文不匹配一律拒绝，先于授予读取与超管旁路；未注册 `ICurrentTenant` 的宿主视为 Host 侧（仅租户侧专属权限被拒，存量项目无感）。
- `PermissionGrantRecord` / `AuthorizationRevisionRecord`（及 Resource 家族对应两表）实现 `IMultiTenant`：新增可空 `TenantId` 列，授予与版本按租户分区；唯一索引重构为宿主行（`IS NULL` 过滤）与租户行（`IS NOT NULL` 过滤）成对的带过滤唯一索引——可空列直接进唯一索引时 NULL 互不相等，宿主行会失去唯一性兜底。**需要一次 EF 迁移**；存量行 `TenantId` 为 NULL 即宿主语义，行为不变。

**`Leistd.ServiceClient.*`**

- 出站管道追加租户头注入（`X-Tenant-Id`，来源 `ICurrentTenant`，`UserContextForwardingOptions.ForwardTenantId` 默认开、独立于用户头开关；宿主未注册 `ICurrentTenant` 时直通）。
- 被调方受信恢复扩展到租户：`ServiceUserContextOptions.TenantIdHeader`（默认 `X-Tenant-Id`）恢复为 `tenant_id` claim，且**独立于用户头**（仅有租户上下文的后台任务调用也恢复）；不受信来源的租户头不剥离——解析链主体优先级已使其无害，匿名登录的租户选择依赖它。
- 受信判定使用 `ClientSubject` 唯一契约：`sub == "client:" + client_id`，与模板 OpenIddict 签发端保持一致，并从结构上隔离机器与自然人主体。

**依赖变化**：CPM 新增 `Microsoft.Extensions.Caching.Abstractions` / `Microsoft.Extensions.Caching.Memory`（10.0.10）；`Leistd.MultiTenancy.EntityFrameworkCore` 引用 `Microsoft.EntityFrameworkCore.Relational`（配置部分唯一索引，与 `Leistd.Authorization.EntityFrameworkCore` 一致）；`Leistd.Authorization.Core`、`Leistd.ServiceClient.Core`、`Leistd.Ddd.Infrastructure` 新增对 `Leistd.MultiTenancy.Core` 的依赖。

## 0.13.0 授权体系最终态（破坏性变更）

本次一并完成，不保留过渡重载与兼容分支。升级需要一次代码调整加一次 EF 迁移。

**`Leistd.Authorization.Core`**

- `IPermissionGrantStore` 重构为三个读方法：`GetGrantsAsync(providerName, providerKey)`、`GetGrantsAsync(providerName, providerKeys)`（批量，供列表页取每个主体的授予数，避免按行 N+1）与 `GetGrantsForSubjectAsync(userId, roleIds)`。原有 `IsGrantedToUserAsync` / `IsGrantedToRoleAsync` / `IsGrantedToAnyRoleAsync` / `IsGrantedToUserOrRolesAsync` / `GetGrantedPermissionsFor*Async` 全部移除——它们互为退化形式，且逐权限查询拿不到主体的完整授予集合与版本号。**自定义 Store 实现必须补上批量重载**，否则编译失败。
- `IPermissionGrantManager` 改为按 `(providerName, providerKey)` 的通用签名：`GrantAsync` / `RevokeAsync` / `ReplaceGrantsAsync`。原有 `GrantToUserAsync` / `GrantToRoleAsync` / `RevokeFromUserAsync` / `RevokeFromRoleAsync` 与两个转发读方法移除，读职责归 Store。
- 新增 `PermissionGrantSet`、`SubjectPermissionGrants`、`PermissionGrantConcurrencyException`、`UndefinedPermissionException`。
- **授予收敛为两态（纯加法）**：移除 `PermissionGrantEffect` 与 `PermissionGrant`，授予集合退化为权限名列表；多来源之间取并集，不再有"显式拒绝"。要收回能力请调整角色构成——单个来源做减法会让有效权限不可组合，排查"他为什么没权限"必须遍历全部来源。`GrantAsync` 去掉 `effect` 参数，`ReplaceGrantsAsync` 改收 `IReadOnlyCollection<string>`。
- `SubjectPermissionGrants.GetEffectiveEffects(definitions)` 改为 `GetGrantedNames()`，返回各来源的权限名并集。
- 授予未定义/已禁用权限时抛 `UndefinedPermissionException`（此前是 `ArgumentException`，会被全局处理器归一化成 500）。宿主需把它映射为 HTTP 400。
- `IPermissionDefinitionContext.AddPermission` 移除：每个权限都必须归属于某个组，游离权限无法被权限管理界面表达。权限名改为**全局唯一**，重复注册在启动阶段抛异常。
- `IPermissionDefinitionManager` 新增 `GetGroups()`、`IsEffectivelyEnabled(name)`、`GetAncestorNames(name)`、`GetDescendantNames(name)`。
- `IPermissionChecker` 注册生命周期从 Transient 改为 **Scoped**；调用签名不变，但同一作用域内只解析一次主体、只读取一次授予。
- **运行时语义变更**：权限未定义或未启用一律拒绝（此前不校验定义，`IsEnabled` 形同虚设）。若此前依赖"未定义权限也能通过 Checker"，需要补齐定义。

**`Leistd.Authorization.AspNetCore`**

- 策略名支持用 `|` 连接多个权限表示「任一满足」，如 `[Authorize(Policy = "App.Permissions|App.Roles.ManagePermissions")]`；仅当每一段都是已定义权限时才按权限策略解析。分隔符常量为 `PermissionPolicyNames.AnyOfSeparator`（定义在 `Leistd.Authorization.Core`），**权限名自身不得包含它**，否则在定义注册阶段抛 `InvalidOperationException`。
- **策略解析顺序变更**：显式注册的同名策略优先于动态权限策略。此前动态策略会无条件覆盖宿主注册的同名策略（与文档描述不符），宿主若为某权限名注册过更严格的策略，升级后该策略才真正生效。
- `PermissionRequirement.PermissionName` 改为 `PermissionNames`（`IReadOnlyList<string>`）。直接构造该类型或自定义 Handler 的调用方需要调整。

**`Leistd.Authorization.EntityFrameworkCore`**

- `PermissionGrantRecord` 移除 `ForUser` / `ForRole` 静态工厂（它们会绕过写时归一化）；**不含 `Effect` 列**，一行即一次授予。
- 新增 `AuthorizationRevisionRecord` 表，`ConfigureAuthorization()` 会一并映射，缺失会导致批量替换无法做乐观并发校验。
- 需要一次 EF 迁移：新建授权版本表。唯一索引保持 `(PermissionName, ProviderName, ProviderKey)` 不变。既有数据若曾写入过"拒绝"语义的行，升级前需自行清理——两态模型下这些行会被当作授予。
- 写入行为变更：授予子权限会补齐祖先，撤销父权限会级联清理子孙。既有的扁平授予在下一次经由 Manager 写入时被归一化。
- `Leistd.Authorization.Resource` 保留显式拒绝（`ResourceGrantEffect`，本组件自有）：资源共享中"分享给一个部门、排除其中某人"没有等价替代写法，而功能权限的减法可以靠拆分角色解决，两者取舍依据不同。
- `AuthorizationRevisionRecord.Version` 是并发令牌（`IsConcurrencyToken()`，列类型不变，无需额外迁移）。并发写入中落败方得到 `PermissionGrantConcurrencyException` 而不是静默覆盖；首次写入的竞争由唯一索引兜住，同样映射为该异常。宿主需把它映射为 HTTP 409。
- `PermissionGrantConcurrencyException.ActualRevision` 取自存储的最新值（此前回填的是本次写入前读到的旧值，首写冲突时恒为 0）。

**`Leistd.Lock.Core` / `Leistd.Lock.Redis`**

- `ILockHandle` 新增必需成员 `LockLost`（`CancellationToken`）：持锁资格失效时被取消。**自定义实现必须提供该成员**——无租约的实现（进程内互斥）返回 `CancellationToken.None` 即可；基于租约的实现应在续期失败时取消它。
- `ILock.UnlockAsync(key)` **移除且不提供替代**：它不校验持有者，而"按 key 删锁"的后置条件与锁的核心不变量冲突——key 没了不等于上一个执行者停了。手动释放请用句柄（`await handle.DisposeAsync()`，带持有者校验）；持有者卡死时终止该实例并等待租约到期，那是唯一能真正让它停下来的手段。确需直接删 Redis 键的场景，自行注入 `IConnectionMultiplexer` 处理。
- Redis 句柄按租约三分之一周期自动续期（续期时校验 token，不会误续他人的锁）；续期失败或续期通道异常时取消 `LockLost`，且释放时不再删除 key。长临界区（数据迁移、批量初始化）应把该令牌并入自己的 `CancellationToken`。

**`Leistd.Authorization.Resource.*`**

- `IResourceGrantStore.GetGrantsAsync` 返回类型由 `IReadOnlyList<ResourceGrant>` 改为 `ResourceGrantSet`（授予 + 版本）。
- `IResourceGrantManager.ReplaceGrantsAsync` 新增 `expectedRevision` 参数并返回写入后的版本；冲突抛 `ResourceGrantConcurrencyException`，宿主需映射为 HTTP 409。
- 写入未定义的 `ResourceGrantEffect` 抛 `InvalidResourceGrantEffectException`；判定端与集合查询改为只认明确的 `Granted`，其余一律拒绝。
- 需要一次 EF 迁移：新增 `ResourceAuthorizationRevisions` 表与 `Effect` 的检查约束，迁移步骤与存量脏数据的处理见[资源实例授权](../../framework/docs/components/authorization-resource.md)。

**新增包**

- `Leistd.Authorization.Resource.Core` / `Leistd.Authorization.Resource.EntityFrameworkCore`：资源实例授权（可选）。
- `Leistd.Authorization.DataScope.Core`：数据范围（可选）。

三者互不依赖，按需引用；不引用即完全不产生模型与运行时成本。

## 0.13.0 新增服务间调用客户端（service-client）

纯新增组件家族，无破坏性变更：

- `Leistd.ServiceClient.Core`：强类型客户端注册（`AddServiceClient`）与标准管道（调用日志、TraceId 透传、`X-User-*` 用户头注入）、统一响应解包（`ReadResultAsync`）、`ServiceClientException` / `RemoteServiceException`。
- `Leistd.ServiceClient.OAuth`：OAuth2 client credentials 令牌获取/缓存/并发单飞/401 自愈（`AddClientCredentials`）。
- `Leistd.ServiceClient.AspNetCore`：被调方用户上下文恢复中间件（`UseServiceUserContext`），仅采信已认证服务客户端（`sub` 为 `ClientSubject` 契约形态、且持有委托 scope）携带的用户头，不受信一律剥离。
- `Leistd.ServiceClient.Refit`：Refit 接口式客户端注册（`AddRefitServiceClient`），与手写路径共享标准管道与 `RemoteServiceException` 错误契约；自带 `Refit.Reflection` 承接无法内联源生成的方法形态。

依赖基线变化：`Microsoft.Extensions.*` 统一从 10.0.8 升至 **10.0.10**（Refit 15 要求 `Microsoft.Extensions.Http >= 10.0.10`），随包依赖下限同步抬升，消费方需要 .NET SDK 能解析 10.0.10 的 `Microsoft.Extensions.*` 包。

**`Leistd.Security.Core`** 新增 `ClientSubject`（机器主体 `sub` 契约，`client:<client_id>`）：认证服务签发 client credentials 令牌时必须用 `ClientSubject.Format(clientId)` 构造 `sub`，服务间调用的用户上下文恢复以此为信任判据。这把机器主体与自然人主体（`sub` 是用户 GUID）隔离在不可碰撞的命名空间——否则任意指定的 `client_id` 可取某个真实用户 Id，使机器令牌被解析为该用户。自建认证端的宿主需同步该形态，否则服务间用户上下文不会恢复。

用法与信任边界见[服务间调用客户端](../../framework/docs/components/service-client.md)；方案与决策记录见 [`docs/plans/2026-08-11-service-invocation-sdk.md`](../plans/2026-08-11-service-invocation-sdk.md)。

## 提交规范决定版本递增（Conventional Commits）

发版时流水线（`release.yml`）分析"自上个 `v*` tag 以来"的提交信息，算出下一个版本（默认"优先最小版本"）：

| 提交 | 递增 | 例 |
| --- | --- | --- |
| 普通提交 / `fix:` | Patch（默认） | `fix: 修复锁超时` → 0.8.0 → 0.8.1 |
| `feat:` / `feat(scope):` | Minor | `feat: 新增 Redis 锁` → 0.8.x → 0.9.0 |
| `feat!:` / 任意类型带 `!` / 含 `BREAKING CHANGE` | Major | → 1.0.0 |

> **提交信息务必遵循 [Conventional Commits](https://www.conventionalcommits.org/)** —— 它直接决定版本如何递增。

## 分支 → 包类型

| 分支 / 触发 | 版本形态 | 发布目标 | 工作流 |
| --- | --- | --- | --- |
| push `main` | `x.y.z`（正式，自动递增） | nuget.org | `release.yml`（stable 通道） |
| push `develop` | `x.y.z-beta.<N>` | nuget.org（预发布） | `release.yml`（beta 通道） |
| 每工作日定时（develop） | `x.y.z-preview.<yyyyMMdd>` | GitHub Packages（内部） | `release.yml`（nightly 通道） |

> 三个通道由**单个** `release.yml` 内部按 `github.ref` / `github.event_name` 自动判定。

> 预发布后缀用**点分数字**（`-beta.12`、`-preview.20260623`），保证 NuGet 数值排序正确。

## 正式版发布（全自动）

**push 到 `main` 即自动发布**，无需手动打 tag：

1. 按提交推算新正式版本；
2. 回写 `VERSION` + 同步模板，提交 `chore: 发布 vX.Y.Z [skip ci]`；
3. 打 tag `vX.Y.Z`；
4. 打包 → 经 Trusted Publishing 推 nuget.org；
5. 创建 GitHub Release（自动生成 release notes）。

机制要点：
- 触发发版的变更：`VERSION`、**`framework/` 源码**（框架内非 docs 的 `.md` 除外）、或 **`framework/docs/` 组件文档**（文档随包分发，故文档更新也发一版送达）。
- **不**触发 stable 正式版：`docs/framework/`、`template/` 与仓库根的 `*.md`（内部开发规范、模板文档、仓库元文档），避免非交付内容改动误发。develop 分支仍按 beta 通道策略执行。
- 回写提交带 `[skip ci]` 且过滤 `github-actions[bot]`，避免死循环。
- ⚠️ NuGet 包不可删（只能 unlist）。框架源码每次有效变更都会产出一个正式版，请把控合入 main 的节奏。

## 鉴权

- **nuget.org**（stable / beta）：Trusted Publishing（OIDC，免长期 API Key）。需在 nuget.org 配置信任策略（仓库 + 工作流文件名 `release.yml`），并设 `NUGET_USER` secret。
- **GitHub Packages**（nightly）：用内置 `GITHUB_TOKEN`，无需额外配置。

### 引用 nightly 包（内部测试）

GitHub Packages 需认证拉取，即使公开仓库：

```bash
dotnet nuget add source "https://nuget.pkg.github.com/zengqinglei/index.json" --name leistd-nightly --username <你的GitHub用户名> --password <PAT，需 read:packages> --store-password-in-clear-text

dotnet add package Leistd.Core --prerelease
```

## 本地手动操作（不发布）

```bash
# 打包（用 VERSION 文件的版本，固定产出到本地 feed）
dotnet pack framework/Leistd.Framework.slnx -c Release -o .tmp/local-feed

# 想发布更高的基准版本：直接编辑 VERSION 文件即可（CI 在此基础上按提交递增）
```

> `.tmp/local-feed` 仅用于本地开发并由 `.gitignore` 忽略；CI 发布继续使用 `framework/artifacts`。版本推算、模板同步、打包发布逻辑全部内联于 `.github/workflows/release.yml`，发布由 push 自动触发，本地通常无需手动介入。

## 注意

- CPM 下第三方包版本集中在 `framework/Directory.Packages.props`；升级第三方依赖按提交规范评估影响。
- monorepo 统一版本：所有可发布框架包共享同一版本，要么全发要么全不发；`--skip-duplicate` 保证重跑幂等。
- 整个机制零外部版本工具（纯 git + PowerShell + MSBuild 读文件），与团队其它项目（如 ai-relay）的 VERSION 文件范式一致。
