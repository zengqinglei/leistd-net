# Framework / Template 待调整内容：文件级任务分解

本文只回答**改哪些文件、每处职责是什么**。为什么要改、取舍依据见 [CRM V2 模板落地指南](./2026-08-20-crm-v2-program.md) 的 §5、§6，不在此重复。

优先级：`P0` 阻塞 CRM V2 开工 · `P1` 生产上线前必须 · `P2` 可捎带 · `P3` 待决策

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

**无需改动。** 三条依据均在多租户开发中验证过：

| 依据 | 验证方式 |
| --- | --- |
| `BaseDbContext.ConfigureModel` 封闭式钩子已正确——过滤器在派生配置之后套用 | 本轮修复并有回归用例（只经 `ApplyConfiguration` 入模型的实体同样被过滤） |
| 框架组件表名**未硬编码 schema**，派生上下文一行 `HasDefaultSchema` 即可覆盖全部实体 | 已 grep 确认无 `HasDefaultSchema`、组件 `ToTable` 不带 schema |
| 仓储与 `IQueryableAsyncExecuter` 抽象够用 | 验证项目里数据范围特性做完，Application 层未引用 EF Core |

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

### 3.3 前端权限白名单合一（P2）

`/platform` 父路由的 `data.permissions`（5 项）与 `AuthorizationService.canAccessPlatform`（4 项）是两份硬编码清单，新增模块要同步改两处。漏改的表现是"后端通、前端 403"，最易被误判为权限未生效。

```text
template/frontend/src/app/
├── shared/models/permission.ts                       # 改：导出"平台入口权限集"单一来源
├── core/services/authorization-service.ts            # 改：canAccessPlatform 引用该集合
└── app.routes.ts                                     # 改：/platform 的 data.permissions 引用同一集合
```

### 3.4 多服务共库的 schema 指引（P2）

`HasDefaultSchema` 与 `MigrationsHistoryTable` **必须配对**——只做前者会让多个服务争用同一张 `__EFMigrationsHistory`，这是最隐蔽的坑。两者都是生成后调整，不改模板代码，只补文档与检查单。

```text
template/README.md                                    # 改：多服务共库时的两行配对调整
template/docs/standards/coding-backend.md             # 改：schema 与迁移历史表的约定
```

## 四、执行顺序

| 序 | 项 | 理由 |
| --- | --- | --- |
| 1 | 3.1 资源服务器模式 | 唯一阻塞 CRM V2 开工的项 |
| 2 | 3.2 迁移形态 | 同在模板侧，可同批；生产上线前必须 |
| 3 | 3.3 + 3.4 | 小改，随上一批捎带 |
| 4 | 1.1 按租户分库 | 首个需要独享库的租户出现前；改动面在框架，需独立验证 |
| 5 | 1.2 AutoMapper 去留 | 待决策 |

1.1 与 3.x 之间没有依赖，可并行；但 1.1 会改 `AddMultiTenancyEfCore` 的注册形态，届时模板的 `Infrastructure/DependencyInjection.cs` 需同步。
