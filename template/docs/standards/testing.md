# 测试规范

> 测试命令、框架和门槛以当前解决方案、`package.json`、测试配置和 CI 为准。本文定义选择与执行原则，不提供配置模板。

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
| 编辑中 | ≤ 1 分钟 | 受影响的单个测试类或 spec（见 §2、§3 的收窄命令） |
| 提交前 | 几分钟 | 受影响测试项目全量、`npm run lint`（暂存文件另由提交钩子检查）、改动涉及的构建 |
| CI | 由流水线定 | 全部测试、lint 与构建 |

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
│   └── Registration/    在 IServiceCollection 上断言注册结果
└── CompanyName.ProjectName.IntegrationTests/   经真实宿主验证端到端行为
    ├── Fixtures/        ProjectWebApplicationFactory、共享装配基类
    └── <各 *Tests.cs>
```

**一个 `WebApplicationFactory` 宿主约 0.5–0.8 秒**，集成测试的时长几乎全在这上面，
且它随测试类数量和配置变体数量线性增长。因此：

- 能在单元测试里验证的规则不要放进集成测试。§1 的风险分级表就是这条线。
- 集成测试每类一个 `IClassFixture<ProjectWebApplicationFactory>`，**不要每个用例建宿主**。
- 需要改配置的用例用 `WithWebHostBuilder` 派生宿主，但**同类配置变体应当归组复用**，
  而不是每个用例一个——一个测试类里起七八个派生宿主，这个类就会独占整套测试的大部分时间。
  前提是用例不依赖空库：断言精确用户名、全表计数或修改租户级设置的用例共享宿主会互相干扰，
  这类用例保留独立宿主。
- 宿主里与被测行为无关的固定成本要压低。`ProjectWebApplicationFactory` 把口令哈希的工作因子
  （`PasswordHash:IterationCount`）调到 1000：每个宿主都要播种管理员、每次登录都要校验口令，
  生产默认值会让这两步占去集成测试一半以上的 CPU。默认值本身由 `PasswordHashingTests` 钉住。

### 2.2 通用要求

- 测试方法名用英文句子、单词以下划线分隔，写出行为与条件，力求简短（如 `Revoked_device_cookie_stops_working_immediately`）；不用中文标识符。名字装不下的前因后果写进 XML 注释。
- 数据库测试使用隔离数据库、独立 schema 或可靠清理机制。
- 实体配置、唯一索引与全局查询过滤器必须用关系型 Provider 验证；
  EF InMemory 全内存求值，会让被违反的约束和不可翻译的查询静默通过。
- 外部服务使用 fake、mock 或明确的测试环境。
  时间用 `FakeTimeProvider`、日志用 `FakeLogger`，不手写替身。
- 领域规则、状态变化、权限和错误语义应通过可观察行为断言。
- 组合根的每个 `AddXxx()` 至少覆盖：注册结果与生命周期、重复调用幂等。
  注册面是契约，编译期看不出错。

## 3. 前端

从 `frontend/` 使用项目已声明的脚本：

```bash
npm test                                            # 全部 spec
npm test -- --include src/app/core                  # 只跑某个目录或文件
npm run lint
npm run build
```

单测由 Vitest 在 Playwright 驱动的真实 Chromium 里运行（无头）。新机器首次运行前安装一次浏览器：`npx playwright install chromium`。

- 用例名（`describe` / `it`）用英文句子，小写开头，写出行为与期望（如 `keeps the dialog open when saving fails`）；中文只出现在注释与测试数据里。名字装不下的前因后果写进上方注释。
- service、pipe、复杂状态和共享组件覆盖输入、输出、空态与错误态。
- HTTP 调用使用 mock，除非当前任务明确执行前后端集成验证。
- 表单覆盖校验、提交、防重复操作和失败反馈。
- 项目当前未配置 E2E 命令时，不臆造 `npm run e2e`；先根据真实需求选择工具并获得确认。

## 4. 核心业务闭环

相关能力启用时，端到端验证至少覆盖：

- 登录、未登录和令牌失效。
- 普通用户、角色管理员与超级管理员的权限差异。
- 关键资源的创建、查询、更新、删除和越权访问。
- 审计字段及软删除行为。
- 通知持久化、未读状态和实时推送。
- 实时订阅、资源鉴权和通用订阅不受影响的路径。

## 5. 静态闸门

有些错漏不是测试能发现的——它们不让任何断言变红，只让界面显示得不对。这类判据做成脚本，
跟测试一起跑：

```bash
python3 scripts/check-operation-action-i18n.py            # 每个动作码都有句子模板
python3 scripts/check-operation-action-i18n.py --self-test  # 判据本身还成立吗
```

**动作码 ↔ 词条必须集合相等。** 界面把动作码渲染成一句话（「删除了角色 管理员」），
靠的是 `operationRecords.actions.<码>` 这条词条。漏配**不会报错**：没有词条的码按降级规则
原样显示裸码，页面照常能用，只是那一行是 `role.deleted` 这种机器码——没有红灯，
只有"有些行看不懂"。新增动作码时同一个提交里补上中英两种句子。

新增这类闸门时一并写 `--self-test`：判据自己失效之后，它给出的每一次"通过"都是假的。

## 6. 数据与结果

- 测试数据可重复构造，不依赖执行顺序，不使用生产密钥或真实客户数据。
- 会写数据库的测试应说明前置条件并清理可变数据。
- 失败时保留命令、失败用例和关键日志，区分产品、测试、环境与外部依赖问题。
- 不删除有效断言、不静默跳过失败、不把未执行测试描述为通过。

测试结果默认记录在当前答复和 CI。只有合规、审计或团队明确要求长期证据时，才参考最新同类记录写文档，不使用固定报告模板。
