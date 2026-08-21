# Leistd 多租户生产基线：增量实施计划

> 本文只描述 Leistd Framework 与 `fullstack-app` Template 相对当前代码的变化。现状已经满足的能力不进入任务树，也不为制造改动而修改源码。

**目标：** 支持同一套业务代码同时服务“共享默认数据库”和“租户覆盖数据库”两种数据放置方式，并让 Identity 宿主、Resource 宿主、迁移入口及各服务自有前端形成可直接用于生产项目的基线。

**范围：** `framework/components`、`framework/ddd-struct` 的必要回归验证、`template`、相关仓库文档与验证脚本。

**执行边界：** 不修改 CRM v2 业务项目；不修改历史方案；不提交 Git；不保留旧 Template 参数或旧运行语义的兼容层。

---

## 1. 直接复用的现状

现有 `ICurrentTenant`、`IMultiTenant`、EF 全局租户过滤、保存时 `TenantId` 写入、Identity 侧 `ITenantStore`/`IsActive` 校验、ServiceClient 租户传播及关键 HTTP 诊断继续直接使用。它们不需要新增平行抽象，也不列入本计划的修改文件树。

DDD 基座仍负责实体、仓储和 `BaseDbContext` 的通用领域基础设施；本轮不把连接路由、Identity 客户端或数据库迁移编排下沉到 DDD。

## 2. 需要发生的变化

### 2.1 Framework：可信 Resource 租户上下文

Identity 宿主继续使用现有多租户中间件解析租户并校验 Store。Resource 宿主新增另一条明确的组合入口：Bearer 认证成功后，只接受已验证主体中唯一且格式合法的 `tenant_id`，再通过现有 `ICurrentTenant.Change()` 建立作用域。

新入口必须忽略 Header、QueryString、Domain 等未认证线索，且不查询 `ITenantStore`。租户停用的收敛边界由 Identity 停止签发/刷新和 Access Token 的短生命周期保证，不对外承诺 Resource 立即撤销。

### 2.2 Framework：连接配置持久化与解析

租户连接配置是业务无关的多租户能力，归 `Leistd.MultiTenancy.*`，不由 Template 重建领域实体。现有 `TenantConfiguration` 继续只承载租户身份和启停状态；连接配置使用独立、受限的模型：

```csharp
public sealed class TenantConnectionConfiguration
{
    public required Guid TenantId { get; init; }
    public required TenantDatabaseMode DatabaseMode { get; init; }
    public string? RuntimeSecretReference { get; init; }
    public string? MigrationSecretReference { get; init; }
    public long Version { get; init; }
}

public enum TenantDatabaseMode
{
    SharedDatabase,
    DedicatedDatabase
}
```

`ITenantConnectionConfigurationStore` 提供受限读取，`ITenantConnectionConfigurationManager` 是唯一写入口，EF 实现将记录存放在宿主/Identity Control DB。记录不实现 `IMultiTenant`，也不随租户连接路由。

存储不变量：

- 每个有效租户必须且只能有一条连接配置；创建租户与创建连接配置必须在同一 Control DB UoW 内完成。
- `SharedDatabase` 不保存租户覆盖 Secret，运行时使用各服务部署注入的 `ConnectionStrings:Default`。
- `DedicatedDatabase` 必须保存 Runtime 与 Migration 两个 Secret Reference；前者供 API/后台 DML，后者仅供 DbMigrator DDL。
- Framework 数据库只保存 Secret Reference，不保存明文连接字符串；真实值由部署选定的 Secret Provider 保存。
- 配置缺失、模式与 Secret Reference 不匹配、租户不存在时必须失败，不能把异常状态解释为共享默认库。

DbContext 侧公共解析 API 仍保持最小，只返回最终连接字符串，不向调用方暴露存储模型或解析状态：

```csharp
public interface ITenantConnectionStringResolver
{
    Task<string> ResolveAsync(
        string connectionStringName,
        CancellationToken cancellationToken = default);
}
```

解析语义：

- Host 上下文返回宿主配置的命名连接。
- 权威来源返回明确的 `SharedDatabase` 配置时，返回宿主默认连接。
- 权威来源返回合法的 `DedicatedDatabase` 配置时，解析 Runtime Secret 并返回覆盖连接。
- Identity 不可用、超时、租户不存在、Secret 无效或宿主默认连接缺失时抛出异常，DbContext 不得创建。
- 连接字符串、Secret Reference、Token 和 Cookie 不得进入异常消息、结构化日志或 `ToString()`。

这与 ABP 将租户连接配置放在 Tenant Management 能力、并由 `IConnectionStringResolver -> Task<string>` 异步解析的方向一致，但保留一项更严格的安全约束：Resource 请求链不运行 `ITenantStore` 校验，因此“未知租户”不能像 ABP 的部分默认实现那样回落到默认数据库。只有权威配置明确声明 `SharedDatabase` 时才允许使用默认连接。

### 2.3 Framework：动态连接与 UoW

当前 UoW 已支持单 DbContext、单连接上的保存、提交和回滚，这部分不重做。需要修复的是动态连接接入后的两个真实缺口：

1. `DbContextProvider.GetDbContextAsync()` 在同步工厂中使用 `.Result` 创建事务，存在 sync-over-async。
2. `UnitOfWork` 只保存一个未命名的 Database API 和 Transaction API，第二种 DbContext 可能发生泛型错误转换，也无法判定租户或物理连接是否在 UoW 中途被切换。

终局约束：

- 连接解析必须在首次创建 DbContext 之前异步完成。
- 使用内部 `DbContextCreationContext` 将已解析连接传给 EF 的同步 Options 工厂；不增加 HTTP 中间件，不把远程调用放进 Options 回调。
- Database API 与 Transaction API 改为按稳定 key 管理，保存、提交、回滚和释放覆盖全部已登记实例。
- 一个活动 UoW 在第一次取 DbContext 时绑定一个 `TenantId` 和一个物理连接目标；后续切换租户或连接立即拒绝。
- 同一 UoW 可以使用多个 DbContext 类型，但只能连接同一物理目标，并按关系型 Provider 的能力共享同一事务。
- 跨租户后台任务必须“每租户一个新 UoW”；不承诺跨库事务或分布式原子性。

连接解析属于多租户组件，UoW Core 只感知 key；租户和连接绑定检查、异步创建桥接位于 EF Core 实现层。DDD 不增加新抽象。

### 2.4 Template：Identity / Resource 宿主职责

Template 增加 `ServiceRole=Identity|Resource`，直接替换当前把本地授权服务器能力与通用身份能力混在一起的条件组合：

| 角色 | 后端职责 | 自有前端职责 |
| --- | --- | --- |
| `Identity` | OIDC Server、登录凭据、用户身份、租户、OIDC 应用；通过 Framework Manager 管理租户连接配置 | 登录/注册、租户、应用、连接配置及 Identity 自身管理页面 |
| `Resource` | 远端 Token Validation、本服务 Membership、角色、权限和业务 API | 本服务业务页面及本服务角色/权限管理页面 |

每个服务继续保持当前 Template 的 `backend/ + frontend/` 完整结构，可以独立成仓和独立发布。Template 不生成中央 `admin-web`，也不新增 BackendOnly、FrontendOnly、Worker 等当前没有已确认需求的形态。

Framework 提供连接配置模型、Store/Manager、EF 持久化与最终解析契约。Template 的 Identity 模式提供管理用例、内部 API/SDK 和权限；生成后的 Resource 项目使用真实 `Identity.Client` 与部署选定的 Secret Provider，在 Infrastructure 实现最终解析器。Framework 不引用 `Identity.Client` 或任何云厂商 Secret SDK。

### 2.5 Template：固定服务 schema 与独立 DbMigrator

两种数据放置方式只有连接目标不同，数据模型保持一致：

| 数据放置 | 连接目标 | 隔离规则 |
| --- | --- | --- |
| 不分库 | 所有租户进入服务的默认数据库 | 每服务固定 schema；业务表以 `TenantId` 隔离 |
| 分库 | 租户配置一条覆盖连接；各服务为该租户共用该连接 | 各服务仍使用自己的固定 schema；仍保留 `TenantId` 纵深防御 |

Template 必须同时配置 `HasDefaultSchema(serviceSchema)` 与 `MigrationsHistoryTable("__EFMigrationsHistory", serviceSchema)`。不支持“每租户每服务一条连接”或“每租户一个 schema”。

每个含持久化后端的服务新增独立 `CompanyName.ProjectName.DbMigrator`：

- API 不再在启动时调用 `MigrateAsync()` 或 `EnsureCreatedAsync()`。
- DbMigrator 迁移默认目标，再从 Identity 获取覆盖配置并解析 Migration Secret，按物理目标去重后迁移本服务 schema。
- DbMigrator 是单服务一次性部署 Job，不是 Migration Fleet；不增加控制库、批次编排或 checkpoint。
- 任一必需目标无法枚举、连接或迁移时以非零退出码结束并阻止该服务发布。
- 重复运行必须幂等；API 使用 DML 身份，DbMigrator 使用独立 DDL 身份。

## 3. 增量文件树与职责

图例：`[新增]` 创建文件；`[修改]` 修改现有文件；`[验证]` 仅新增或调整针对变化点的测试。树中不列无需改动的文件。

### 3.1 Framework

```text
framework/
├── components/multi-tenancy/
│   ├── Leistd.MultiTenancy.Core/
│   │   └── ConnectionStrings/
│   │   │   ├── ITenantConnectionStringResolver.cs           [新增] 异步返回最终连接字符串
│   │   │   ├── TenantConnectionStringNameAttribute.cs      [新增] DbContext 声明命名连接
│   │   │   ├── ITenantConnectionConfigurationStore.cs       [新增] 受限读取租户连接配置
│   │   │   ├── ITenantConnectionConfigurationManager.cs     [新增] 连接配置唯一写入口
│   │   │   ├── TenantConnectionConfiguration.cs             [新增] 模式、Secret Reference 与版本
│   │   │   └── TenantDatabaseMode.cs                        [新增] SharedDatabase/DedicatedDatabase
│   ├── Leistd.MultiTenancy.EntityFrameworkCore/
│   │   ├── Entities/TenantConnectionRecord.cs               [新增] Control DB 一租户一记录
│   │   ├── EntityConfigurations/TenantConnectionRecordConfiguration.cs
│   │   │                                                      [新增] 一对一、必填和模式约束
│   │   ├── Stores/EfCoreTenantConnectionConfigurationStore.cs [新增] 不经租户路由的读取实现
│   │   ├── Managers/EfCoreTenantConnectionConfigurationManager.cs
│   │   │                                                      [新增] 校验不变量并维护版本
│   │   └── DependencyInjection.cs                            [修改] 注册 Store/Manager 并映射实体
│   └── Leistd.MultiTenancy.AspNetCore/
│       ├── AuthenticatedTenantContextMiddleware.cs           [新增] 从已验证 tenant_id 建立当前租户
│       └── DependencyInjection.cs                            [修改] 暴露 UseAuthenticatedTenantContext()
│
├── components/unit-of-work/
│   ├── Leistd.UnitOfWork.Core/
│   │   ├── Database/IDatabaseApiContainer.cs                 [修改] Database API 改为按 key 查找/登记
│   │   ├── Database/ITransactionApiContainer.cs              [修改] Transaction API 改为按 key 查找/登记
│   │   └── Uow/
│   │       ├── UnitOfWork.cs                                 [修改] 管理并完整处理多个 keyed API
│   │       └── ChildUnitOfWork.cs                            [修改] 透传新的 keyed 容器契约
│   └── Leistd.UnitOfWork.EfCore/
│       ├── Database/DbContextCreationContext.cs              [新增] 异步解析结果到同步 EF Options 的内部桥接
│       ├── Database/UnitOfWorkConnectionBinding.cs           [新增] 固定 UoW 的租户与物理连接目标
│       ├── Database/DbContextProvider.cs                     [修改] 移除 sync-over-async 并按 key 复用 DbContext/事务
│       ├── DependencyInjection.cs                            [修改] 注册创建上下文与绑定服务
│       └── Leistd.UnitOfWork.EfCore.csproj                   [修改] 接入最小多租户解析契约
│
├── tests/
│   ├── Leistd.MultiTenancy.Tests/
│   │   ├── AuthenticatedTenantContextMiddlewareTests.cs      [新增] claim 信任边界与伪造输入反例
│   │   ├── TenantConnectionConfigurationManagerTests.cs      [新增] 模式、Secret 与版本不变量
│   │   └── TenantConnectionConfigurationStoreTests.cs        [新增] Control DB 固定读取与敏感数据边界
│   └── Leistd.UnitOfWork.Tests/
│       ├── Leistd.UnitOfWork.Tests.csproj                    [新增] UoW 独立测试工程
│       ├── UnitOfWorkDatabaseApiTests.cs                     [新增] keyed API 的提交/回滚/释放
│       ├── TenantBoundDbContextProviderTests.cs              [新增] 租户/连接绑定与切换拒绝
│       └── UnitOfWorkDynamicProxyTests.cs                    [新增] 声明式异步 UoW 拦截验证
│
├── Leistd.Framework.slnx                                    [修改] 纳入 UnitOfWork 测试工程
└── docs/
    ├── components/multi-tenancy.md                           [修改] 存储、解析、宿主组合与失败语义
    └── components/unit-of-work.md                            [修改] keyed API、单目标约束与后台用法
```

`framework/ddd-struct` 没有修改源码。动态连接对既有 `BaseDbContext` 的租户过滤和写入落值没有提出新契约。真实 PostgreSQL 的物理目标、schema、迁移和行隔离证据收敛在 Template E2E 脚本，避免 Framework 单元测试重复建立容器基础设施。

### 3.2 Template

```text
.
├── template/
│   ├── .template.config/template.json                        [修改] 增加 ServiceRole 并完整裁剪两类宿主资产
│   ├── backend/
│   │   ├── CompanyName.ProjectName.sln                       [修改] 纳入 DbMigrator
│   │   ├── src/
│   │   │   ├── CompanyName.ProjectName.Api/
│   │   │   │   ├── CompanyName.ProjectName.Api.csproj        [修改] Server/Validation 依赖按角色裁剪
│   │   │   │   ├── Program.cs                               [修改] 两类认证/租户管道、探针；移除启动 DDL
│   │   │   │   ├── IdentityControlDbContextFactory.cs         [新增] Control migration 设计时入口
│   │   │   │   ├── MyProjectDbContextFactory.cs             [修改] 设计时 schema 与迁移历史配置一致
│   │   │   │   ├── Controllers/{Auth,Connect,ExternalAuth}.cs [修改] 仅 Identity 生成协议与凭据入口
│   │   │   │   ├── Controllers/{Tenant,OpenApplication}.cs  [修改] 仅 Identity 生成控制面入口
│   │   │   │   ├── Controllers/TenantConnectionController.cs [新增] Identity 内部连接配置查询/管理 API
│   │   │   │   ├── Controllers/{Role,Permission,User}.cs    [修改] 拆分账户与本地授权职责
│   │   │   │   └── appsettings*.json                        [修改] Issuer、Audience、Identity 地址与连接配置
│   │   │   ├── CompanyName.ProjectName.Domain/
│   │   │   │   ├── Auth/**                                  [修改] 仅 Identity 保留登录凭据与外部身份
│   │   │   │   └── Users/**                                 [修改] Identity 账户/Resource 本服务成员的角色化模型
│   │   │   ├── CompanyName.ProjectName.Application/
│   │   │   │   ├── Auth/**                                  [修改] 仅 Identity 保留认证用例
│   │   │   │   ├── Tenants/**                               [修改] 仅 Identity 保留租户控制面用例
│   │   │   │   ├── Users/**                                 [修改] Identity 账户/Resource 本地成员用例按角色裁剪
│   │   │   │   └── TenantConnections/**                     [新增] Identity 调用 Framework Manager 的管理用例
│   │   │   ├── CompanyName.ProjectName.Infrastructure/
│   │   │   │   ├── DependencyInjection.cs                  [修改] 动态连接、固定 schema 与迁移历史
│   │   │   │   ├── Persistence/IdentityControlDbContext.cs [新增] 固定 Control DB 的租户/OIDC 模型
│   │   │   │   ├── Persistence/MyProjectDbContext.cs       [修改] 服务 schema 与角色对应的数据模型
│   │   │   │   ├── Persistence/Migrations/{Control,Identity,Resource}/**
│   │   │   │   │                                                  [新增] 按角色裁剪的可审查基线迁移
│   │   │   │   └── TenantConnections/**                     [新增] Identity 本地/Resource SDK + Secret 解析适配
│   │   │   ├── CompanyName.ProjectName.Client/
│   │   │   │   ├── IMyProjectClient.cs                      [修改] Identity 连接配置 SDK 契约
│   │   │   │   └── Dtos/TenantConnectionDto.cs             [新增] 只返回模式/Secret Reference/版本
│   │   │   └── CompanyName.ProjectName.DbMigrator/
│   │   │       ├── CompanyName.ProjectName.DbMigrator.csproj [新增] 独立一次性迁移进程
│   │   │       ├── Program.cs                               [新增] 配置、日志、退出码与 DDL 身份入口
│   │   │       └── DatabaseMigrationRunner.cs               [新增] Control/默认/去重覆盖/显式首迁目标
│   │   └── tests/CompanyName.ProjectName.IntegrationTests/
│   │       ├── AuthenticationModeTests.cs                   [新增] Identity/Resource 信任边界
│   │       ├── TenancyTests.cs                              [修改] 租户创建、配置不变量和 Secret 脱敏
│   │       ├── DbMigratorTests.cs                           [新增] 目标去重、幂等、失败退出与 API 无 DDL
│   │       ├── HealthEndpointTests.cs                       [修改] liveness/readiness 依赖语义
│   │       └── ProjectWebApplicationFactory.cs              [修改] 两类宿主的集成测试组合根
│   ├── frontend/src/
│   │   ├── environments/environment*.ts                     [修改] 单 Issuer、ClientId、Audience 与 API 地址
│   │   └── app/
│   │       ├── app.config.ts                                [修改] OIDC Authorization Code + PKCE
│   │       ├── app.routes.ts                                [修改] Identity/Resource 自有页面按角色裁剪
│   │       ├── core/services/auth-service.ts                [修改] 本地 Identity 与远端登录模式
│   │       ├── core/services/tenant-context-service.ts      [修改] token tenant_id 为认证后唯一权威值
│   │       ├── core/interceptors/tenant-interceptor.ts      [修改] 禁止本地线索覆盖已认证 tenant_id
│   │       └── features/platform/**                         [修改] 租户/应用与本服务授权页面按所有权裁剪
│   ├── Dockerfile                                           [修改] 增加 DbMigrator 发布目标
│   ├── deploy/docker-compose*.yml                           [修改] 迁移 Job、独立身份、启动顺序与探针
│   ├── README.md                                            [修改] 参数、生成职责和迁移入口
│   ├── backend/README.md                                    [修改] 后端宿主、连接和 DbMigrator 契约
│   ├── frontend/README.md                                   [修改] 每服务自有前端与 OIDC 配置
│   └── docs/
│       ├── deploy/README.md                                 [修改] DbMigrator 先行与 DDL/DML 身份
│       └── standards/{api,coding-backend,project-structure,testing}.md
│                                                               [修改] 新增行为对应的开发与验证规则

├── scripts/test-template-matrix.ps1                         [修改] 验证 Identity/Resource 两个完整 Fullstack 产物
├── scripts/test-template-postgresql-e2e.ps1                  [新增] 真实 PostgreSQL 闭环与 DDL/DML 身份验收
└── docs/template/development-guide.md                       [修改] 维护矩阵与失败诊断
```

## 4. 运行时契约

### 4.1 请求顺序

```csharp
app.UseAuthentication();

// Identity
app.UseMultiTenancy();

// Resource（与上面的 Identity 分支互斥）
app.UseAuthenticatedTenantContext();

app.UseAuthorization();
```

连接不在 HTTP 中间件中预解析。Repository/DbContext 第一次真正被请求时，`DbContextProvider` 异步调用解析器，因此 API、后台任务和 DbMigrator 不产生三套行为。

### 4.2 存储与解析链路

```text
Identity Control DB                        Secret Provider
TenantRecord 1 ── 1 TenantConnectionRecord    ├── Runtime Secret
                         │                     └── Migration Secret
                         ▼
                 Identity.Client
                         │ 仅返回模式、Secret Reference、Version
                         ▼
Resource Infrastructure + Secret Provider Adapter
                         │
                         ▼
             ITenantConnectionStringResolver
                         │ 仅返回最终连接字符串
                         ▼
                    DbContext
```

Identity 的创建租户用例必须在同一个 Control DB UoW 中写入 `TenantRecord` 和 `TenantConnectionRecord`；初始化完成前租户保持禁用。内部 API/SDK 不返回明文连接字符串，Runtime 与 Migration Secret 使用不同授权策略。

Identity 为这两条读路径登记两个独立 OIDC scope：`tenant-routing.read` 只读单租户 Runtime 路由，`tenant-migration.read` 只供 Resource DbMigrator 枚举 Migration 目标。Open Application 必须显式被授予对应 scope，不提供一个同时暴露两类 Secret Reference 的粗粒度入口。

### 4.3 可用性与缓存归属

Framework 与 Template 不实现通用 Identity 回源缓存。生成后的业务 Resource 适配器负责进程内有界缓存、同租户并发请求合并和带抖动重试；缓存 key 至少包含 `TenantId + connectionStringName`，不得持久化连接字符串。

业务项目应让缓存 TTL 不短于其 Access Token 有效期。liveness 只检查进程自身；readiness 可以在冷启动时验证必需依赖，但不得在每次探针中同步回源 Identity。冷缓存且 Identity 不可用时失败关闭，不能用未知或过期路由继续服务。

### 4.4 迁移边界

DbMigrator 只处理本服务 schema。它可以遍历默认目标和租户覆盖目标，但不负责跨服务发布顺序、跨服务回滚或数据库版本舰队管理。破坏性 schema 演进由各服务部署流程使用 expand/contract 约束处理，不在 Framework 中增加编排器。

新 Dedicated 物理目标在租户登记前通过 `ConnectionStrings:MigrationTarget` 显式预迁移。该模式只迁本服务 Business DbContext，不迁 Identity Control DB、不枚举已登记租户；常规发布仍使用默认目标 + Identity 中已登记 Dedicated 目标的去重迁移。这解开“先登记才能枚举，但租户创建又必须立即播种”的首次建库循环，不引入 Worker 或迁移编排平台。

## 5. 实施任务

### Task 1：租户连接配置与可信上下文

- [x] 先增加连接配置模式/Secret 不变量、Control DB 固定读取和 Resource claim 正反例测试。
- [x] 实现独立于 `TenantConfiguration` 的连接配置模型、Store/Manager 与 EF 持久化。
- [x] 实现 `UseAuthenticatedTenantContext()` 与只返回最终字符串的解析契约。
- [x] 验证缺失配置、无效 Secret、未知租户和 Identity 不可用全部失败关闭。
- [x] 回归 Identity 现有 Store/IsActive 行为，确认两条宿主路径互不混用。

### Task 2：修正 UoW/EF 动态连接生命周期

- [x] 建立独立 UnitOfWork 测试工程，并先复现 `.Result`、第二 DbContext 错误转换及租户切换复用问题。
- [x] 将 Database/Transaction API 改为 keyed 管理，补齐全部实例的保存、提交、回滚和释放。
- [x] 实现异步解析与 `DbContextCreationContext`，绑定单 UoW 的租户和物理连接目标。
- [x] 使用真实 PostgreSQL 验证单目标多 DbContext、事务提交/回滚和跨租户拒绝。

### Task 3：重构 Template 宿主职责

- [x] 以 `ServiceRole` 取代现有耦合条件，不保留旧参数兼容层。
- [x] Identity 只生成授权服务器和控制面；Resource 只生成远端验证、本地 Membership/角色/权限。
- [x] Identity 使用 Framework Manager 管理连接配置，并在创建租户的同一 UoW 内建立配置记录。
- [x] Identity.Client 只返回模式、Secret Reference 和版本；管理 API、日志与异常不返回连接字符串。
- [x] 两种角色均保留自己的完整前端，关闭分支不存在源码、引用、配置、路由或测试残留。

### Task 4：落地 schema 与 DbMigrator

- [x] 固定服务 schema 和独立迁移历史表，生成可审查基线 migration。
- [x] 新增 DbMigrator，删除 API 启动 DDL 与 `EnsureCreatedAsync()` 生产路径。
- [x] 使用独立 Migration Secret 对默认目标和去重后的覆盖目标执行幂等迁移，任一失败返回非零退出码。
- [x] 用两个生成服务验证共享一个数据库时 schema 与迁移历史互不干扰。

### Task 5：完成前端、部署和分发文档

- [x] Identity/Resource 前端分别完成登录方式、路由和管理页面裁剪。
- [x] 增加 DbMigrator 镜像目标、部署 Job、DDL/DML 身份和正确探针。
- [x] 只把已由生成结果证明成立的契约写入 Framework/Template 分发文档。

### Task 6：全链路验证

- [x] Framework 执行 Release build、全测试、pack、隔离消费与文档 API 漂移检查。
- [x] Template 至少生成 Identity Fullstack 与 Resource Fullstack，逐个执行后端 restore/build/test 和前端 lint/test/production build。
- [x] 运行模板矩阵与 Skill 校验，搜索旧参数、模板标记、默认密钥和被裁剪职责残留。

### 实施结果与验证证据

- Framework Release build 与全量测试通过；其中 MultiTenancy 109 项、UnitOfWork 10 项，49 个 `Leistd.*` NuGet 包隔离消费通过。
- Template 七个条件组合（Identity/Resource、Notifications、External Login、Localization）的后端测试、前端 lint、production build 和有头 Chrome 测试全部通过。
- 真实 PostgreSQL 闭环通过：package → generate → migrate → API → Shared/Dedicated 隔离，同时验证迁移幂等、DDL/DML 身份分离、Secret 缺失失败关闭与失败补偿。
- 真实生成的 Identity SPA 通过独立有头浏览器验收：登录、共享库租户创建/激活、用户管理和 OIDC 应用管理页均成功。验收发现并修复了 Control DbContext 下租户创建时间未落值问题，修复后页面显示有效 UTC 时间。

## 6. 验收标准

以下条件必须全部成立：

1. Framework 只有现有 `ICurrentTenant`；没有公开三态结果、解析结果 Accessor、连接解析中间件或业务 DTO。
2. Resource 只信已验证主体的唯一 `tenant_id`；Header、QueryString 和 Domain 不能改写认证后的租户。
3. 每个有效租户在 Control DB 中有且只有一条连接配置；模式和 Secret Reference 满足不变量，并与租户创建原子提交。
4. Framework 数据库、`TenantConfiguration`、Identity API/SDK、日志和异常均不包含明文连接字符串。
5. 权威查询失败、配置缺失或租户未知时 DbContext 不创建，也不会落入默认数据库。
6. 动态连接路径不存在 `.Result`、`.Wait()` 或 `GetAwaiter().GetResult()`；一个 UoW 不能中途切换租户或物理目标。
7. 现有单连接 UoW 行为不回归；多个 DbContext 在同一物理目标上的提交和回滚由真实 PostgreSQL 用例证明。
8. 两种数据放置使用同一实体模型和 `TenantId` 隔离；各服务拥有固定 schema 和独立迁移历史表。
9. API 与其运行身份不能执行 DDL；DbMigrator 只使用 Migration Secret，能幂等迁移全部目标并以退出码阻断失败发布。
10. Identity 与 Resource 生成物职责清晰且可独立构建；每个服务保留自己的前后端，不出现中央 `admin-web`。
11. Template 不生成无需求的 Worker、Migration Fleet、Kafka、Outbox/Inbox、Runtime Directory、本地租户投影或通用 HTTP Idempotency 组件。
12. 所有 Framework 与 Template 验证通过，实施改动保持未提交，除非用户之后明确授权提交。

## 7. 非目标

- 不为业务 Branding、注册流程、KYC、文件、邮件、短信或语音能力设计 Framework 抽象。
- 不解决跨租户查询、跨库事务、跨服务迁移编排或自动 schema 回滚。
- 不把连接配置加入现有 `TenantConfiguration`，不在 Framework 数据库中保存明文连接字符串。
- 不以“未来可能需要”为理由预建传输、通用缓存组件、Worker 或额外项目形态。

## 8. ABP 对照依据

实施时以 ABP Framework `10.6.0` 的下列设计作为机制对照，而不是逐类复制：

- [`IConnectionStringResolver`](https://github.com/abpframework/abp/blob/10.6.0/framework/src/Volo.Abp.Data/Volo/Abp/Data/IConnectionStringResolver.cs)：异步返回最终连接字符串。
- [`MultiTenantConnectionStringResolver`](https://github.com/abpframework/abp/blob/10.6.0/framework/src/Volo.Abp.MultiTenancy/Volo/Abp/MultiTenancy/MultiTenantConnectionStringResolver.cs)：宿主默认连接与租户覆盖连接的解析顺序。
- [`UnitOfWorkDbContextProvider`](https://github.com/abpframework/abp/blob/10.6.0/framework/src/Volo.Abp.EntityFrameworkCore/Volo/Abp/Uow/EntityFrameworkCore/UnitOfWorkDbContextProvider.cs)：异步解析与 UoW 中的 DbContext 获取。
- [`DbContextCreationContext`](https://github.com/abpframework/abp/blob/10.6.0/framework/src/Volo.Abp.EntityFrameworkCore/Volo/Abp/EntityFrameworkCore/DependencyInjection/DbContextCreationContext.cs)：将异步解析结果安全传给同步 Options 配置。
- [`UnitOfWork`](https://github.com/abpframework/abp/blob/10.6.0/framework/src/Volo.Abp.Uow/Volo/Abp/Uow/UnitOfWork.cs)：按 key 管理 Database API 与 Transaction API。

Leistd 将 ABP Tenant Management 中通用的租户连接配置职责收敛在现有 `Leistd.MultiTenancy.*` 家族，但不复制完整 Data/Tenant Management 模块，也不复制其默认回退行为。Template 只消费这些 Framework 能力，不再定义平行的 Tenant Data 领域模型。
