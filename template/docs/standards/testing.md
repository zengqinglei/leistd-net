# 测试规范

> 测试命令、框架和门槛以当前解决方案、测试配置和 CI 为准。本文定义选择与执行原则，不提供配置模板。
<!--#if (SpaFrontend)-->
> 前端命令同时以本项目的 `package.json` 为准。
<!--#endif-->

## 1. 按风险选择测试

| 变化 | 最低验证 |
| --- | --- |
| 纯函数、领域规则、权限判断 | 单元测试 |
| 数据库、DI、序列化、外部适配 | 集成测试 |
| API 或前后端契约 | 集成测试，并验证成功、失败和越权 |
| 关键用户流程 | 已配置的 E2E，或明确的运行验证 |
| 缺陷修复 | 能在修复前失败、修复后通过的回归测试 |
| 认证、授权、输入或敏感数据 | 正向与拒绝路径的安全验证 |

修改共享组件、公共契约或基础设施时扩大回归范围；局部低风险改动先运行最小相关测试。

按时机分层执行，不在每次改动后跑全量：

| 时机 | 目标耗时 | 运行 |
| --- | --- | --- |
<!--#if (SpaFrontend)-->
| 编辑中 | ≤ 1 分钟 | 受影响的单个测试类或 spec（见后端、前端的收窄命令） |
| 阶段完成 / 提交前 | 以实际范围计时，通常目标几分钟 | 按 §1.1 选择测试范围；相关 lint 和构建 |
<!--#else-->
| 编辑中 | ≤ 1 分钟 | 受影响的单个测试类（见后端的收窄命令） |
| 阶段完成 / 提交前 | 以实际范围计时，通常目标几分钟 | 按 §1.1 选择测试范围；相关构建 |
<!--#endif-->
| 已配置的 CI | 由流水线定 | 当前项目配置的完整测试、静态检查、构建和关键流程 |

### 1.1 完整验证由谁承担

模板没有附带业务 CI 流水线。默认在阶段完成时运行受影响测试项目全量，**包含集成测试**，并执行相关 lint、构建及关键运行验证；可使用已有隔离验证环境，但不能假定 CI 已经接替。

只有项目已配置承担完整集成责任的**必过 CI**（在本规范记录其入口、范围与阻止合并的设置），才能改为"本地单测全量与目标集成，CI 完整回归"。

数据库、事务、租户、HTTP 授权、协议或网络取消变化尽早验证对应真实路径；纯领域拒绝和参数判断由单测验证。CI 缺失或环境不可用时，未完成的真实验证明确列出，不冒称通过。

### 1.2 按开发场景选择范围

| 场景 | 编辑中 | 阶段完成 / 提交前 |
| --- | --- | --- |
| 小 bug 修复 | 目标回归测试证明修复前失败、修复后通过；必要的目标构建 | 按 §1.1 承担受影响测试项目回归 |
| 新功能模块 | 领域/应用规则单测、相关接口或组件测试 | 模块成功/失败路径、需要认证的接口 401/403；新增注册验证生命周期与重复登记 |
| 跨模块需求 | 修改规则及直接调用方测试 | 修改模块及依赖方集成测试，跨模块 API/事件契约；涉及事务或副作用时验证回滚或一致性 |
| 数据库、迁移或租户路由 | 目标真实 PostgreSQL 集成测试 | 按变化验证迁移、模型快照、约束、查询和隔离；并发/独立事务变化补相应路径 |
| 认证、授权或协议 | 目标规则及拒绝路径 | 真实宿主、授权边界；涉及 Cookie、跳转、令牌、跨服务或退出时执行已有协议/浏览器闭环或真实运行验证 |
<!--#if (SpaFrontend)-->
| 前端局部变更 | 目标 spec | 受影响测试、lint、构建；共享组件、状态或 API 契约变化扩大到调用方 |
<!--#endif-->
| 文案、项目规范或可执行示例 | 引用、格式及对应静态检查 | 涉及可执行示例、生成或运行契约时检查实际交付内容，不仅按扩展名选择 |

新模块只有涉及权限时才验证权限登记与种子，涉及持久化模型时才验证迁移与快照，涉及本地化时才检查词条键。关键用户流程变化运行项目已配置的 E2E；没有对应入口时明确启动条件、操作、预期结果并完成真实运行验证，不杜撰命令。共享组件、公共契约和基础设施按实际依赖扩大回归，完整责任按 §1.1 分配。

### 1.3 审查与失败重跑

完整可审查改动形成后核对代码与文档一致性，可与已配置的 CI 并行；高风险难返工的边界可提前审查。失败时先复现目标路径，修复后跑目标验证与受影响回归；不反复运行未变化的无关全套。

## 2. 后端

从 `backend/` 的解决方案或目标测试项目执行：

```bash
dotnet test                                                       # 全部
dotnet test tests/CompanyName.ProjectName.UnitTests               # 只跑单元测试，秒级
dotnet test tests/CompanyName.ProjectName.IntegrationTests --filter "FullyQualifiedName~<测试类名>"
```

### 2.1 两个测试项目，分工由成本决定

```text
backend/tests/
├── CompanyName.ProjectName.UnitTests/          不建宿主、不连库、不发 HTTP
│   ├── Domain/          领域规则、值对象、策略判定
│   ├── Application/     应用层契约与纯逻辑，依赖用假实现从构造函数传入
│   ├── Infrastructure/  基础设施里的纯映射（如数据库错误翻译），不连库
│   ├── Api/             宿主层里的纯逻辑（协议交互凭据、限频装饰器），不建宿主
│   └── Registration/    在 IServiceCollection 上断言注册结果
└── CompanyName.ProjectName.IntegrationTests/   经真实宿主与真实 PostgreSQL 验证端到端行为
    ├── Fixtures/        ProjectWebApplicationFactory、PostgreSqlTestDatabase、共享装配基类
    └── <各 *Tests.cs>
```

集成测试跑在真实 PostgreSQL 上，需要本机 Docker：`PostgreSqlTestDatabase` 每次运行起一个容器，用项目迁移建好模板库，每个 `ProjectWebApplicationFactory` 克隆一份独立的库。唯一约束、查询翻译、事务回滚与独立事务因此与生产一致。单元测试不连库。

宿主初始化、容器启动、迁移、库克隆与播种都有成本，且不因测试类所在目录而改变。因此：

- 能在单元测试里验证的规则不要放进集成测试。§1 的风险分级表就是这条线。
- 集成测试每类一个 `IClassFixture<ProjectWebApplicationFactory>`，**不要每个用例建宿主**。
- 需要改配置的用例用 `WithWebHostBuilder` 派生宿主，但**同类配置变体应当归组复用**，
  而不是每个用例一个——一个测试类里起七八个派生宿主，这个类就会独占整套测试的大部分时间。
  前提是用例不依赖空库：断言精确用户名、全表计数或修改租户级设置的用例共享宿主会互相干扰，
  这类用例保留独立宿主。
<!--#if (LocalIdentity)-->
- 宿主里与被测行为无关的固定成本要压低。`ProjectWebApplicationFactory` 把口令哈希的工作因子
  （`PasswordHash:IterationCount`）调到 1000：每个宿主都要播种管理员、每次登录都要校验口令，
  生产默认值会让这两步占去集成测试一半以上的 CPU。默认值本身由 `PasswordHashingTests` 钉住。
<!--#endif-->

### 2.2 通用要求

- 测试方法名用英文句子、单词以下划线分隔，写出行为与条件，力求简短（如 `Revoked_device_cookie_stops_working_immediately`）；不用中文标识符。名字装不下的前因后果写进 XML 注释。
- 数据库测试使用隔离数据库、独立 schema 或可靠清理机制；集成测试的每个宿主已经各有一份库。
- 实体配置、唯一索引与全局查询过滤器在集成测试里验证。不用 EF InMemory 或 SQLite 代替：
  前者全内存求值，会让被违反的约束和不可翻译的查询静默通过；后者只有一个写者，
  "已写入后再开独立事务写入"这种生产上合法的写法会在测试里锁死。
- 批量 `ExecuteUpdate` / `ExecuteDelete` 不经过变更跟踪器：断言删除或更新结果时换一个作用域读，
  否则读到的是同一 DbContext 里仍被跟踪的旧实体。
- 外部服务使用 fake、mock 或明确的测试环境；日志用 `FakeLogger`，不手写替身。
- 时间边界（锁定、挑战与验证码有效期、令牌到期、限频窗口）用 `FakeTimeProvider` 或显式时刻验证，不靠真实时间流逝。集成测试在 `ConfigureTestServices` 里 `RemoveAll<TimeProvider>()` 后登记假时钟，`IClock`、Cookie 认证与 OpenIddict 随之跟随；起点取当前时刻。缓存过期不跟随该时钟：测试自身的缓存 TTL 时保留真实 MemoryCache/HybridCache，经 `MemoryCacheOptions.Clock` 接入同一假时钟，让 TTL 计算与本地到期一起推进，验证到期前与到期时刻，不用主动删除冒充到期；Redis、Data Protection 限时保护器与 OIDC 处理器的寿命校验用各自机制，不手写替代判定来快进。项目自己的挑战规则把到期时刻存在挑战里、用注入的时钟判定，因此能快进。
- 端到端只验接线与生效值，不等安全窗口过期；确需观察真实到期时用配置缩短窗口（如[部署说明](../deploy/README.md)中的 `OAuth:AccessTokenLifetime`）并断言已生效。网络、取消、超时等有上限、等可观察结果的等待不在此列。
- 领域规则、状态变化、权限和错误语义应通过可观察行为断言。
- 各层注册入口覆盖注册结果、生命周期与相同登记重复调用不重复生效，有意覆盖组件默认实现的登记验证两种调用顺序；有注册或配置决策（选择实现、派生 Options）的宿主扩展测行为，单纯转调由启动集成测试覆盖（见[后端开发规范 §4](./coding-backend.md#4-依赖注入)）。注册面是契约，编译期看不出错。

### 2.3 后台维护任务

Job 的截止判据与失败传播放单元测试；批量 SQL、租户过滤与 DI 接线放 PostgreSQL 集成测试；调度器自身行为由框架覆盖，不重复测。集成测试固定 `TimeProvider` 后经 DI 直接执行 Job（配置变体复用既有数据库，只在该变体关闭自动调度），用截止前、等于、后三组数据按业务定义核对 `<` 或 `<=`（时刻对齐 PostgreSQL 微秒精度、留毫秒间隔），批量 SQL 后换新作用域读取；涉及租户时宿主与租户都验证。不按 Job 数量增加容器、固定等待或独立 E2E，跨进程责任变化时才扩大到真实运行闭环。

<!--#if (SpaFrontend)-->
## 3. 前端

从 `frontend/` 使用项目已声明的脚本：

```bash
npm test                                            # 全部 spec
npm test -- --include src/app/core                  # 只跑某个目录或文件
npm run lint
npm run build
```

单测由 Vitest 在 Playwright 驱动的真实 Chromium 里运行（无头）。新机器首次运行前安装一次浏览器：`npx playwright install chromium`。

保存状态最短时长与搜索防抖使用 Vitest 假计时器验证边界，不睡真实业务时长。先完成宿主／路由初始化，再伪造 Date、timeout 与 interval（RxJS 防抖使用 interval），保留原生 rAF、performance 和微任务；用 `vi.advanceTimersByTimeAsync` 推进，配合 fixture 稳定与 DOM 断言。边界期望独立于生产常量，teardown 清理业务计时器并恢复真实计时器；HTTP 验证与真实 Chromium 隔离保留。

- 用例名（`describe` / `it`）用英文句子，小写开头，写出行为与期望（如 `keeps the dialog open when saving fails`）；中文只出现在注释与测试数据里。名字装不下的前因后果写进上方注释。
- service、pipe、复杂状态和共享组件覆盖输入、输出、空态与错误态。
- HTTP 调用使用 mock，除非当前任务明确执行前后端集成验证。
- 表单覆盖校验、提交、防重复操作和失败反馈。
- 项目当前未配置 E2E 命令时，不臆造 `npm run e2e`；先根据真实需求选择工具并获得确认。

<!--#endif-->

## 4. 核心业务闭环

相关能力启用时，端到端验证至少覆盖：

- 登录、未登录和令牌失效。
- 普通用户、角色管理员与超级管理员的权限差异。
- 关键资源的创建、查询、更新、删除和越权访问。
- 审计字段及软删除行为。
<!--#if (IncludeNotifications)-->
- 通知持久化、未读状态和实时推送。
<!--#endif-->
<!--#if (IncludeRealTime)-->
- 实时订阅、资源鉴权和通用订阅不受影响的路径。
<!--#endif-->

<!--#if (IncludeLocalization || (SpaFrontend && IncludeOperationRecords))-->
## 5. 静态闸门

有些错漏不让任何断言变红，只让界面显示得不对；这类判据做成脚本，跟测试一起跑（Windows 上把 `python3` 换成 `py`）：

```bash
<!--#if (IncludeLocalization)-->
python3 scripts/check-i18n.py                             # 词条、引用与写死文案
python3 scripts/check-i18n.py --self-test                 # 判据本身还成立吗
<!--#endif-->
<!--#if (SpaFrontend && IncludeOperationRecords)-->
python3 scripts/check-operation-action-i18n.py            # 每个动作码都有句子模板
python3 scripts/check-operation-action-i18n.py --self-test  # 判据本身还成立吗
<!--#endif-->
```

<!--#if (IncludeLocalization)-->
`check-i18n.py` 检查词条键集合与占位符、静态引用、后端资源与错误码、DataAnnotations 键与写死的中文，判据见脚本文件头。

<!--#endif-->
<!--#if (SpaFrontend && IncludeOperationRecords)-->
**动作码 ↔ 词条必须集合相等。** 界面把动作码渲染成一句话（「删除了角色 管理员」），
靠的是 `operationRecords.actions.<码>` 这条词条。漏配**不会报错**：没有词条的码按降级规则
原样显示裸码，页面照常能用，只是那一行是 `role.deleted` 这种机器码——没有红灯，
只有"有些行看不懂"。新增动作码时同一个提交里补上中英两种句子。

<!--#endif-->
新增这类闸门时一并写 `--self-test`：判据自己失效之后，它给出的每一次"通过"都是假的。

<!--#endif-->

## 6. 数据与结果

- 测试数据可重复构造，不依赖执行顺序，不使用生产密钥或真实客户数据。
- 会写数据库的测试应说明前置条件并清理可变数据。

