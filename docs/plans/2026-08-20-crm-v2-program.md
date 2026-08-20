# CRM v2 多系统多租户落地方案

面向两个 broker（to-C 面向个人、to-B 面向机构）与三个业务系统（CRM / Sales / IB）。本文回答：建哪些服务与 SDK、每个功能模块落在树的哪个位置、哪些是框架适配哪些是业务项目调整、测试策略与验收标准，以及数据库分离、公共能力归置与数据迁移三项架构取舍（§5–§7）。

能力边界与选型依据见[隔离与授权场景](../architecture/isolation-and-authorization-scenarios.md)，不在此重复。

## 1. 服务与 SDK 划分

### 1.1 五个服务

| 服务 | 职责 | 模板参数 | 为什么独立 |
| --- | --- | --- | --- |
| **Identity** | 租户注册表、用户与凭据、SSO 授权服务器、租户品牌与配置的唯一来源 | `IncludeTenancy=true` `IncludeOpenIddict=true` `IncludeExternalLogin=true` | 登录页在认证之前渲染，品牌必须由不依赖任何业务系统的服务提供 |
| **CRM** | 客户主档、broker 差异化的开户/入驻流程 | `IncludeTenancy=true` `IncludeOpenIddict=false` | 差异化流程的主战场 |
| **Sales** | 销售机会、业绩 | 同 CRM | 与 CRM 生命周期不同、权限口径不同 |
| **IB** | 介绍经纪人、佣金 | 同 CRM | 同上 |
| **Platform** | 媒体库、KYC 单据、外发消息（邮件/短信/语音）与第三方适配器 | 同 CRM | 见 §6：有状态的公共能力归一处，适配器不各自成服务 |

**身份集中、授权分散**：Identity 只发令牌，三个业务服务各自定义权限、各自持有角色与授予。同一个人在 CRM 是客户经理、在 Sales 只读、在 IB 无角色——这就是三份互不相干的本地角色分配，天然满足场景二。

不要把授权也集中：那会强迫三个系统共用一个权限命名空间，任何一个系统加权限都要动中心服务。

业务服务用 `IncludeOpenIddict=false`：它们是资源服务器（校验 Bearer），不是授权服务器。

### 1.2 五个 SDK

模板已产出 `*.Client`（Refit 接口 + DTO + `AddXxxClient()`），每个服务把它作为 NuGet 发布：

| SDK | 谁消费 | 关键方法 |
| --- | --- | --- |
| `Identity.Client` | CRM / Sales / IB / 三个前端 | 租户品牌查询（匿名）、用户档案查询、凭据创建 |
| `Crm.Client` | Sales / IB | 客户主档查询 |
| `Sales.Client` | CRM | 客户的销售视图 |
| `Ib.Client` | CRM / Sales | 介绍关系查询 |
| `Platform.Client` | CRM / Sales / IB | 文件与媒体库、KYC 提交与状态、消息投递 |

`ServiceClient` 组件已在出站请求自动注入用户上下文与 `X-Tenant-Id`，跨服务调用不需要手写租户传递。

**依赖方向必须是单向的**：`Crm → Sales.Client` 与 `Sales → Crm.Client` 同时存在时，两个服务就绑成了一个部署单元。出现双向需求时改用事件（`Leistd.EventBus`），不要互相同步调用。

### 1.3 仓库形态

初期单仓多服务（一个 CI、一次改动跨服务可原子提交）。拆分触发条件：**任意两个服务的发布节奏出现持续冲突**——那时再按服务切仓，SDK 已经是 NuGet，切仓不改调用方。

## 2. 目录结构与模块归属

标记含义：`[F]` 框架既有能力直接用 · `[T]` 模板生成后调整 · `[B]` 业务新增 · `[!]` 需要决策或有坑

```text
crm-v2/
├── services/
│   ├── Broker.Identity/                          # dotnet new leistd-app --include-tenancy true
│   │   ├── backend/
│   │   │   ├── src/
│   │   │   │   ├── Broker.Identity.Api/
│   │   │   │   │   ├── Program.cs                # [T] DomainFormat 配置；转发头信任网段
│   │   │   │   │   └── Controllers/
│   │   │   │   │       ├── TenantController.cs           # [T] 模板自带，宿主侧租户 CRUD
│   │   │   │   │       └── TenantBrandingController.cs   # [B] 匿名品牌读取（只放非敏感字段）
│   │   │   │   ├── Broker.Identity.Domain/
│   │   │   │   │   ├── Users/                    # [T] 模板自带
│   │   │   │   │   └── Tenants/
│   │   │   │   │       └── TenantProfile.cs      # [B] IMultiTenant；Kind + 品牌 + 流程开关
│   │   │   │   ├── Broker.Identity.Application/
│   │   │   │   │   ├── Tenants/                  # [T] 模板自带 TenantAppService / TenantSeeder
│   │   │   │   │   └── Branding/                 # [B] 品牌读写 + 匿名投影
│   │   │   │   ├── Broker.Identity.Infrastructure/
│   │   │   │   └── Broker.Identity.Client/       # [B] SDK：品牌、用户档案、凭据创建
│   │   │   └── tests/
│   │   │       └── Broker.Identity.IntegrationTests/     # [T] 模板自带 + [B] 品牌与流程用例
│   │   └── frontend/
│   │       └── src/app/features/account/
│   │           └── components/login/             # [T] 按品牌渲染（logo/文案/主题）
│   │
│   ├── Broker.Crm/                               # dotnet new leistd-app --include-tenancy true --include-openiddict false
│   │   ├── backend/
│   │   │   ├── src/
│   │   │   │   ├── Broker.Crm.Api/
│   │   │   │   │   ├── Program.cs                # [T] Bearer 资源服务器；DomainFormat
│   │   │   │   │   └── Controllers/
│   │   │   │   │       ├── CustomerController.cs         # [B]
│   │   │   │   │       ├── OnboardingController.cs       # [B] 按 Kind 分流的入驻
│   │   │   │   │       └── DataScopeController.cs        # [B] 角色数据范围配置
│   │   │   │   ├── Broker.Crm.Domain/
│   │   │   │   │   ├── Customers/
│   │   │   │   │   │   ├── Customer.cs           # [B] IMultiTenant
│   │   │   │   │   │   └── CustomerManager.cs    # [B] 领域服务
│   │   │   │   │   ├── Onboarding/
│   │   │   │   │   │   ├── IOnboardingStrategy.cs        # [!B] 按租户 Kind 选策略，不按 TenantId 分支
│   │   │   │   │   │   ├── IndividualOnboarding.cs       # [B] to-C：个人信息、手机验证
│   │   │   │   │   │   └── InstitutionalOnboarding.cs    # [B] to-B：机构资料、授权人
│   │   │   │   │   └── Authorization/
│   │   │   │   │       └── RoleDataScope.cs      # [B] 范围分配表（IMultiTenant）
│   │   │   │   ├── Broker.Crm.Application/
│   │   │   │   │   ├── Customers/                # [B] 集合入口统一走 IDataScopeApplier
│   │   │   │   │   ├── Onboarding/               # [B] 编排策略 + 调 Identity.Client 建凭据
│   │   │   │   │   ├── DataScopes/
│   │   │   │   │   │   ├── OwnCustomerScopeProvider.cs           # [B] IDataScopeProvider<Customer>
│   │   │   │   │   │   ├── TeamCustomerScopeProvider.cs          # [B]
│   │   │   │   │   │   ├── AllCustomerScopeProvider.cs           # [B] 必须显式 _ => true
│   │   │   │   │   │   └── RoleDataScopeAssignmentProvider.cs    # [B] IDataScopeAssignmentProvider
│   │   │   │   │   ├── Permissions/Provider/     # [T] 加 App.Customers.* / App.DataScopes.*
│   │   │   │   │   └── Notifications/
│   │   │   │   │       └── BrandedEmailComposer.cs        # [B] 页眉页脚取自 Identity.Client
│   │   │   │   ├── Broker.Crm.Infrastructure/
│   │   │   │   │   └── Persistence/
│   │   │   │   │       ├── CrmDbContext.cs       # [T] ConfigureModel 内挂业务实体配置
│   │   │   │   │       └── EntityConfigurations/ # [B] 唯一索引按宿主/租户成对过滤索引
│   │   │   │   └── Broker.Crm.Client/            # [B] SDK
│   │   │   └── tests/
│   │   │       └── Broker.Crm.IntegrationTests/  # [T]+[B]
│   │   └── frontend/
│   │       └── src/app/
│   │           ├── core/services/                # [T] 模板自带租户上下文与拦截器
│   │           ├── features/
│   │           │   ├── onboarding/               # [B] 按 Kind 渲染的入驻表单
│   │           │   ├── customers/                # [B] 列表页 + 独立 table 组件 + 分页器
│   │           │   └── platform/data-scopes/     # [B] 角色数据范围配置页
│   │           └── layout/components/default-sidebar/   # [T] 菜单分组按关注点，权限裁剪
│   │
│   ├── Broker.Sales/                             # 同 CRM 形态
│   └── Broker.Ib/                                # 同 CRM 形态
│
├── shared/
│   └── Broker.Contracts/                         # [!B] 仅放跨服务稳定枚举（TenantKind 等）
│
└── deploy/                                       # [T] Compose / K8s；泛域名与证书
```

### 2.1 两个 broker 的差异点归属

| 差异 | 落点 | 形态 |
| --- | --- | --- |
| 登录页（logo、文案、主题） | Identity `[B]` | `TenantProfile` + 匿名品牌端点；子域名解析后第一个请求即可取 |
| 用户注册流程字段 | CRM `[B]` | 领域策略按 `TenantKind` 分流；Identity 只负责凭据 |
| 邮件页眉页脚 | Identity 提供数据、各服务组装 `[B]` | 品牌是唯一来源，避免三个服务各存一份 |
| 业务规则阈值 | 各服务 `[B]` | `TenantProfile` 的强类型配置段 |

**关键约束：按 `TenantKind` 建模，不要按 `TenantId` 分支。** 接第三个同类 broker 的成本应该是插一行数据，而不是改遍代码。

## 3. 框架适配 vs 业务项目调整

### 3.1 框架侧：保持默认，不为本项目加能力

五个服务全部使用组件的默认装配与最佳实践配置。经评估**本项目不需要新增或修改任何通用组件**：

| 需求 | 用现成的什么 | 判定 |
| --- | --- | --- |
| 租户隔离 | `Leistd.MultiTenancy.*` | 实体实现 `IMultiTenant` 即可 |
| 子域名品牌化 | `MultiTenancyOptions.DomainFormat` | 配置项，非改造 |
| 每系统独立角色 | `Leistd.Authorization.*` | 授予已按 `(TenantId, ProviderName, ProviderKey)` 分区 |
| 集合可见性 | `Leistd.Authorization.DataScope.Core` | 业务实现 Provider，组件不动 |
| 跨服务身份与租户传递 | `Leistd.ServiceClient.*` | 出站已自动注入 |
| 领域分层、仓储、工作单元 | `framework/ddd-struct` | 不动 |

**唯一一个需要提前定的门槛**：现有组件里没有 Settings / Features 家族。两个 broker、差异项在 20 以内时，业务侧 `TenantProfile` 表 + 强类型配置类足够；**差异项持续增长或租户数上到几十**时才立项 `Leistd.Settings`。不要为两个租户造设置中心。

同样明确**不做**：不把品牌字段塞进 `TenantConfiguration`。租户注册表是访问控制状态（已为此去掉缓存），不能和高频读的展示数据混住，否则所有消费多租户的服务都背上不需要的业务包袱。

### 3.2 业务项目侧：生成后必须调整的清单

`[T]` 标记的每一处都是生成后的必做项：

| 项 | 调整内容 | 不做的后果 |
| --- | --- | --- |
| `Program.cs` | 配 `DomainFormat`；配 `ForwardedHeaders:KnownNetworks` 为网关网段 | 不配网段则网关后转发头全部失效；配错为空则任意客户端可改写 Host |
| 权限定义 Provider | 加本系统的 `App.*`，宿主侧能力标 `MultiTenancySides.Host` | 租户超管会看到宿主功能，点进去必然 403 |
| `DbContext.ConfigureModel` | 挂业务实体配置（不要改写 `OnModelCreating`，它已封闭） | 全局过滤器覆盖不到该实体 |
| 实体唯一索引 | 租户化实体的唯一索引拆成宿主行 / 租户行成对过滤索引 | 可空 `TenantId` 进唯一索引时 NULL 互不相等，宿主行失去兜底 |
| 前端父路由白名单 | 新模块权限加入 `/platform` 的 `data.permissions` 与 `canAccessPlatform` | 只有该模块权限的用户进不了控制台（后端通、前端 403） |
| 前端菜单分组 | 按关注点分组（数据范围属访问控制，不属业务） | 归类错会误导使用者 |
| 列表页形态 | 复用 `hlmTable` + TanStack + `app-table-paginator` | 裸表格与既有页面风格断裂、无分页 |
| SDK | 在 `*.Client` 补对外方法与 DTO | 调用方手写 HttpClient，租户与用户上下文传递失效 |

### 3.3 部署侧

- 泛域名 `*.crm.example.com` 与通配证书；**只把租户用的通配子域指向应用**，否则 `www` 会被解析成名为 `www` 的租户并 404。
- 网关必须**覆盖或剥离**客户端传入的 `X-Forwarded-*`，并把自身网段配进 `KnownNetworks`。

## 4. 测试策略与验收标准

### 4.1 分层策略

| 层 | 覆盖什么 | 硬性要求 |
| --- | --- | --- |
| 单元 | 领域规则、入驻策略按 Kind 分流、范围 Provider 的组合语义 | 范围 Provider **必须在关系型 Provider 上测**——InMemory 全内存求值，不可翻译的谓词会静默通过 |
| 集成（每服务） | 三路越权矩阵（列表/详情/更新）、同名用户跨租户不可见、停用租户处理、批量操作整体拒绝 | 用 Sqlite 或真实 PostgreSQL；断言以"看不见"为主 |
| 跨服务 | 双宿主：A 在租户 T 内调 B，B 的 `ICurrentTenant.Id == T` 且查询落 T 分区；匿名伪造租户头被拒 | 必须覆盖，SDK 的价值全在这里 |
| 浏览器闭环 | 只测跨进程、跨时序、跨浏览器语义的接缝 | 见 4.2 |
| 矩阵 | 各服务的模板参数组合可生成、可构建、零残留标记 | 每次动条件代码都跑 |

**每个修复都要反向验证**：把修复退回去，确认对应用例变红。本轮框架开发中多次出现"用例根本没走到目标分支"，只有反向验证能发现。

### 4.2 浏览器闭环必测清单

进程内测不到的接缝，逐条对应本项目的真实风险：

1. to-C / to-B 子域名各自打开登录页 → 品牌正确且不串。
2. 租户超管菜单不含宿主侧功能，直接敲 URL 同样 403。
3. 只有单个模块权限的用户能进控制台并只看到该模块。
4. 入驻流程：两个 Kind 的表单字段与校验各自正确。
5. 配置角色数据范围 → 换用该角色的用户登录 → 列表集合随配置变化。
6. 租户被停用 → 在途会话下一请求被注销、本地租户状态清空、回登录页不死锁。
7. **CORS 分离部署下**重跑 5 与 6：跨域响应头需 `WithExposedHeaders` 才能被前端读到。

第 7 条是唯一只有真实浏览器能验证的一类：后端集成测试直读响应头、前端单测手工构造 `HttpHeaders`，**两侧都绕过浏览器的 CORS 过滤**。

### 4.3 验收标准

功能性：

- [ ] 两个 broker 各自子域名进入，登录页品牌、注册流程、邮件页眉页脚全部正确且互不影响。
- [ ] 新增第三个同 Kind 的 broker **只需数据变更**，不改代码、不发版。
- [ ] 同一用户在 CRM / Sales / IB 拥有不同角色，各系统权限判定互不影响。
- [ ] 数据范围在列表、总数、导出三个入口给出一致集合；读范围不蕴含写范围；批量操作含越权项时整体拒绝。

隔离与安全（**任一不过即不可上线**）：

- [ ] 跨租户按 Id 取数返回 404 而非 403（不泄漏资源存在性）。
- [ ] 已认证会话携带伪造租户头无法改写自身租户。
- [ ] 子域名部署下匿名请求无法用请求头改写租户；受管域内无租户段的主机定案为宿主；受管域外仍可用请求头（服务间调用依赖它）。
- [ ] 每个租户化实体都在"完整性锁"断言清单内；不实现 `IMultiTenant` 的关联表单独断言。

工程性：

- [ ] 生成项目零编译警告（含 CS0105、CS1573 这类）。
- [ ] 每服务集成测试全绿；模板参数矩阵全绿。
- [ ] 配置错误（非法域名格式、非法代理网段）导致宿主**启动失败**并在错误中带出配置键与值——而不是记日志后以退出码 0 结束。
- [ ] 随包文档与源码无 API 漂移；公共契约的约束写在 XML 与文档里，不是只存在于校验器和异常文本里。

## 5. 数据库分离

### 5.1 两个互相独立的轴，不要混谈

| 轴 | 本方案的选择 | 理由 |
| --- | --- | --- |
| **按服务分库** | 每个服务一个库（5 服务 5 库） | 服务自治的前提。共享数据库的"微服务"是同一个部署单元穿了五件衣服 |
| **按租户分库** | **不做**，共库共表 + `TenantId` 列 + 全局过滤器 | 见下 |

### 5.2 为什么当前不按租户分库

框架只支持共库共表：`TenantConfiguration` 是封闭的六个字段，**没有连接串**（ABP 有，我们刻意没有）。对本项目这是正确的默认：

两个 broker 是**产品分段**而不是客户。按租户分库会得到 10 个库，换来的隔离性全局过滤器已经提供，而代价是实打实的：

- 迁移从"一次"变成"× 租户数"，且部分失败会留下混合 schema 版本。
- 连接池按租户翻倍，EF 的 `DbContextOptions` 要按连接串缓存。
- **宿主侧跨租户聚合报表直接做不了**——而租户管理控制台恰恰需要它。
- 建租户从一条 INSERT 变成一次基础设施操作（建库 + 迁移 + 校验）。

### 5.3 现在就要留的接缝（零成本）

不建能力，但别把路堵死：

1. **DbContext 的创建集中在 `Infrastructure/DependencyInjection` 一处**。将来插入连接串解析器时只改一个文件。
2. **`IMultiTenant` 全局过滤器永远不撤**，即使将来分库。一条路由错的连接会泄漏整个库，过滤器是第二道防线。把"分库了就不需要过滤器"当成优化是灾难性的。
3. **跨租户查询必须走显式的 `IDataFilter.Disable<IMultiTenant>()` 口子**，不要散落在报表代码里。分库时这些点就是要重写的清单。

### 5.4 触发条件与届时的改造量

出现以下任一情况才立项：

- **数据驻留合规**：不同司法辖区要求数据物理留在境内。
- **合同约定的物理隔离**：大客户要求独享实例。
- **单租户体量迫使独立伸缩**。

届时的框架改造（约一个组件的量）：`TenantConfiguration` 增连接串 → DbContext 创建时按 `ICurrentTenant` 解析 → 选项按连接串缓存 → 迁移器遍历租户。ABP 是这个形状，属已知设计，不是探索性工作。

**明确否决"共库分 schema"**：EF Core 支持别扭，备份粒度并没有变好，等于承担了迁移复杂度却没拿到隔离收益。

## 6. 公共能力：第五个服务，以及为什么只有第五个

### 6.1 先分清两类东西

| 类别 | 例子 | 该做成什么 |
| --- | --- | --- |
| **适配器**（无自有领域状态） | 邮件、短信、语音、KYC 厂商、对象存储 SDK | **不要各自成服务**。它们是某个服务 Infrastructure 层里的实现 |
| **有状态能力**（有生命周期、跨服务消费） | 媒体库、KYC 单据与审核状态、外发消息审计 | 值得一个服务 |

把五个第三方对接各做成一个服务，是微服务过细最典型的形态：得到五个薄 HTTP 代理，每个都要自己的认证、重试、追踪、值班，而耦合一点没减少。

### 6.2 结论：一个 `Broker.Platform`

```text
services/Broker.Platform/                        # 第五个、也是最后一个服务
└── backend/src/
    ├── Broker.Platform.Domain/
    │   ├── Files/            # [B] 媒体库：元数据、租户分区、访问授权
    │   ├── Kyc/              # [B] 单据、审核状态、有效期
    │   └── Messaging/        # [B] 外发记录（发给谁、何时、经哪个通道）
    ├── Broker.Platform.Application/
    │   └── Messaging/        # [B] 统一投递面；模板按租户取自 Identity
    ├── Broker.Platform.Infrastructure/
    │   └── Providers/        # [B] 厂商适配器住在这里，不是独立服务
    │       ├── Email/        #     模板已带 IEmailSender/MailKit，直接用
    │       ├── Sms/          # [B]
    │       ├── Voice/        # [B]
    │       ├── Kyc/          # [B] 厂商 API
    │       └── ObjectStorage/# [B] 直接包 S3/OSS SDK
    └── Broker.Platform.Client/  # [B] SDK：文件、KYC、消息投递
```

合成一个而不是三个的依据：它们**共享同一组横切需求**（租户分区、幂等与重试、发给谁的审计、按租户的厂商配置），且**消费者相同**（三个业务系统）。拆开只会让部署与认证面翻倍。

### 6.3 一个可被推翻的判据

外发消息**是否必须集中**，取决于一件事：**是否需要"我们给这个客户发过什么"的合规审计**。

- 需要（受监管的券商大概率需要）→ 集中到 Platform，业务服务调 SDK，厂商凭据不散落在三个服务里。
- 不需要 → 邮件就留在各服务的 Infrastructure（模板已有 `IEmailSender`），**Platform 里不要这个模块**。

这条写成判据而不是结论，是为了让它可被证伪。

### 6.4 硬约束

- **五个服务是本项目的上限**。再要加服务，先回答"它有自己的领域状态吗、消费者是谁"两个问题。
- **禁止跨服务直连数据库**。CRM 想读 Platform 的表时，走 `Platform.Client`。共享库是拆分服务的反面。
- 框架侧**不新增组件**：对象存储只有 Platform 碰厂商 SDK，一个消费者的抽象不该进通用组件（通用组件的门槛是三个以上真实消费者）。

## 7. 数据迁移：开源便利性与生产可靠性

### 7.1 当前形态就是问题所在

模板在 `app.Run()` 之前无条件执行：有迁移走 `MigrateAsync`、没迁移走 `EnsureCreatedAsync`。**没有环境判断**。于是生产环境每个 Pod 启动都会尝试改 schema，N 个副本同时启动就是 N 路并发迁移。

### 7.2 两个目标不冲突，因为它们作用于不同环境

便利性是**开发环境的默认值**，可靠性是**其余环境的硬约束**。同一份代码可以同时满足：

| 环境 | 行为 |
| --- | --- |
| Development | 保持零配置：`Database:AutoMigrate` 默认开，`dotnet run` 即可用 |
| 其余环境 | **应用绝不改 schema**。检测到待执行迁移时**启动即失败**并打印待执行清单——不要带着旧 schema 静默跑起来 |

### 7.3 落地要点

1. **独立迁移器**（`*.DbMigrator` 控制台工程或 `dotnet ef migrations bundle`），作为 CI 步骤 / init container / K8s Job 执行。运行镜像不需要 EF 工具链。
2. **生产的应用数据库账号不给 DDL 权限。** 这一条是"应用不迁移"从愿望变成机制的关键——只靠代码里的 if 判断，早晚有人绕过。
3. **多副本竞争由构造消除**：迁移器是一次性 Job，不是每个副本各跑一遍。若不得已在应用内迁移，必须加数据库咨询锁——但不要走到这一步。
4. **`EnsureCreated` 只允许出现在开发环境**。它与迁移不可混用，生产库永远走迁移。
5. **零停机用 expand/contract**：加可空列 → 回填 → 部署同时兼容新旧的代码 → 切换 → 下个版本再删列。**任何"一步重命名"都会在滚动发布期间打断正在运行的旧副本。**
6. **按租户分库会让这件事显著变难**（迁移器要遍历租户、部分失败留下混合版本）——这是 §5.2 之外又一条"暂不分库"的理由。

### 7.4 与既有生产变更约定对齐

生产数据变更遵循既定流程：**先只读预演并输出给人确认 → 再执行写入；写入用单事务，带前置校验与提交前自检，任何一项不过则整体回滚；同时准备回滚脚本。** 迁移器的 runbook 就是这条流程的实现，不是另一套规矩。

### 7.5 需要改模板的部分

| 项 | 现状 | 目标 |
| --- | --- | --- |
| 启动自动迁移 | 无条件执行 | 仅 Development 且开关为真；其余环境有待执行迁移则启动失败 |
| 迁移器 | 无 | 新增 `*.DbMigrator` 工程 + Compose/K8s Job 示例 |
| 文档 | 未涉及 | 生成项目 README 增"迁移与发布"一节，写明 DDL 权限约定 |

这三项是**模板改造**（影响所有下游项目），不是本项目的业务调整；建议与本方案一并排期。
