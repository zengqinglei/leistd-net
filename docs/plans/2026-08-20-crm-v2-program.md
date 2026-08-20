# CRM v2 多系统多租户落地方案

面向两个 broker（to-C 面向个人、to-B 面向机构）与三个业务系统（CRM / Sales / IB）。本文只回答四件事：建哪些服务与 SDK、每个功能模块落在树的哪个位置、哪些是框架适配哪些是业务项目调整、测试策略与验收标准。

能力边界与选型依据见[隔离与授权场景](../architecture/isolation-and-authorization-scenarios.md)，不在此重复。

## 1. 服务与 SDK 划分

### 1.1 四个服务

| 服务 | 职责 | 模板参数 | 为什么独立 |
| --- | --- | --- | --- |
| **Identity** | 租户注册表、用户与凭据、SSO 授权服务器、租户品牌与配置的唯一来源 | `IncludeTenancy=true` `IncludeOpenIddict=true` `IncludeExternalLogin=true` | 登录页在认证之前渲染，品牌必须由不依赖任何业务系统的服务提供 |
| **CRM** | 客户主档、broker 差异化的开户/入驻流程 | `IncludeTenancy=true` `IncludeOpenIddict=false` | 差异化流程的主战场 |
| **Sales** | 销售机会、业绩 | 同 CRM | 与 CRM 生命周期不同、权限口径不同 |
| **IB** | 介绍经纪人、佣金 | 同 CRM | 同上 |

**身份集中、授权分散**：Identity 只发令牌，三个业务服务各自定义权限、各自持有角色与授予。同一个人在 CRM 是客户经理、在 Sales 只读、在 IB 无角色——这就是三份互不相干的本地角色分配，天然满足场景二。

不要把授权也集中：那会强迫三个系统共用一个权限命名空间，任何一个系统加权限都要动中心服务。

业务服务用 `IncludeOpenIddict=false`：它们是资源服务器（校验 Bearer），不是授权服务器。

### 1.2 四个 SDK

模板已产出 `*.Client`（Refit 接口 + DTO + `AddXxxClient()`），每个服务把它作为 NuGet 发布：

| SDK | 谁消费 | 关键方法 |
| --- | --- | --- |
| `Identity.Client` | CRM / Sales / IB / 三个前端 | 租户品牌查询（匿名）、用户档案查询、凭据创建 |
| `Crm.Client` | Sales / IB | 客户主档查询 |
| `Sales.Client` | CRM | 客户的销售视图 |
| `Ib.Client` | CRM / Sales | 介绍关系查询 |

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

四个服务全部使用组件的默认装配与最佳实践配置。经评估**本项目不需要新增或修改任何通用组件**：

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
