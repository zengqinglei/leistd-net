# Framework / Template 待调整内容：文件级任务分解

本文只回答**改哪些文件、每处职责是什么**。取舍依据见 [CRM V2 模板落地指南](./2026-08-20-crm-v2-program.md) 的 §5、§6，不在此重复；少数项（§2 的时机取舍、§3.3 的缺陷表现）依据只在本文，随条目就地给出。

优先级：`P0` 阻塞 CRM V2 开工 · `P1` 生产上线前必须 · `P2` 可捎带 · `P3` 待决策

> 状态基线：`252bbac`。§2 与 §3.5 是**已核实无待调整项**的记录，其余为待做项。

---

## 一、Framework：通用组件

### 1.1 按租户分库支持（P1）

租户可独立配置连接串，所有服务共用该租户的库，各服务仍在自己的 schema。**最关键的约束是租户注册表必须钉在宿主连接上**——解析租户要先读注册表，注册表若随业务数据搬进租户库就形成鸡生蛋。

```text
framework/components/multi-tenancy/
├── Leistd.MultiTenancy.Core/
│   ├── Store/
│   │   ├── TenantConfiguration.cs                    # 改：增 ConnectionString（可空，空=用默认库）
│   │   ├── ITenantManager.cs                         # 改：Create/Update 支持连接串维护
│   │   └── ITenantConnectionStringResolver.cs        # 新增：按 ICurrentTenant 解析连接串；
│   │                                                 #      抽象放 Core（不依赖 EF）
│   └── DependencyInjection.cs                        # 改：注册解析器默认实现
│
└── Leistd.MultiTenancy.EntityFrameworkCore/
    ├── Entities/TenantRecord.cs                      # 改：增连接串列
    ├── EntityConfigurations/TenantRecordConfiguration.cs  # 改：列长度约束；连接串不进索引
    ├── Stores/EfCoreTenantStore.cs                   # 改（核心）：不再复用业务 DbContext，
    │                                                 #   改为绑定"宿主连接"上下文，否则鸡生蛋
    ├── Managers/EfCoreTenantManager.cs               # 改：写入连接串；同宿主连接
    └── DependencyInjection.cs                        # 改：AddMultiTenancyEfCore 区分
                                                      #   宿主注册表上下文与业务上下文
```

配套测试与文档：

```text
framework/tests/Leistd.MultiTenancy.Tests/
├── TenantConnectionRoutingTests.cs                   # 新增：按租户路由到不同连接；
│                                                     #   空连接串回落默认库；
│                                                     #   **误指到别的租户库时过滤器仍然生效**
└── TenantStoreHostPinningTests.cs                    # 新增：注册表读取始终走宿主连接，
                                                      #   不受当前租户影响（鸡生蛋回归锁）

framework/docs/components/multi-tenancy.md            # 改：两种隔离模式、宿主连接约束、跨租户聚合的显式出口
docs/framework/versioning.md                          # 改：破坏性变更条目（Store 注册形态变化）
```

### 1.2 弃用的 AutoMapper 组件去留（P3，待决策）

现状是**已文档化的保留决定**（`framework/Directory.Packages.props` 注释：13.0.1 有高危 DoS，官方仅在商业授权版修复，新项目用 Mapster，NU1903 告警属预期）。按"不考虑兼容性"原则应当删除整个家族；保留则下游仍会拿到有漏洞的依赖。

```text
framework/components/object-mapping/Leistd.ObjectMapping.AutoMapper/   # 删除整个包（若决定删）
framework/Directory.Packages.props                                     # 移除 AutoMapper 版本项
framework/docs/components/object-mapping.md                            # 改：移除该实现的章节
framework/Leistd.Framework.slnx                                        # 移除工程引用
```

### 1.3 判定不做（记录门槛，避免反复讨论）

| 项 | 门槛 |
| --- | --- |
| `Leistd.Settings` / Features 家族 | 租户可配置项 <20 时业务侧一张表足够；差异项持续增长或租户数上到几十再立项 |
| 对象存储抽象组件 | 只有 `foundation` 碰厂商 SDK；单一消费者不进通用组件（门槛：三个以上真实消费者） |
| `TenantConfiguration` 承载业务字段 | 租户注册表是访问控制状态，不与高频读的展示数据混住 |

## 二、Framework：DDD 领域分层

**无待调整项。** 新增实体的环境值落点已定案并落地（`00ae4e7`、`252bbac`），当前职责边界：

```text
framework/ddd-struct/Leistd.Ddd.Infrastructure/Persistence/
└── BaseDbContext.cs          职责一：新增实体的环境值落点
                                （ChangeTracker.Tracked + StateChanged）
                                → TenantId + 创建审计（CreationTime / CreatorId）
                                → 三层护栏：FromQuery 跳过、状态须为 Added、值已有则不动
                              职责二：两个命名全局查询过滤器（软删除 AND 租户）
                              职责三：封闭 OnModelCreating，保证过滤器在派生配置之后套用

framework/components/auditing/Leistd.Auditing.EntityFrameworkCore/
└── AuditSaveChangesInterceptor.cs
                              职责：只管 Modified / Deleted（含软删除转换）
                                → Added 已移出，不再是它的职责

framework/components/multi-tenancy/Leistd.MultiTenancy.EntityFrameworkCore/
                              职责：只做租户注册表的存储与管理
                                → 落值组件已删除（MultiTenantSaveChangesInterceptor）
```

四条依据均在多租户开发中验证过：

| 依据 | 验证方式 |
| --- | --- |
| 环境值落在"进入跟踪时"是正确时机 | 与 ABP 的 `AbpDbContext.ChangeTracker_Tracked` → `SetCreationAuditProperties` 一致；四条回退验证按预期变红 |
| 提早落值不会覆盖查询出来的数据 | 三层护栏各有用例；反向验证确认 `FromQuery` 是冗余护栏（查询物化结果是 `Unchanged`，永不为 `Added`），保留以把意图写进代码 |
| `BaseDbContext.ConfigureModel` 封闭式钩子已正确——过滤器在派生配置之后套用 | 有回归用例（只经 `ApplyConfiguration` 入模型的实体同样被过滤） |
| 框架组件表名**未硬编码 schema**，派生上下文一行 `HasDefaultSchema` 即可覆盖全部实体 | 已 grep 确认无 `HasDefaultSchema`、组件 `ToTable` 不带 schema |
| 仓储与 `IQueryableAsyncExecuter` 抽象够用 | 验证项目里数据范围特性做完，Application 层未引用 EF Core |

> 与 Volo.ABP 的唯一残留差异是 `TenantId`：ABP 落在 `Entity` 基类构造函数（更早一步，靠反射写私有 setter）。本框架选择保持领域实体基类零环境依赖，代价是"作用域内 `new`、作用域外 `Add`"这种跨作用域持有实体的写法拿不到租户值——那本身是应当避免的写法。取舍已记录，不再重开。

## 三、Template：业务项目模板

### 3.1 资源服务器模式（P0，唯一阻塞项）

模板当前只支持"自己既是授权服务器又是资源服务器"（`AddCore().AddServer().AddValidation(UseLocalServer)` 一条链，`false` 分支是纯 Cookie 且 `AddServiceUserContext` 也在该门禁内）。

```text
template/
├── .template.config/template.json                    # 改：IncludeOpenIddict 布尔 →
│                                                     #   三态（Server / Validation / None）；
│                                                     #   同步 modifier 的排除清单
└── backend/src/CompanyName.ProjectName.Api/
    ├── Program.cs                                    # 改：三分支装配
    │                                                 #   Server: Core+Server+Validation(UseLocalServer)
    │                                                 #   Validation: 仅 Validation + 远端 Issuer
    │                                                 #                + UseSystemNetHttp + audience/scope
    │                                                 #   None: 现有 Cookie 分支
    │                                                 # 改：AddServiceUserContext 移出 OpenIddict 门禁
    │                                                 #   （Validation 形态同样需要服务间用户上下文）
    └── appsettings.json                              # 改：增 OAuth:Issuer / OAuth:Audience
```

```text
scripts/test-template-matrix.ps1                      # 改：新增 resource-server 场景；
                                                       #   既有场景断言 Validation 形态零残留
template/backend/tests/.../ServiceInvocationTests.cs   # 改：远端签发令牌被正确校验、
                                                       #   错误 audience 被拒
```

### 3.2 迁移形态（P1）

由**条件剪裁**二选一，不用运行时开关——剪裁让能力根本不在产物里。

```text
template/
├── .template.config/template.json                    # 改：新增 IncludeDbMigrator（默认 false）；
│                                                     #   拒绝"租户连接串 + false"的组合
├── backend/
│   ├── CompanyName.ProjectName.sln                    # 改：条件包含 DbMigrator 工程
│   └── src/
│       ├── CompanyName.ProjectName.Api/Program.cs     # 改：IncludeDbMigrator=true 时启动
│       │                                              #   **只校验不施加**，有待执行迁移即失败
│       │                                              #   并打印清单；false 时保持现状
│       └── CompanyName.ProjectName.DbMigrator/        # 新增工程：一次性 Job
│           ├── Program.cs                             #   无参=只读预演（清单+SQL）；--apply=施加
│           └── *.csproj                               #   引用 Infrastructure；不进运行镜像
└── docs/deploy/README.md                              # 改：迁移与发布 runbook；
                                                        #   生产应用账号不给 DDL 权限；
                                                        #   expand/contract；多副本仅限模式 2
```

```text
template/backend/tests/.../DbMigratorContractTests.cs  # 新增：模式 2 有待执行迁移时启动失败
                                                        #   且错误含待执行清单
scripts/test-template-matrix.ps1                       # 改：新增 db-migrator 场景
docs/template/development-guide.md                     # 改：新场景进标准矩阵表
```

### 3.3 前端平台入口权限集合一（P1）

两份硬编码清单在**租户场景下不等价**——这不是可维护性问题，是能落到真实角色上的缺陷：

| 位置 | 项数 | 含 `tenants.default` |
| --- | --- | --- |
| `app.routes.ts:61-71`（`/platform` 的 `data.permissions`） | 5 | **有**（`#if TenancyEnabled`） |
| `authorization-service.ts:36-44`（`canAccessPlatform`） | 4 | **无** |

一个只有租户管理权限的平台运营账号：

| 位置 | 表现 |
| --- | --- |
| `user-menu.ts:205` | 菜单里看不到平台入口 |
| `login.ts:212` | 登录后被重定向到非平台页 |
| `external-auth-callback.ts:99` | 外部登录同上 |
| 直接敲 `/platform/tenants` | **能进**（路由守卫放行） |

即"后端通、前端不通"，且是租户场景专属。

```text
template/frontend/src/app/
├── shared/models/permission.ts                       # 改：紧邻 PERMISSIONS 导出
│                                                     #   PLATFORM_ENTRY_PERMISSIONS ——
│                                                     #   平台入口权限集的**单一来源**（含 #if 条件段）
├── core/services/authorization-service.ts            # 改：canAccessPlatform 改引用该集合
│                                                     #   （删掉自己那份 4 项硬编码）
├── app.routes.ts                                     # 改：/platform 的 data.permissions 引用同一集合
└── core/services/authorization-service.spec.ts       # 改：断言两处**同源**（集合相等）；
                                                      #   补"仅 tenants 权限可进平台"的用例
```

> 同源断言是这一项的关键交付物。只改成引用同一常量，下一个人仍可能在路由里手写补一项——断言让分叉在 CI 里立刻失败。

### 3.4 多服务共库的 schema 指引（P2）

`HasDefaultSchema` 与 `MigrationsHistoryTable` **必须配对**——只做前者会让多个服务争用同一张 `__EFMigrationsHistory`，这是最隐蔽的坑。两者都是生成后调整，不改模板代码，只补文档与检查单。

```text
template/README.md                                    # 改：多服务共库时的两行配对调整
template/docs/standards/coding-backend.md             # 改：schema 与迁移历史表的约定
```

### 3.5 无需调整（已核实，记录以免重复排查）

| 面 | 核实结论 |
| --- | --- |
| 模板 Controller | `TenantController` 8 个端点全带 `[Authorize(Policy=...)]`；`tenants.*` 声明 `MultiTenancySides.Host`，侧别边界经 `[Authorize]` 免费获得（检查器先于授予读取拒绝）；无 Controller/DTO 接受客户端传入的 `tenantId`（`AuthController.cs:90` 是从用户实体写 claim，不是入参）；`BaseController` 只是 `ControllerBase` 薄壳 |
| 模板前端租户链路 | `tenant-interceptor`（附 `X-Tenant-Id`）、`tenant-context-service`、`http-error-interceptor` 的 `X-Tenant-Invalid` 恢复、租户管理页与登录页租户选择，均完整且有 spec |
| 框架前端 | **不存在交付面**（`framework/` 下无 `package.json`，前端只在 `template/frontend`） |
| 框架 Controller | 只有 `Response.AspNetCore/Extensions/ControllerExtensions.cs` 与 `Exception.AspNetCore` 的异常映射两个触点，与租户无关 |

## 四、执行顺序

| 序 | 项 | 级别 | 理由 |
| --- | --- | --- | --- |
| 1 | 3.3 前端权限集合一 | P1 | 最小（一个常量 + 两处引用 + 一条同源断言），且是现存缺陷，先清掉 |
| 2 | 3.1 资源服务器模式 | P0 | 唯一阻塞 CRM V2 开工的项 |
| 3 | 3.2 迁移形态 | P1 | 同在模板侧可同批；生产上线前必须 |
| 4 | 3.4 schema 指引 | P2 | 纯文档，随上一批捎带 |
| 5 | 1.1 按租户分库 | P1 | 首个需要独享库的租户出现前；改动面在框架，需独立验证 |
| 6 | 1.2 AutoMapper 去留 | P3 | 待决策 |

3.3 排在 3.1 之前是因为它已是缺陷而非改造，且改动面不与其它项重叠。

1.1 与 3.x 之间没有依赖，可并行；但 1.1 会改 `AddMultiTenancyEfCore` 的注册形态，届时模板的 `Infrastructure/DependencyInjection.cs` 需同步——该文件在 `252bbac` 刚因删除落值拦截器改过。
