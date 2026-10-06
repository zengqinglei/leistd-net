# 阶段四：规范逐条合规审查与补漏

前三阶段（`1b0cad7c`、`fac8a15e`、`23f6211f`、`8e2b489e`，PR #66）完成了 Skill 与规范分层、注册与配置入口收敛、模板分层与依赖方向重构。阶段二审查集中在结构类规则。本阶段先盘点规范全集，再对照**最终版规范**逐条核对未覆盖的规则，补做漏掉的验收，把能自动化的规则补成闸门。在同一分支完成，推到 PR #66。

取舍原则沿用：终局但不过度设计、单一事实、按需加载、可证伪、分发自洽、开发者视角。判定以规范原文为准；代码更合理时改规范，不为一致性改动收益弱的公共 API。

## 1. 规范全集与覆盖矩阵

先列出全部规范文件的每条可判定规则，逐条标注责任项与状态。状态只取：**合规**、**违规已修复**、**证据复用**（指向既有闸门、测试或阶段二审查，并确认相关输入未变）、**不适用**（写明原因）、**未完成**。验收时不允许出现"未完成"。

| 规范 | 责任项 |
| --- | --- |
| 模板 `coding-common.md`、`coding-backend.md`（§2–§3 职责类） | P1 |
| 模板 `coding-backend.md`（§1、§4–§8）、`api.md`、`auth.md`、`service-invocation.md`、`testing.md`（后端）、`tech-stack.md`、`project-structure.md`、`deploy/README.md`；`docs/template/development-guide.md`、`docs/template/browser-authentication.md` 的维护规则 | P2 |
| 模板 `coding-frontend.md`、`frontend-ui.md`、`frontend-spartan.md`、`frontend-i18n.md`、`testing.md`（前端） | P3 |
| `docs/framework/development-guide.md` 全部章节（含 §5 依赖与示例自包含、§6.1–§6.6、§7、§9 跨平台约定）、`docs/architecture/design-principles.md`、组件文档与 XML 的一致性 | P4 |
| 阶段二、三新增或改写的规则（见 P5 增量清单） | P5 核对清单完整，由所属 P1–P4 审查 |

矩阵写入审查清单（第 3 节），每行：规则（文件 §）→ 检查方法（闸门／测试／人工）→ 范围 → 状态 → 证据。

## 2. 任务

### P1 模板后端职责审查

范围：全部实体、领域服务、应用服务、事件处理器、后台任务、Controller 与 Api 层协作类型。

对每个有判断或状态变更的方法记录：**约束对象 → 所需状态与依赖 → 应归属层 → 当前实现 → 结论**。判据：

| 约束或行为 | 应归属 |
| --- | --- |
| DTO 字段格式、长度、必填 | 入口 DTO 的 DataAnnotations；内层不重复（多入口共享的实体守卫除外） |
| 单聚合不变量（只看本聚合状态） | 实体方法或领域服务 |
| 领域规则所需的存在性或唯一性查询（如用户名占用） | 领域服务，可经仓储查询 |
| 跨聚合关系（依赖其他聚合存在或状态） | 应用服务 |
| 查询用例：分页、筛选、投影、多来源拼装 | 应用服务 |
| 事务边界 | 应用服务按需 `[UnitOfWork]` |
| 实体变更衍生的缓存、通知等副作用 | 实体本地事件 + 应用层事件处理器 |
| 协议与宿主操作（Cookie 签发、认证挑战、HTTP 结果） | Api 层，直接执行 |
| 路由、鉴权、调用应用服务 | Controller，不含业务判断 |

同时检查实体状态是否只经公共方法修改。修复后的测试验证拒绝条件、状态与副作用不变，不能只验证"移动后的类被调用"。

### P2 模板后端其余规范

| 规范 | 检查 |
| --- | --- |
| `api.md` | 路由前缀与方法名映射、成功与失败响应形态、状态码与错误码映射、分页参数 |
| `auth.md` | 每条权限的侧别与实体租户维度；会话、Cookie、Origin 与浏览器认证契约 |
| `coding-backend.md` §1、§4–§8 | 注册归属与方式、DTO 与校验写法、数据访问、异常类型、日志英文结构化且不含敏感信息、Api 目录 |
| `coding-common.md` §3 | 删除幂等、外部调用统一开关、枚举只在边界转换、签名同步与短名导入 |
| `service-invocation.md` | 服务间身份、错误契约、Client 包边界 |
| `testing.md`（后端） | 测试项目分工、真实 PostgreSQL、时间与假时钟、注册测试范围 |
| `tech-stack.md`、`project-structure.md` | 依赖与版本、目录与分发入口 |
| `deploy/README.md` | 配置键、多副本约束、迁移顺序与实际配置文件一致 |
| `docs/template/*` 维护规则 | 条件生成、错误码不随本地化裁剪、浏览器认证维护约束 |

### P3 模板前端其余规范

静态全集检查 + 代表性浏览器验证：

- **静态**：全部页面与共享组件对照字号与字重档位、圆角、语义色板、间距与当前项写法、图标注册、表格列裁剪与操作列、列表查询状态、导航分组与权限、页面标题、显示偏好同源、日期管道、OnPush、Spartan 定制登记表与 `libs/ui` 相对上游的实际差异。
- **浏览器**（agent-browser，有头）：在生成项目上覆盖桌面与窄屏、亮暗主题、粗指针（按钮与输入 44px）、减少动效、表格溢出与行展开、按权限裁剪的菜单、键盘操作。每项写明检查对象与断言，存截图证据；按共享布局与组件选代表页面。

### P4 Framework 其余规范

开发指南按章节逐条盘点；阶段二已审的章节（§1 注册与目录、§6.6 DI）复用证据并确认输入未变。重点：§4 注释内容（契约在接口、实现继承文档）、§5 依赖方向与组件文档示例自包含、§6.1 公共 API 设计、§6.2 参数与配置校验、§6.3–§6.5 变更与删除、异常类型选择、§7 测试、§9 跨平台约定（含本轮新增脚本）。公共 API 改动同步消费者、XML、组件文档、升级说明与破坏性脚注；删除或替换按 §6.5 写明替代入口。

### P5 增量规则清单

从 `1b0cad7c..HEAD` 提取新增或改写的规则，登记：规则 → 最终文件与章节 → 适用代码范围 → 负责的 P 项 → 验证入口。至少包含：组合期读配置的划分、注册测试范围、DTO 一文件一类型、`MOCKED_URL`、依赖矩阵与路由懒加载例外、`import()` 字面量、单测服务范围、i18n 闸门、失败去重载体边界、官方日志脱敏接线、多租户与单点登出已知边界、部署的 SignalR 背板与就绪日志。P5 只保证清单完整，审查由所属 P 项完成。

### P6 规则补闸门

按优先级：编译器与官方分析器 → 现有 lint 与测试 → 新脚本。生成项目长期需要的规则随模板分发、经项目入口执行，仓库入口负责调用并验证分发；仓库专属规则留在根 `check-all.ps1`。新闸门的自检覆盖合法输入、违规输入与允许的例外。无法稳定判定的规则（如职责归属）保留人工审查，不做关键词闸门。

### P7 项目 Skill 路由抽查

验证对象是生成项目的 `leistd-project-workflow`；仓库专项 Skill 不在本轮声明范围内。

- **环境**：固定 AI CLI 与模型版本、当前分支生成的项目快照；每个请求独立会话，在仓库外的临时目录运行，不指定预期 Skill。记录 CLI 自带的全局配置作为已知环境因素。
- **判定**：以工具轨迹（触发的 Skill、读取的文件）、文件变化与测试或部署结果判定，不接受 AI 自述；超时或轨迹不足记未完成。

路由按 `SKILL.md` 的意图表与条件路由判定，加载分三类：必需加载、条件允许（如出现文档归属、沉淀或精简候选时加载 `documentation.md`）、无依据的额外加载。只有既无任务依据、也不满足条件路由的加载判失败；只审查场景始终要求文件不变。

| 场景 | 夹具 | 应加载 | 不应加载 | 通过条件 |
| --- | --- | --- | --- | --- |
| 实现 | 生成项目中植入一个已知小缺陷，请求按现象修复 | `SKILL.md`、`development.md`、`quality.md`；`docs/README.md` 与读取表对应的规范 | `delivery.md`、`bootstrap.md`、无关专题规范 | 缺陷修复且有回归测试；相关测试通过 |
| 只审查 | 植入已知违规（应用服务里的单聚合规则），请求审查 | `SKILL.md`、`quality.md`；被审代码涉及的规范 | `development.md`、`delivery.md`、`bootstrap.md` | 指出该违规并给出证据；工作区文件无变化 |
| 部署 | 明确授权的本机隔离目标：`deploy/docker-compose.yml` | `SKILL.md`、`delivery.md`、`docs/deploy/README.md` | `development.md`、`bootstrap.md`、编码规范 | 按部署文档执行迁移与启动；健康检查通过；报告实际版本 |
| 无索引的其他项目 | 独立的非 .NET 小项目（只安装 workflow Skill，无 `docs/README.md`），植入小缺陷 | `SKILL.md`、`development.md`、`quality.md` | `bootstrap.md`（未要求初始化）、`delivery.md` | 不因缺少索引停止；从源码与测试恢复约定，修复并测试通过 |
| 重复纠正与精简 | 实现场景后同一会话再给一次同类纠正；夹具规范中预置一条重复条目与一条与代码不符的过时条目 | `documentation.md` | — | 沉淀为项目 `docs/` 规范或闸门并登记索引；重复条目被合并、过时条目被删除或改正，有效规则保留；不改 Skill |

### P8 OIDC 真实到期等待

已在当前工作区执行（`-IncludeExpiryWait`）：`exchange-expiry` 与 `S7` 实际运行并通过，快速档寿命生效。认证相关输入若在本阶段改变，在最终候选上重跑。

### P9 读取成本测量脚本

新增 `scripts/measure-template-read-cost.py`（仓库专属，不随模板分发）：固定五类任务的样例请求、生成条件（模板源码、全部功能开启）、读取文件集合，以及基线提交 `e984db19` 的读数（后端 CRUD 53,365；全栈 CRUD 89,680；UI 调整 51,672；新增文案 33,266；只审查 78,841）与当前上限（33,747；58,291；39,271；19,055；39,404）。输出每类任务的去重文件清单与字符数；`--check` 在任一任务超过上限时失败。`docs/template/development-guide.md` §4 改为引用该脚本。不纳入 `check-all.ps1`（篇幅调整需要人工判断，不作为每次提交的阻断）。

## 3. 流程

1. P1–P5 并行只读审查，输出覆盖矩阵与发现（文件:行证据；改法；是否改规范）。
2. 汇总为 `docs/assessments/2026-10-06-compliance-findings.md`，送 Codex 审核；通过后转为第 5 节任务，assessment 删除。
3. 实施修复与 P6；P7 可在隔离快照上先行发现问题，最终在候选上复验。
4. 实施结果送 Codex 审查，意见先核实前提再处理。

## 4. 验收

所有结论绑定最终候选（提交 SHA 或 diff 摘要）；相关输入变化后重跑受影响项。

| 检查 | 通过标准 |
| --- | --- |
| 覆盖矩阵 | 每条可判定规则状态为合规、违规已修复、证据复用或不适用；无"未完成" |
| 静态闸门 | `check-all.ps1` 全部通过；新增闸门自检正反例与例外通过 |
| Framework | 有代码改动时：框架测试全集、打包与受影响包消费验证；只改文档时检查引用与示例编译 |
| 模板 | `test-template-matrix.ps1 -Tier pr`；持久化或认证改动补 PostgreSQL 与 OIDC 端到端；前端交互或视觉调整完成对应浏览器验证 |
| 读取成本 | `python3 scripts/measure-template-read-cost.py --check` 通过：五类任务读取量均不高于登记的上限 |
| P7 | 五个场景按通过条件判定，轨迹与结果留存 |
| 收尾 | 推送；PR #66 最终候选 CI 全部通过；有效结论上收到稳定文档后删除本计划 |

## 5. 修复任务

审查清单（P1–P4、P7 共 123 条，决定表 122 行）经 Codex 审核后转入本节，原 assessment 已删除。
### 5.0 审核修订（优先于下文各表，Codex 两轮审核）

| # | 涉及 | 修订 |
| --- | --- | --- |
| A1 | P1-13/P2-1 | 测试发信端点除改为策略授权外，补动作码、Provider 登记、端点 `[OperationRecordAction]` 与词条；测试：已认证无权限用户被拒时恰好记录一次 |
| A2 | P1-1/P1-2，TB2b | 加 `[UnitOfWork]` 时，`TwoFactorAppService.EnableAsync` 的设置密钥缓存清理移到提交后；测试"回滚保留设置密钥、提交后清理"。令牌撤销若移到 AfterCommit，单独写明并测试提交后撤销失败的结果，不沿用"失败整体回滚"的验收 |
| A3 | P4-4 | 修正 `AddServiceClientPipeline` 相同调用重复添加处理器的缺陷，验证同一客户端重复调用后各处理器仍各一份；Redis 不同参数的既有处理如实写入 XML，不放宽相同登记的幂等要求 |
| A4 | P4-1 | 共享校验判据：启动期校验默认选项，`Begin(options)` 创建实际工作单元前校验单次选项；Timeout 覆盖亚秒与超出整数秒范围；补默认配置、单次选项、边界值测试，区分配置错误（`OptionsValidationException`）与方法参数错误（`ArgumentOutOfRangeException`） |
| A5 | §4 工作包 | G1b 依赖 G1a（同改 `check-retired-terms.ps1`、`check-i18n-keys.ps1`）；G-TB 依赖 G1a（同改 `test-template-generation.py`），G-06 的矩阵接线归 G-TB；补文件归属：F1 含 `OperationRecordArchiveJob.cs`，TB3a 含 `UserAppService.cs`，TB3b 含 `AuthAppService.cs` 与 Auth DTO，P3-1 的 a11y 测试归 FE3a |
| A6 | 破坏性提交 | 每个破坏性包在同一提交中携带自己的升级清单条目与 `BREAKING CHANGE:` 脚注；`docs/framework/upgrade-0.13.0.md` 由主会话串行整合；Z 只做最终核对 |
| A7 | P2-10 | 补 `VerificationCodes__Key` 的部署注入入口、`.env.example` 与说明，保持"开启邮箱验证才要求有效密钥"；验收：开启后缺失或无效密钥启动失败、有效密钥通过 |
| A8 | FE5/P3-15 | 浏览器验证增加一个带浏览器会话的 Resource 场景（`resource`）；B-1～B-14 的操作、预期与场景归属见本节附录，不依赖临时目录 |
| A9 | G-06/G-08/G2 | G-06 接入矩阵：生成产物上检查脚本存在并执行，覆盖关闭本地化的形态，自检含"只被异常映射引用、无业务使用"的死码；G-08 的 `test-template-generation.py` 改动归 G-TB；G2 扩展现有 `framework/build/test-package-consumption.ps1`，不新建脚本 |
| A10 | G-15 | 在 `scripts/check-i18n-repo.py` 中实现：扫描范围扩展到 `*ErrorCodes.cs` 之外（如 `OperationFailureCodes`）并覆盖完全没有资源目录的包；自检含这两种情况 |
| A11 | G-10/P3-2 | G-10 只比对"有差异的组件集合"，规范写明其边界；逐项差异人工核对；补登记 button 的 destructive 暗色改动、badge `success` 变体与 a11y 文案注入 |
| A12 | G-01/G-02/G-12/G-14/G-16 | 每个诊断登记独立反例与允许的例外；G-16 复验 P4-5～P4-7 的原始错误片段，桩只补业务示例类型；G-14 自检证明 runner 改写参数后、经 PowerShell 启动的 Python 子进程实际收到警告设置 |
| A13 | G1b/G-X | 依赖 Node、Spartan CLI、Prettier 的闸门放在已安装前端依赖的 CI 作业执行（或在作业中显式准备），不依赖本地残留、缺依赖不跳过 |
| A14 | P7-1 | `quality.md` 措辞：规范类 finding 引用具体条目；其他 finding 给出预期行为、影响与源码或测试证据 |
| A15 | 全部包（用户审查发现） | 每个包报告列出新建的类型与文件及其依据的规范条款；主会话提交前核对命名后缀、目录与模块归属是否已在规范登记，未登记的类别先补规范或改用已登记形式（如 `UserRoleNamesQuery` → `Users/UserRoleReader`）；Z 收口时对 `1b0cad7c..HEAD` 全部新增类型再核一遍 |
| A16 | P1-7（用户裁定方案 A） | 用户角色名查询不设 `*Reader`/`*Query` 类型：Domain 新增 `Users/Repositories/IUserRepository : IRepository<User, Guid>`（`GetRoleNamesAsync`），Infrastructure `Persistence/Repositories/EfCoreUserRepository` 一次连接查询实现，经 `AddRepository<User, EfCoreUserRepository>()` 登记；框架 `AddRepository` 同时注册实现的自定义仓储接口（并入 F6a，追加能力，非破坏）；规范补聚合与自定义仓储规则（已提交）。模板改动为新包 **TB-R**：依赖 TB2a、F6a 与重新打包，先于 TB2b；删除 `Application/Users/UserRoleReader.cs`，三处调用方改注入 `IUserRepository`，补仓储集成测试（含租户过滤与软删除） |
| A17 | 实施中的更正 | P4-23 前提不成立：空响应体原本已是 502，F2 只删除不可达分支并补用例，无行为变化、不写升级说明；G-12 不启用 CA1812（测试 114 处均为经 DI/反射实例化的误报），死替身由评审与 §7.3 把关；TB2b 的令牌撤销不进业务事务，放在提交前最后一步（失败整体回滚）；F7 新增：修复 `DataFilter<T>` 并行分支状态泄漏；FE4 新增：移除与同源契约矛盾的 `API_GATEWAY` 构建参数与环境中的固定网关域名 |

### 5.1 执行约束

- 每个工作包由独立的子 agent 在干净上下文中完成，**不得再派生子 agent 或分身**；超过约 60 次工具调用的包先拆分。
- 包内不提交；主会话按依赖顺序核对后分组提交，破坏性提交带升级条目与脚注（A6）。
- 修正意见交给新的小 agent，附精确文件与改法，不恢复已很长的原 agent。

### 0. 核实记录

本次逐条打开所引的 file:line，核对了全部"高/中"发现，以及所有判为"改规范"或"破坏性"的发现。没有发现前提完全不成立、需要整条删除的项。以下 5 项的前提做了更正或补充：

| ID | 原结论 | 核实结果 | 处理 |
| --- | --- | --- | --- |
| P1-2 | 角色列表推送与安全提醒都在写入后由应用服务直接发出，加 `[UnitOfWork]` 后会先于提交发出 | **角色部分不成立**：`RoleListChangedEvent` 经本地事件总线发布，`RoleListChangedEventHandler.cs:11-13` 写明"有工作单元时提交之后分发"；框架 `UnitOfWorkEventHandlerAttribute` 默认 `AfterCommit`（`Leistd.UnitOfWork.Core/Events/UnitOfWorkEventHandlerAttribute.cs:19`）。**安全提醒部分成立**：`ISecurityAlertPublisher` 是直接调用 | 收窄为安全提醒的提交时序问题；角色事件只剩"由应用服务而非实体发布"这一点，与 §3.4 字面不符，交需裁定 §3.1 |
| P1-4 | 邮箱唯一性有三条实现路径 | 管理员改邮箱（`UserAppService.cs:253`）已经调用领域服务 `IsEmailAvailableAsync`，只是另抛一个码。真正的第二套实现只有注册发码前的直查（`EmailVerificationAppService.cs:77-83`：用 `ToLower()`，且不含软删除） | 改为"两套查询实现、三个错误码"，修法不变 |
| P2-4 | 只有开放应用的写操作缺审计 | 另发现：角色更新同样没有记录。`RoleController.cs:69` 没挂 `[OperationRecordAction]`，`OperationRecordActions.cs` 里也没有 `role.updated` | 合并到同一项，修复范围扩到角色更新 |
| P3-7 | 构造函数里有 10 处业务流程 | `coding-frontend.md:74` 已经允许"随 `DestroyRef` 释放的订阅及其首次读取"，首次 GET 加载按字面可以算合规。明确违规的只有 `two-factor-setup.ts:85-86`（发起 POST）和 `login.ts:153-156`（清理会话主体） | 交需裁定 §3.6 |
| P2-6 | 组合期读取并校验 Issuer、Audience、ClientId、ClientSecret | 属实。但代码注释（`RemoteTokenAuthenticationExtensions.cs:28-29`）给了理由："OpenIddict 在组合期就要用 issuer" | 保持改代码，加一个前提验证：若实测 OpenIddict 必须在组合期拿到 issuer，就改规范登记这条例外 |

下面两条不是规范发现，不纳入决定表：`framework/tests/` 下未入库的旧布局 bin/obj 目录，以及 `framework/components/unit-of-work/Leistd.UnitOfWork.EfCore/`（只有 bin/obj，未跟踪）。它们属于本地残留，在本地清理即可。

### 1. 覆盖矩阵汇总

完整矩阵作为证据复制在**附录 A**；scratchpad 中的原文件是同一份。状态只取：合规、违规（待修）、证据复用、不适用。

| 审查 | 行数 | 合规 | 违规待修 | 证据复用 | 不适用 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| P1 模板后端职责 | 23 | 8 | 9 | 4 | 2 | 另有方法级清单：181 个有判断或状态变更的方法，其中 34 个违规（见 `p1-findings.md` 附录 A，未复制进本文件） |
| P2 模板后端其余规范 | 150 | 97 | 33 | 16 | 4 | 后端、部署与 README 自 `fac8a15e` 起未变，阶段二结论按证据复用计 |
| P3 模板前端 | 111 | 56 | 26 | 25 | 4 | **其中 6 行是"合规（静态）／待浏览器验证"**。浏览器用例 B-1～B-14 只做了设计、尚未执行，由工作包 FE5 关闭；关闭前这 6 行不算完成 |
| P4 Framework | 85 | 40 | 31 | 12 | 2 | 实跑 11 道闸门全部通过；去掉 CS0436 抑制后 Release 全量构建结果为 0 警告 |
| P7 Skill 路由 | 5 个场景 | 3 PASS | 2 FAIL | — | — | 只审查、重复纠正与精简两个场景失败，见第 6 节 |

发现总数：编号发现 121 条（P1 17、P2 38、P3 20、P4 38、P7 8），加上 P4 转交的 2 条（P4-T1 `service-invocation.md` 旧写法、P4-T2 模板文档写死 `python3`），共 123 条，全部进入决定表，没有删除的条目。决定表共 122 行：有 4 组重复项合并成一行，分别是 P1-13＋P2-1、P1-4＋P2-16、P1-12＋P2-28、P2-35＋P2-36＋P7-6，另有 P2-38＋P4-T2 共一行；P2-28、P2-35、P4-T2 因为跨两个交付面，各自另占一行；另外 2 行不是编号发现（P1 附带的未用 using、FE5 浏览器验证）。按交付面统计行数：Framework 35、模板后端 45、模板前端 22、规范与 Skill 6、闸门 4、模板分发缺陷 10。破坏性变更 8 项：Framework 3 项（P4-1、P4-2〔按推荐方案〕、P4-11），模板 HTTP 契约 5 项（P1-4、P1-10、P1-13、P2-3、P2-5〔按推荐方案〕）；另有 P4-23 一项行为变化（500→502），也写进升级说明。

### 2. 决定表

"破坏性"列：Framework 改动写入 `upgrade-0.13.0.md` 的 §36（重新编译、类型变化）或 §37（行为收口）。0.13.0 尚未发布，直接并入这两节。模板 HTTP 契约改动新增 **§39「模板：错误码收敛、入参校验与授权拒绝交给管道、删除幂等（破坏性）」**，写法参照 §38。升级说明统一由工作包 Z 撰写。

#### 2.1 Framework

| ID | 问题与证据 | 决定 | 破坏性 | 验证要求 |
| --- | --- | --- | --- | --- |
| P4-1 | UoW 入口没有验证器，也没有 `ValidateOnStart`。亚秒级 Timeout 被 `(int)TotalSeconds` 截成 0，在 ADO.NET 里等于不限时（`Leistd.UnitOfWork.Core/DependencyInjection.cs:41-52`、`Leistd.UnitOfWork.EntityFrameworkCore/Database/DbContextProvider.cs:260-266`，已核实） | 改代码：新增 `UnitOfWorkOptionsValidator(configSectionPath)`，Timeout 只能为 null 或 ≥1 秒，IsolationLevel 必须是已定义值；入口挂 `ValidateOnStart()` | 是（行为收紧）→ §37 | `[Theory]`：0.5 秒、负值、未定义的隔离级别都在启动时失败，消息以实际配置节的键开头；覆盖自定义 configSectionPath |
| P4-2 | `RetentionDays` 默认 365（`OperationRecordRetentionOptions.cs:28`），与 `docs/framework/development-guide.md:316`"审计类启用时天数必填"相反 | **需裁定 §3.5**，推荐改代码，改为 `int?` 必填 | 推荐方案：是 → §36 | 验证器 `[Theory]` 覆盖三态；模板 `appsettings.json:116-121` 已显式写了 365，矩阵不受影响 |
| P4-3 | `Error:Forbidden` 没有随包译文，模板复制了一份（`OperationFailureCodes.cs:6-20`；模板 `Resources/{en,zh-CN}.json:53`，已核实） | 改代码：OperationRecords.Core 加入嵌入资源 en/zh-CN，在 `AddOperationRecords` 中登记；删掉 XML 和文档里"没有随包译文"的说明；`operation-records.md` 写明它是记录下来的原因码，不属于协议层的 `Error:*`。模板副本由 TB3a 在新包就位后删除 | 否 → release notes | 只注册组件、宿主不提供资源时，FailureMessage 不为空；闸门 G-15 |
| P4-4 | 42 个公开 `Add*` 的 XML 没写重复调用契约（清单见 `f2-idem.txt`）。Redis 第二次传入不同连接串会被静默忽略；`AddServiceClientPipeline` 重复调用会叠加处理器 | 改代码：在 `<remarks>` 补契约（措辞沿用现有写法）；Redis 的行为不改，只如实写明 | 否 | 补用例：Redis 传入不同连接串、不同配置节；Pipeline 重复调用后处理器的数量 |
| P4-5 | `dependency-injection.md:32-34,39-42,89-93` 缺 `using …Registration;`，报 CS0246（已核实：示例只 using 了 `.Extensions`） | 改文档 | 否 | G-16 |
| P4-6 | `unit-of-work.md:124-130` 组件示例用了 `IRepository`，"对"的写法还漏了 `await`（已核实） | 改文档：改用 `dbContext.Orders.Add` 加 `await …SaveChangesAsync`，仓储写法挪到 ddd-struct.md | 否 | G-16 |
| P4-7 | `unit-of-work.md:148-152` 的 `HandleAsync` 少了 CancellationToken 参数，报 CS0535（已核实） | 改文档 | 否 | G-16 |
| P4-8 | `components/README.md:61,125-129,137` 依赖图有错边和缺边，文字说明自相矛盾 | 改文档：删掉手画的边，只保留分层说明，依赖以 csproj 为唯一事实 | 否 | check-docs-sync、check-markdown-anchors |
| P4-9 | `Leistd.MultiTenancy.Tests.csproj:26-27` 引用了 `Ddd.*`（已核实） | 改代码：相关用例迁到 `Leistd.Ddd.Infrastructure.Tests`，删除这两条引用 | 否 | 迁移前后用例数一致；G-13 |
| P4-11 | `AddOperationRecords` 有无参和委托两个重载（`DependencyInjection.cs:38,90`，已核实） | 改代码：合并为 `Action<…>? configure = null`。0.13.0 未发布，§36 本来就要求重新编译，额外代价只落在用命名参数 `configureOptions:` 调用的代码上，不属于"只为统一而破坏" | 是（二进制、命名参数）→ §36 | 传委托和不传委托两条路径都有用例，并测幂等 |
| P4-12 | `AddRemoteTenantConnectionResolution`、`AddHostSettings` 不符合单一入口形态 | 改规范：在 dev-guide §6.1 点名这两个例外。改代码就要在中间插入参数，会让按位置传参的调用静默错位 | 否 | 锚点检查 |
| P4-13 | 命名实例入口的 `configSectionPath` 可空变体没写进规范（dev-guide:258-261） | 改规范 | 否 | — |
| P4-14 | 9 处组件示例跨出了本组件的 csproj 闭包（如 exception-handling.md:34、notifications.md:77-80） | 改规范：§5.1 允许 `## 注册` 段中的宿主组合行调用兄弟组件的入口。改文档 2 处：aspnetcore-signalr.md:56 改用 `ICurrentUser`，multi-tenancy.md:74 改用 IClock 或改成文字链接 | 否 | G-17 |
| P4-15 | `realtime.md:93-117` 仍教宿主手写令牌中间件，而框架已有 `UseHubAccessToken()`（`Leistd.AspNetCore.SignalR/DependencyInjection.cs:130`，已核实） | 改文档：改为调用它，`notifications.md:230` 同步 | 否 | — |
| P4-16 | `operation-records.md:197` 业务规则失败抛了 `InvalidOperationException`，消息是中文 | 改文档：改为 `BusinessException`，消息用英文 | 否 | — |
| P4-17 | `aop.md:76`、`core.md:59` 命名空间写错 | 改文档 | 否 | check-docs-api-drift |
| P4-18 | `multi-tenancy.md:392` 少列一个 409 | 改文档 | 否 | — |
| P4-19 | `security.md:147-155` 少了 `Subject` | 改文档 | 否 | — |
| P4-20 | `SmtpEmailSender.cs:37-43` 在运行期重复检查启动期已拒绝的配置，还写死了默认配置节名 | 改代码：删除，由启动期验证器接替 | 否 | 删除验收做变异：只配 Username 时启动失败 |
| P4-21 | `RecurringJobSchedule.cs:20-23`、`GlobalExceptionOptions.cs:62-66` 手写范围守卫 | 改代码：改用 `ArgumentOutOfRangeException.ThrowIf*` | 否（消息文本会变） | 现有断言不比较消息文本；G-12 |
| P4-22 | `HubIdentityOptions.cs:26` 的 remarks 与示例矛盾 | 改代码（注释） | 否 | — |
| P4-23 | 控制面返回空响应体时得到 500（`RemoteTenantConnectionStore.cs:40-41`）；权限名含保留字符时抛 IOE（`PermissionDefinition.cs:130-134`） | 改代码：前者抛 `ServiceClientException(InvalidResponse)`，返回 502；后者抛 `ArgumentException` | 行为变化 → §37 加一行（调用方可见 500→502） | 新增 502 用例；保留字符用例改为断言 ArgumentException |
| P4-24 | `DefaultUnitOfWork.cs:343`、`MemoryLocalLock.cs:103` 的注释是英文 | 改代码 | 否 | — |
| P4-25 | 实现成员复述接口的 summary（`DataFilter.cs:50,64,72` 等） | 改代码：改为 `<inheritdoc/>` | 否 | — |
| P4-26 | 3 处"原先、曾经、此前"之类的过程叙事 | 改代码 | 否 | — |
| P4-27 | 约 15 处轮次记录（"N9 的回归守卫""上一轮""（P4）"），以及用例名 `The_former_placeholder…` | 改代码：改写为行为描述 | 否 | check-test-names；G-09 |
| P4-28 | 约 12 处复述代码的行内注释 | 改代码 | 否 | — |
| P4-29 | 测试根目录用例超过 10 个；子目录名与被测包不符；替身没放进 TestDoubles/ | 改代码 | 否 | 跑全集；G-13 |
| P4-30 | 3 组逐字相同的替身；死替身 `ListLogger` | 改代码：前三组上移到 `Leistd.TestBase`，`ListLogger` 删除 | 否 | 跑全集；G-12（CA1812） |
| P4-31 | `UseJsonRequestLocalization` 没有用例 | 改代码：补 TestServer 用例 | 否 | 新用例 |
| P4-32 | 约 12 个文件每条用例都新建宿主 | 改代码：没有逐测可变状态的改用 `IClassFixture`。改规范：§7.5 补"有逐测可变状态时可以每次新建" | 否 | 跑全集，记录改前改后耗时 |
| P4-33 | `DataFilterTests.cs:103-115` 靠真实时长，无法证伪 | 改代码：改用 `TaskCompletionSource`/`Barrier` | 否 | 把隔离注回成静态字段，用例应变红 |
| P4-37 | dev-guide §2（:112）与 §6.1（:269）冲突；§7.2 的文件命名措辞 | 改规范 | 否 | — |
| P4-38 | `common.props:37-38` 抑制 CS0436，已无作用（已核实） | 改代码：删除，并改写注释 | 否 | Release `--no-incremental` 全量构建 |
| P2-28（框架部分） | `framework/docs/components/notifications.md:109` 示例中的生命周期与模板定稿不一致 | 改文档：随模板改为 `TryAddTransient` | 否 | — |

#### 2.2 模板后端

| ID（合并） | 问题与证据 | 决定 | 破坏性 | 验证要求 |
| --- | --- | --- | --- | --- |
| P1-1 | 7 个写方法跨多次提交却没标 `[UnitOfWork]`：`UserAppService` 的 Disable/ResetPassword/ResetTwoFactor/Delete，`AuthAppService.ChangePasswordAsync`，`TwoFactorAppService` 的 Enable/Disable。`UserAppService.cs:686-696` 的注释声称"与写入同在一个工作单元"，而 `:277` 的注释承认没有 UoW（已核实；没有请求级的环境 UoW） | 改代码：标上 `[UnitOfWork]`。先在 PostgreSQL 上确认 OpenIddict `RevokeBySubjectAsync` 能否进入同一事务；进不去就把令牌撤销移到 AfterCommit 处理器，并如实改写注释 | 否 | PG 集成：撤会话步骤注入失败时，口令、启停、两步验证状态整体回滚，且没有成功审计；正常路径照旧撤会话、失效缓存、发出提醒 |
| P1-2 | 安全提醒在写入后直接调用 `ISecurityAlertPublisher`（`AuthAppService.cs:475`、`UserAppService.cs:376,436`、`TwoFactorAppService.cs:126,177`），P1-1 加上 UoW 后会先于提交发出。角色事件的时序已经正确（见第 0 节） | **需裁定 §3.1**，推荐：改为应用层本地事件加 AfterCommit 处理器，并在 §3.4 补一条规范 | 否 | 回滚时不发提醒；每种 kind 只发一次 |
| P1-3 | 角色名查重走默认的软删除过滤，而唯一索引 `IdentityEntityConfiguration.cs:46-52` 不排除 IsDeleted（`RoleAppService.cs:129`，已核实）。删除后重建同名角色会得到 500（推断，未复现） | 改代码：新增 `RoleDomainService.CreateAsync`，查重时禁用 `ISoftDelete`。**先写失败测试确认 500** | 否 | 创建→删除→重建，返回 `Role:NameAlreadyUsed`；跨租户同名仍然允许 |
| P1-4 ＋ P2-16 | 邮箱唯一性：`EmailVerificationAppService.cs:77-83` 直查，用 `ToLower()` 且不含软删除；同一件事有三个码，分别是 `User:EmailAlreadyUsed`（`UserAppService.cs:255`）、`User:EmailTaken`（`UserDomainService.cs:272`）、`Auth:EmailAlreadyUsed` | 改代码：发码前的检查改调 `IsEmailAvailableAsync`；大小写口径按唯一索引的实际语义统一。合并错误码见 **§3.3** | 是（合并码）→ §39 | 已删除用户占用的邮箱，在三个入口的结论一致；大小写不同的同一地址结论一致；`ApiExceptionMappingsTests` 的 InlineData；同步 `_mock` |
| P1-5 | 两步验证的已启用/未启用、邮箱已验证这些前置条件只写在应用服务里，`User.EnableTwoFactor` 会静默覆盖密钥，`ReplaceRecoveryCodes` 对未启用账号也照写（`User.cs:358-381`、`TwoFactorAppService.cs:186-190,218-225`，已核实） | 改代码：在实体中守卫并抛码；码值不变，常量移到 Domain `Errors/` | 否 | 实体单测；三个端点的拒绝码与 HTTP 状态不变 |
| P1-6 | 默认角色分配：领域服务读 Role 聚合（`UserDomainService.cs:381-396`），应用服务另有一套实现（`UserAppService.cs:212-214,599-600`），已核实 | **需裁定 §3.2**，推荐改代码 | 否 | 注册、首次外部登录、管理员不指定角色建用户，三条路径都得到默认角色，签发的会话带角色声明 |
| P1-7 | `UserDomainService.GetUserRoleNamesAsync`（`:401-413`）是纯查询投影 | 改代码：移到 Application 层的查询协作类型 | 否 | 现有 `SessionRoleClaimTests`、`UserRolesQueryTests` |
| P1-8 | `CreateUserWithRolesAsync`、`AssignRolesToUserAsync` 全仓零引用（已核实） | 改代码：删除 | 否 | 编译 |
| P1-9 | 停用两步验证时直接调用 `passwordHasher.VerifyPassword`（`TwoFactorAppService.cs:148`，已核实） | 改代码：新增 `UserDomainService.VerifyCurrentPassword` | 否 | `TwoFactorTests`、`ReauthenticationLockoutTests` |
| P1-10 | 应用服务里做 DTO 校验：`OpenApplicationAppService.cs:140-145,304-314,438-446`；`UserAppService.cs:85,122-127`；`AuthAppService.cs:198-206,497-503` | 改代码：改用特性或 `IValidatableObject`；删除 `OpenApp:ClientIdRequired` 等码；`AvatarPolicy.EnsureValid` 在管理员更新路径上保留 | 是：业务码改为 400 + `errors[].field` → §39（见 §3.3） | 集成测试：400 且带 field；`_mock/api/open-application.ts:107` 同步 |
| P1-11 | Controller 里做多来源拼装（`ResourceAuthController.cs:37-50`、`AuthController.cs:127-138`） | 改代码：新增对应的应用服务方法 | 否（字段与值不变） | `ResourceBrowserSessionTests`、认证契约测试 |
| P1-12 ＋ P2-28 | 中间件自己开工作单元并直调领域服务（`ResourceUserProvisioningMiddleware.cs:30-85`）；收件人解析在 Api 层直查仓储，还判定"只发已验证邮箱"（`UserEmailRecipientResolver.cs:19-22`，已核实）；两个无状态服务登记为 Scoped | 改代码：新增应用层 `ResourceUserProjector`（requiresNew，加一次重试）；收件人解析移到 `Application/Notifications`，改为 Transient；`HttpRequestClientInfo` 改为 Transient。改规范：`coding-backend.md:232` 的 `Notifications/` 一行改为"只放依赖宿主资源的通知扩展点实现" | 否 | `ResourceUserProjectionTests`（首访、刷新、并发重试）；未验证邮箱不发；注册测试断言生命周期 |
| P1-13 ＋ P2-1 | 测试发信端点只有类级 `[Authorize]`，权限在应用服务里以业务码 403 模拟（`SettingController.cs:16-25`、`EmailSettingsAppService.cs:33-35`，已核实；前端不按这个码分支） | 改代码：action 加 `[Authorize(Policy = PermissionConstant.Settings.Default)]`；删除 `IPermissionChecker` 判定，以及 `AppSetting:ManagePermissionRequired` 的常量、映射和词条（`!Email` 形态下它只剩一条死映射）；保留 `TestEmailHostOnly` | 是：403 响应不再带 `code` → §39 | 无权限时得到管道 403，不带 code，且由 `ApiAuthorizationResultHandler` 补记；租户管理员得到 `AppSetting:TestEmailHostOnly`；G-03 能拦住回退 |
| P1-14 | 提升超管绕过了宿主守卫（`SystemInitializer.cs:188-192`） | 改代码：新增 `UserDomainService.PromoteToSuperAdmin`，`MarkAsSuperAdmin` 改为 internal | 否 | 在租户上下文中提升超管抛 IOE；`DefaultAdminBootstrapTests` |
| P1-15 | 写后回查（`AuthAppService.cs:431,514,558`；`OpenApplicationAppService.cs:234`） | 改代码 | 否 | 响应体不变 |
| P1-16 | 会话过期判据写了 5 处；外部 URL 判定写了 2 处 | 改代码：新增 `UserSession.ActiveAt` 表达式；外部 URL 统一用 `AvatarPolicy.IsExternalUrl` | 否 | 边界用例：`LastSeen + idle == now` 时 5 处判定一致 |
| P1-17 | `RoleListChangedEvent` 的无参构造隐式取系统时间 | 改代码：由发布方传入 `clock.Now` | 否 | — |
| P2-2 | `ExternalAuthController.cs:160` 写了字面量码 `"ExternalAuth:InvalidState"`（已核实；P1 写的 `:142` 有误） | 改代码：改用常量 | 否（码值不变） | G-06 |
| P2-3 | 用户名分配失败复用了 `User:UsernameTaken`，界面显示"用户名已存在"（`ExternalAuthDomainService.cs:318-321`，已核实） | 改代码：新增 `ExternalAuth:UsernameAllocationFailed`，不带 Username 数据 | 是（码变）→ §39 | 单测：全部候选用户名都被占用 |
| P2-4 | 开放应用的 4 个写操作没有操作记录（`OpenApplicationController.cs:51,63,76,86`）；**另**发现角色更新没有 `role.updated` | 改代码：新增 5 个动作码并在 Provider 登记，成功后调用 `RecordSucceededAsync`，补前端中英句子模板 | 否（新增记录） | `AuthorizationAndAuditingTests`；`check-operation-action-i18n.py` |
| P2-5 | 删除不存在的资源：角色、会话、解绑返回成功；用户（`UserAppService.cs:460`）、开放应用（`OpenApplicationAppService.cs:239`）返回 404。`coding-common.md:50-52` 只约束外部资源 | **需裁定 §3.4**，推荐统一为成功 | 推荐方案：是 → §39 | 重复 DELETE，第二次返回 200 且不新增记录 |
| P2-6 | 组合期读值校验 RemoteIdentity（`RemoteTokenAuthenticationExtensions.cs:26-37,76-77`） | 改代码：改为 Options 加 `ValidateOnStart`，OpenIddict 与 OIDC 处理器经 `Configure<IOptions<…>>` 延后取值。若实测不可行，改规范在 §7 登记例外，代码保留 | 否 | `DeploymentSafeguardsTests` 改为断言 `OptionsValidationException`；OIDC e2e |
| P2-7 | `AddInfrastructureServices`、`AddPersistenceServices` 没有结果、生命周期和重复调用测试（现有只有 `ServiceRegistrationTests.cs:140-161` 的顺序测试，已核实） | 改代码（补测试） | 否 | 新测试：分别在空配置和带 Redis 的配置下 |
| P2-8 | `service-invocation.md:23` 无条件写了 whoami，代码只在 `LocalIdentity` 下生成（`ServiceInfoController.cs:31`，已核实） | 改代码：去掉 `#if`，Resource（Token Exchange 的主要接收方）也提供；同步 template.json 和 Client | 否（新增端点） | `ServiceInvocationTests` 的 resource 场景；OIDC e2e 的 Token Exchange |
| P2-13 | `PermissionDefinitionProvider.cs:89` 的注释过时 | 改代码 | 否 | — |
| P2-14 | `auth.md:81` 没写"带 Authorization 头的 API 写请求跳过来源检查"（`BrowserOriginMiddleware.cs:24`） | 改规范 | 否 | 补一条带头写请求的 `BrowserOriginTests` 用例 |
| P2-15 | `ExternalAuth:ProviderNotSupported` 零引用 | 改代码：删除常量和词条 | 否（不可观察） | G-06 |
| P2-17 | `SecurityErrorCodes` 只被 Application/Auth 使用，却放在 Domain/Shared | 改代码：移到 `Application/Auth/Errors/`，码值不变 | 否 | 编译 |
| P2-18 | 不可达的防御分支抛业务码：`TenantImpersonationAppService.cs:65-66`（已核实）、`ConnectController.cs:262` | 改代码：改为 IOE，删除这 2 个码 | 否（不可达） | — |
| P2-19 | `TwoFactorSetupEnforcementMiddleware`、`UserAppService.cs:565-568` 用业务码拒绝 | 改规范：api.md §4 登记这两类例外（界面靠稳定码路由；附加权限取决于请求体） | 否 | — |
| P2-20 | 协议结果用 `IActionResult`；`/connect/*` 端点（规范 api.md:31-33、coding-backend.md:132 只写了"多种"，已核实） | 改规范 | 否 | — |
| P2-21 | `ServiceInfoController` 同步返回 DTO | 改规范：补"无 I/O 的端点可以同步返回" | 否 | — |
| P2-22 | 开放应用排序末尾追加 `ClientId`（`coding-backend.md:119` 写的是追加 Id） | 改规范：改为"末尾追加唯一键" | 否 | — |
| P2-23 | `api.md:148`"最低实际使用层"有按类、按码两种读法 | 改规范：同一模块的码集中在用到它的最低层的一个文件里 | 否 | — |
| P2-24 | 约 31 处孤立的 `;` 行（如 `UserAppService.cs:326`；P7 也附带报告了） | 改代码 | 否 | 编译；不做闸门（收益低） |
| P2-25 | `WarningLogCapture.cs` 手写日志替身 | 改代码：改用 FakeLogger | 否 | 3 个使用它的测试类 |
| P2-26 | 测试目录分工不符 | 改代码，同步 template.json 和 `scripts/template-matrix-scenarios.ps1` | 否 | 矩阵 |
| P2-27 | `WebHostExtensions.cs:33` 用了 `AddSingleton`；重复调用时健康检查重名 | 改代码 | 否 | 注册测试：调用两次后仍能解析 `HealthCheckService` |
| P2-29 | 9 处仓储和 Query 变量命名不符 | 改代码 | 否 | 编译 |
| P2-30 | `ApiResource.cs:1` 的命名空间不跟随目录 | 改代码 | 否 | check-using-guards；G-02 |
| P2-31 | 互斥与解密捕获条款的边界没写清 | 改规范 | 否 | — |
| P2-32 | §3.8 写"统一小写"，与 camelCase 转换器在多词枚举上冲突 | 改规范：改为与 JSON 命名策略一致 | 否 | — |
| P2-33 | 4 处为消除同名歧义而内联全名 | 改代码：改用 using 别名；改规范补别名写法 | 否 | G-02 |
| P7-3 | 模板先例"实体给谓词、应用服务抛码"让审查者把植入的违规当成了既有风格（P7 只审查场景） | 改规范：coding-backend §3.4 写明判据——规则判定在实体或领域服务，应用服务只按用例选码抛出；在应用服务里组合实体字段做判定属于违规。P1 判 `CanBeManagedBy` 等写法合规，与此一致 | 否 | P7 只审查场景复测 |
| P1（附带） | 未使用的 `using Leistd.MultiTenancy.*`（如 `Role.cs:2-6`） | 改代码：清理 | 否 | 编译 |

#### 2.3 模板前端

| ID | 问题与证据 | 决定 | 破坏性 | 验证要求 |
| --- | --- | --- | --- | --- |
| P3-1 | dialog、sheet 内容和 sidebar-trigger 的读屏文案改走 `injectHlmA11yLabels()`，新增的 `utils/…/hlm-a11y-labels.token.ts` 没有登记（已核实该文件存在）；这条定制曾被上游升级覆盖过一次 | 改规范：在 frontend-spartan 登记表加一行；补 sheet 的 a11y 断言 | 否 | `a11y-labels.spec.ts` 覆盖三个组件；G-10 |
| P3-2 | badge 新增 `success` 变体、destructive 暗色 alpha，均未登记（`hlm-badge.ts:14-17`，4 处 `variant="success"`）；`frontend-ui.md:40` 的例子只列了 4 个变体 | 改规范：登记表补这两条；§2.3 的例子加上 `success`，写明只用于"启用、已验证、成功" | 否 | 编译；G-10 |
| P3-3 | 按状态码 409 分支（`permission-grant-dialog.ts:293`，已核实） | 改代码：`API_ERROR_CODES` 加 `Permission:ConcurrencyConflict`，按 code 分支 | 否 | 409 用例带 code；补"409 但 code 不同时只 toast"的反例；G-07 |
| P3-4 | 6 个 DTO 在 `shared/models/permission.ts:87-125`，`NotificationOutputDto` 在 `signalr-service.ts:50`（已核实） | 改代码：DTO 移到 `shared/dtos/*.dto.ts`；`PERMISSIONS` 常量移到 `shared/utils/permissions.ts` | 否 | lint、build、现有 spec；G-07 |
| P3-5 | 6 个前端在用的端点没有 Mock：模拟登录 3 个、`/auth/two-factor`、`link/complete`、`reset-two-factor`（`_mock` 中零命中，已核实） | 改代码：在 `_mock/api/*.ts` 补处理器，含 403 和 404 形态；刻意不 Mock 的端点在清单里写明 | 否 | 相邻 `_mock/api/*.spec.ts`；G-11 |
| P3-6 | `frontend-ui.md:119,128,136` 仍写「个人」组；代码已删除 `groupPersonal`（已核实） | 改规范 | 否 | — |
| P3-7 | 构造函数里有首次加载与流程（10 处），见第 0 节更正 | **需裁定 §3.6** | 否 | 补用例：构造后不发 POST，`ngOnInit` 后发一次 |
| P3-8 | `zoned-time.ts` 没有 spec；`operation-records.ts`（约 600 行）没有 spec | 改代码：补 spec（夏令时、整秒边界） | 否 | 新增 spec |
| P3-9 | 7 个表单组件没有 spec | 改代码：补校验、防重复提交、400 回填、失败 toast 的用例 | 否 | 新增 spec |
| P3-10 | role/tenant-service 有参数映射却没有 spec；`popover-aria`、`confirm-dialog` 没有 spec | 改代码：补 spec；`confirm-dialog` 若由 confirm-service 用例覆盖，则在 spec 注释中说明 | 否 | 新增 spec |
| P3-11 | 字号档位违规（landing 用 30px、卡片标题 24px 却是 500 字重、缺 `tracking-tight`，空态标题等） | 改代码：页面标题统一为 `text-2xl font-semibold tracking-tight`；landing 主标题降到 24px，不新增档位 | 否 | FE5 B-10 |
| P3-12 | 规范要求 16px 标题用 600，而 Spartan 4 个标题组件默认是 500 | 改规范：改为 `text-base font-medium`（组件默认，不新增 helm 定制）；删掉 3 处手工加的 `font-semibold` | 否 | FE5 B-10 |
| P3-13 | 约 60 处兄弟元素靠外边距隔开；空态没用 `gap` | 改代码 | 否 | FE5 B-10、B-14 |
| P3-14 | 3 个页面外框 padding 不符 | 改代码 | 否 | FE5 B-10 |
| P3-15 | `resource-login.html:6-10` 用原生按钮；侧栏图标块用 `rounded-md` | 改代码 | 否 | FE5 B-11 |
| P3-16 | 分类标签用了主色 `default` | 改代码：改为 `secondary` 或 `outline` | 否 | FE5 B-10 |
| P3-17 | `workspace-nav.html:97` 当前项写法偏离；`default-header.html:72` 用 5 个 `dark:` 抵消变体 | 改代码：改用 ghost 变体，删掉 `dark:` | 否 | FE5 B-3、B-4 |
| P3-18 | 原生 `<label>`，字段说明做成独立方框 | 改代码：改用 Field 族 | 否 | P3-9 的 spec 断言 label 关联 |
| P3-19 | `account-service.ts:104-133` 在 `tap` 里回写 `AuthService` | 改代码：回写移到调用方 | 否 | 调整 `account-service.spec.ts` |
| P3-20 | 图像遮罩、二维码底色、品牌 SVG 用了具体色值 | 改规范：在 §2.2 列出这三类例外 | 否 | — |
| P2-37 | `public/i18n/operationRecords`、`openApp` 是 camelCase 目录 | 改规范：project-structure §4 登记"i18n scope 目录名跟随 scope 名"。改目录名就要改全部运行时键前缀，没有收益 | 否 | — |
| 浏览器验证 | P3 矩阵中 6 行"待浏览器验证"，B-1～B-14 未执行 | 执行（工作包 FE5） | — | 每个用例都带证伪反例（见 `p3-findings.md` §3） |

#### 2.4 规范与 Skill（只改文档，不伴随代码）

| ID | 问题与证据 | 决定 | 验证要求 |
| --- | --- | --- | --- |
| P2-12 | `docs/template/development-guide.md:179` 说 `RoleAppService.DeleteAsync` 刻意不做成一个事务；代码 `:192` 实际有 `[UnitOfWork]`（已核实） | 改为如实描述。注意该文件已有一处未提交改动，要在它的基础上修改 | check-markdown-anchors |
| P4-T1 | `template/docs/standards/service-invocation.md:11-12` 仍是已删除的 `AddXxxServiceClient(builder.Configuration)` 写法（已核实；P2 矩阵漏判，P4 转交） | 改为现行入口 | 生成后 grep 不到旧写法 |
| P4-T2 | `template/docs/standards/testing.md:170-171` 写死了 `python3`（README 部分归 2.6 的 P2-38） | 改为跨平台写法 | — |
| P2-35（结构部分） | project-structure §1 缺条件生成的 `scripts/`，§3 缺 `libs/` | 改规范 | 读取成本检查 |
| P7-1 | `quality.md:6` 的审查优先项里没有"规范符合性" | 改 Skill：加入"与项目规范的符合性（分层与职责归属、依赖方向、命名与目录）"，并要求每条 finding 指出违反的规范条目 | validate-skills；P7 复测 |
| P7-2 | `documentation.md:45` 的"同步精简"范围不清；`SKILL.md:39` 没把"读到与代码冲突的规范"列为候选 | 改 Skill：范围包括写入的目标文件，以及本任务读过、与改动代码相关的规范；实施任务修正，只读任务报告 | validate-skills；P7 复测 |

#### 2.5 闸门（仓库脚本自身的缺陷）

| ID | 问题与证据 | 决定 | 验证要求 |
| --- | --- | --- | --- |
| P4-10 | `scripts/check-clock-access.py:98-110` 没有把 relpath 统一成 `/`，Windows 上必然失败（已核实） | 改代码：`.replace(os.sep, '/')` | 自检补一条反斜杠路径；G-14 |
| P4-34 | 6 个入口 ps1 缺 shebang | 改代码；规范写明只被点源加载的库脚本不要求 | G-14 |
| P4-35 | 3 个脚本写死 `python3`；约 54 处读写未指定 encoding | 改代码：改用 `sys.executable`，读写补 `encoding="utf-8"` | 用 `-X warn_default_encoding` 跑各自检 |
| P4-36 | `template/scripts/check-i18n.py:274,286` 没用 `as_posix()`（随模板分发） | 改代码（并入 G-06 拆分） | 自检补一个两级 scope 的夹具 |

#### 2.6 模板分发缺陷

| ID（合并） | 问题与证据 | 决定 | 验证要求 |
| --- | --- | --- | --- |
| P2-9 | `deploy/docker-compose.yml:120-128` 的 `ExternalAuth__*__RedirectUri` 没有读取方（`ExternalAuthOptions` 只有 ClientId、ClientSecret，已核实），注释描述的还是旧的前端回调流程 | 删除这两个键；注释改为登记 `/api/v1/external-auth/{p}/signin` | 生成检查；G-08 |
| P2-10 | 生产 compose 和 `.env.example` 里没有 SMTP、`VerificationCodes:Key`、外部登录凭据（已核实），按文档部署时发信会打到 `localhost:1025` | Email 条件下补 `Leistd__Email__Smtp__*` 并列入 `.env.example`；ExternalLogin 条件下补提供商凭据；deploy README 的"缺配即失败"清单补键 | Email、ExternalLogin 场景生成；G-08 |
| P2-11 | `appsettings.json:68` 的 Cors 注释说"前端部署在另一个源时列出它的地址"，与同源契约相反（已核实） | 改注释 | — |
| P2-34 | compose `:134` 的 `Serilog__…__Override__MyProject` 匹配不到 `CompanyName.ProjectName.*` 日志类别 | 删除 | G-08 |
| P2-35 ＋ P2-36 ＋ P7-6 | `template/VERSION`（1.11.6）没有读取方，却进入每个生成项目；`Directory.Build.props:4-7`、`Directory.Packages.props:11-12` 写的是仓库维护事实；部署后无从确认实际版本 | 删除 `VERSION`；注释改为生成项目视角（需确认 release.yml 替换 `LeistdFrameworkVersion` 的写法不受注释影响）；deploy README 补"如何确认已部署版本"（镜像 revision 标签和 service-info） | 生成文件集合断言；G-08、G-09 |
| P2-38 ＋ P4-T2 | `README.md:209-220` 的验证一节漏了 `npm test` 和 `check-operation-action-i18n.py`；`:218` 写死了 `python3` | 改文档 | 生成文档检查 |
| P7-4 | `Dockerfile:40` 注释说私有源可放在"项目根目录 NuGet.Config"，但构建只 `COPY backend/`（`:41`，已核实） | 注释改为"放在 `backend/NuGet.Config`" | 部署场景复测 |
| P7-5 | 生成项目 `docs/README.md:1` 的标题是字面量 `{ProjectName}`，模板不会替换（已核实）；`service-invocation.md:80`、`project-structure.md:26-28` 同理 | 改用模板符号 `MyProject` 或 `CompanyName.ProjectName` | 生成后 grep 不到 `{ProjectName}` |
| P7-7 | deploy README 的"本地验证"没说明 HTTPS 从哪里来（推断：`__Host-` Cookie 与 OpenIddict 生产环境都需要 HTTPS） | **先核实前提**：在生产配置下只做 HTTP 的本机 compose，确认登录是否失败。成立就补本机 HTTPS 做法，或写明"只验证健康端点时可以不配" | 部署场景复测 |
| P7-8 | compose 中的 migrator 写死了 `--apply`，"先预演"没有现成入口 | deploy README 给出预演命令 | 部署场景复测 |

### 3. 需裁定

**裁定结果**：3.1–3.6 均采纳推荐方案。3.4 选择"删除不存在的资源一律成功"：与角色、会话、解绑已有的幂等行为一致，客户端重试安全；契约变化写入升级清单 §39。

#### 3.1 P1-2 安全提醒的发布方式

- A：由实体发出本地事件（`UserCredentialChangedEvent(kind)`），提醒处理器订阅。缺点：本人修改与管理员重置这类区分属于用例语义，要塞进实体方法的参数里。
- B（**推荐**）：应用服务按用例发布应用层本地事件（如 `SecurityAlertRequestedEvent(kind)`），处理器在 AfterCommit 阶段调用 `ISecurityAlertPublisher`。框架 `UnitOfWorkEventHandlerAttribute` 已默认 AfterCommit，角色事件就是这么做的，所以第 0 节说它的时序已经正确。规范 coding-backend §3.4 补一句："副作用由用例语义决定时，应用服务发布本地事件，处理器在提交后执行；纯实体变更的副作用由实体发出。"登录锁定、新设备提醒保持现状。
- C：保持直接调用，但规定这些方法不得处于工作单元内。这与 P1-1 冲突，不可取。

#### 3.2 P1-6 默认角色由谁分配

- A（**推荐**）：改代码。新增应用层 `DefaultRoleAssigner`，注册、首次外部登录、管理员建用户三处共用；`ExternalAuthDomainService.FindOrCreateUserAsync` 只返回"是否新建"。规范 `coding-common.md:30` 明确写着"领域服务不引外聚合"，而且两套实现不论选哪种方案都必须合并。
- B：改规范，允许领域服务为新建实体读取另一个聚合的配置标记。这会开一个例外，以后难以收住。

#### 3.3 错误码收敛与契约变更（P1-4/P2-16、P1-10、P1-13/P2-1、P2-3）

核实结果：前端 `API_ERROR_CODES` 只分支 3 个 Auth 码，不依赖下面任何一个码，受影响的只有 `_mock`。

| 变更 | 方案 |
| --- | --- |
| 邮箱占用三个码合并为 `User:EmailTaken`（`Auth:EmailAlreadyUsed` 也合并：同一事实；匿名发码本来就会暴露占用，不新增枚举面） | 合并 |
| `OpenApp:ClientIdRequired` 等服务内校验码 → 400 `errors[].field` | 改为 DTO 校验 |
| `AppSetting:ManagePermissionRequired` → 管道 403，不带 code | 改为策略授权 |
| 用户名分配失败 → `ExternalAuth:UsernameAllocationFailed` | 新码 |

- A（**推荐**）：在 0.13.0 一并完成，写入 §39，逐条列出旧码到新形态的映射。
- B：只做内部重构，保留旧码。重复的码和以业务异常模拟授权这两个问题就会留下来，与终局原则相悖。

#### 3.4 P2-5 删除不存在资源的策略

- A（**推荐**）：统一为"不存在即成功"，返回 200 且不写操作记录；用户、开放应用由 404 改为 200。HTTP DELETE 本身语义上是幂等的，`coding-common.md` §3.6 的标题和 api.md §1 的"幂等策略"都改为同时覆盖本地资源。属于破坏性变更 → §39。需核对前端删除流程不依赖 404（FE3a 的 Mock 同步）。
- B：统一为 404，角色、会话、解绑改为 404。这样重试就不再安全。
- C：保持现状，在规范里按资源列出。规则就成了逐个登记，没有统一的判据。

#### 3.5 P4-2 审计保留期 `RetentionDays`

- A（**推荐**）：改为 `int?`，`Enabled=true` 而未填时由验证器报 `{path}:RetentionDays is required when Enabled is true`。这与 dev-guide:316 一致（期限受法律和合同约束，类库无从知道）。破坏性：只设了 Enabled 的宿主会在启动时失败，属性类型也变了 → §36。模板已显式写 365，不受影响；实施时还要核对系统设置「审计」面板的设置定义是否另带默认值。
- B：改规范，认为归档不算删除，可以给默认值。但归档表同样有清理语义，这个理由不成立。

#### 3.6 P3-7 构造函数中的首次加载

- A（**推荐**）：改规范。`coding-frontend.md:74` 写明"进入页面时的首次只读查询及其订阅可以放在构造函数"（现有措辞"随 DestroyRef 释放的订阅及其首次读取"已经接近），写操作、多步流程和清理会话放 `ngOnInit`。据此只移动 `two-factor-setup.ts:85-86`（POST）和 `login.ts:153-156`（`sessionContext.clear()`），同时修正 `layout-service` 中与规范不一致的注释。
- B：10 处全部移到 `ngOnInit`。这和 signal、zoneless 的写法相悖，收益也很弱。

### 4. 工作包

同一时刻并行的包，文件集合互不相交。共用文件的包标为串行（→），后一个必须在前一个合入后才开始。每包规模约 60 次工具调用以内（下表给出估计）。凡有需裁定的项，都按推荐方案编排；裁定结果不同时，只影响标出的包。

**依赖总览**

- 第一波可并行：F1、F2、F3、F4、F5、G1a、TB1、TB4、FE1a、S1a、S1b。
- Framework：F1、F2、F3 → F6a → F6b → G-FW；F1 和 F4 → G2；F1 完成后打包，TB3a 才能删除模板中的 `Error:Forbidden` 副本。
- 模板后端链：TB1 → TB2a → TB2b → TB3a → TB3b → TB3c；TB5 在 TB2a 之后，与 TB2b 及后续并行，但 TB3c 必须在 TB5 之后；G-TB 在 TB3c、TB4、S1a 之后。
- 模板前端链：FE1a → FE1b → FE2 → G-FE；FE3a 在 FE2 和 TB3b 之后，然后是 FE3b；G-X 在 FE3a 和 S1b 之后；FE5 在全部前端包和后端链之后。
- 收尾：G1b 在 F6b、F3、G2、G-X、G-TB、TB4 之后；Z 最后。

| 包 | 规模 | 文件（独占） | 条目 | 验证命令 | 依赖 |
| --- | --- | --- | --- | --- | --- |
| **F1** 选项校验与随包译文 | ~50 | `framework/components/unit-of-work/Leistd.UnitOfWork.Core/{DependencyInjection.cs,Options/**}`、`…UnitOfWork.EntityFrameworkCore/Database/DbContextProvider.cs`；`framework/components/operation-records/Leistd.OperationRecords.Core/{DependencyInjection.cs,Errors/**,Resources/**,*.csproj}`、`…OperationRecords.EntityFrameworkCore/Options/**`；`framework/tests/components/{unit-of-work,operation-records}/**`；`framework/docs/components/{unit-of-work,operation-records}.md` | P4-1、P4-2（按 §3.5）、P4-3（框架部分）、P4-11、P4-6、P4-7、P4-16 | `dotnet test` 两个测试项目；`dotnet build framework -c Release`；`pwsh framework/build/check-docs-sync.ps1`；`check-docs-api-drift.ps1` | 无 |
| **F2** 异常类型与守卫 | ~45 | `Leistd.Email.Smtp/SmtpEmailSender.cs`、`Leistd.BackgroundJobs.Core/Recurring/RecurringJobSchedule.cs`、`Leistd.ExceptionHandling.Core/Options/GlobalExceptionOptions.cs`、`Leistd.MultiTenancy.ServiceClient/Stores/RemoteTenantConnectionStore.cs`、`Leistd.Authorization.Core/Definitions/PermissionDefinition.cs`，以及 P4-28 涉及的 `SettingDefinition.cs`、`OperationActionDefinition.cs`、`DbContextExtensions.cs`、`AmbientContext.cs`、`EfCoreTransactionApi.cs`；`framework/tests/components/{email,background-jobs,exception-handling,authorization}/**`、MultiTenancy.Tests 中 `RemoteTenantConnectionStore` 的用例文件 | P4-20、P4-21、P4-23、P4-28 | 受影响测试项目；Release 构建 | 无 |
| **F3** 注册契约与注释 | ~60 | 全部 `framework/components/**/DependencyInjection.cs`（UoW.Core、OperationRecords.Core 由 F1 负责，除外）；`DefaultUnitOfWork.cs`、`MemoryLocalLock.cs`、`DataFilter.cs`、`NotificationRecord.cs`、`PermissionGrantRecord.cs`、`IOperationActionDefinitionProvider.cs`、`ITenantDatabaseEnumerator.cs`、`OperationRecord.cs`、`HubIdentityOptions.cs`；新增用例文件：`Lock.Tests` 中的 Redis 重复调用、`ServiceClient.Tests` 中的 Pipeline 重复调用 | P4-4、P4-22、P4-24、P4-25、P4-26、P4-27（src 部分） | Release 构建（CS1574）；两个新用例 | 无 |
| **F4** 组件文档 | ~35 | `framework/docs/components/*.md`（unit-of-work、operation-records 除外）、`framework/docs/components/README.md` | P4-5、P4-8、P4-14（文档）、P4-15、P4-17、P4-18、P4-19、P2-28（框架部分） | check-docs-sync、check-docs-skeleton、check-docs-api-drift、check-markdown-anchors | 无 |
| **F5** 框架规范 | ~20 | `docs/framework/development-guide.md`、`docs/framework/quality-assurance.md` | P4-12、P4-13、P4-14（规范）、P4-32（规范）、P4-34（规范）、P4-37 | check-markdown-anchors、check-docs-sync | 无 |
| **F6a** 测试结构 | ~55 | `framework/tests/**`、`framework/shared/Leistd.TestBase/**`、`framework/common.props` | P4-9、P4-29、P4-30、P4-38 | 框架测试全集，迁移前后用例数一致；Release `--no-incremental` | F1、F2、F3 |
| **F6b** 测试质量 | ~45 | 同 F6a | P4-27（测试部分）、P4-31、P4-32（代码）、P4-33 | 全集；P4-33 变异应变红；check-test-names | F6a |
| **TB1** 领域守卫与查询归位 | ~60 | `template/backend/src/**/Domain/{Users,Auth,Roles}/**`、`Application/{Auth,Users,Roles,Initialization}/**`、`Domain/DependencyInjection.cs`、`Application/DependencyInjection.cs`；相关测试 | P1-3（先写失败测试）、P1-5、P1-7、P1-8、P1-9、P1-14、P1-16 | `dotnet test`（Unit 与 Integration）；`pwsh scripts/test-template-matrix.ps1 -Tier pr` | 无（链首） |
| **TB2a** 默认角色与宿主协作归位 | ~55 | 同 TB1，加上 `Api/Middlewares/ResourceUserProvisioningMiddleware.cs`、`Api/Notifications/**`、`Api/Hosting/NotificationExtensions.cs`、`Api/Auth/LocalSessionAuthenticationExtensions.cs`、`Api/Controllers/{ResourceAuth,Auth}Controller.cs`、`Application/Notifications/**` | P1-6（按 §3.2）、P1-11、P1-12＋P2-28（代码）、P1-17 | 同上；`ResourceUserProjectionTests`；resource 场景 | TB1 |
| **TB2b** 事务边界与提交后提醒 | ~45 | 同 TB2a 的 Application 部分 | P1-1、P1-2（按 §3.1）、P1-15 | `dotnet test`；`pwsh scripts/test-template-postgresql-e2e.ps1`（真实库事务） | TB2a |
| **TB3a** 错误码收敛（破坏性） | ~60 | `**/Errors/*ErrorCodes.cs`、`Api/Hosting/ExceptionMappings/**`、`Api/Resources/{en,zh-CN}.json`、`SettingController.cs`、`EmailSettingsAppService.cs`、`ExternalAuthController.cs`、`ConnectController.cs`、`TenantImpersonationAppService.cs`、`EmailVerificationAppService.cs`、`ExternalAuthDomainService.cs`；`frontend/_mock/api/{user,auth}.ts` 中的码值；`ApiExceptionMappingsTests.cs`、`EmailSettingsTests.cs` | P1-4＋P2-16、P1-13＋P2-1、P2-2、P2-3、P2-15、P2-17、P2-18；删除模板中的 `Error:Forbidden` 副本 | `dotnet test`；`python template/scripts/check-i18n.py --self-test`；矩阵 pr | TB2b，以及 F1 已打包（按记忆"改了上游必须重做下游产物"：pack、清缓存、验证包内资源） |
| **TB3b** 入参校验、删除幂等与审计 | ~55 | `Application/{OpenApplications,Users}/**/Dtos/**` 与对应 AppService、`Application/OperationRecords/Provider/**`、`Api/Controllers/{OpenApplication,Role,User}Controller.cs`、`frontend/public/i18n/operationRecords/*.json`、`frontend/_mock/api/open-application.ts` | P1-10、P2-5（按 §3.4）、P2-4（含 role.updated） | `dotnet test`；`check-operation-action-i18n.py`；矩阵 pr | TB3a |
| **TB3c** 机械清理 | ~40 | 后端 src 与 tests 全域（链末，独占） | P2-24、P2-25、P2-29、P2-30、P2-33（代码）、P2-13、未使用的 using | `dotnet build`；`check-using-guards.py`；矩阵 pr | TB3b、TB5 |
| **TB4** 部署与分发 | ~45 | `template/deploy/**`、`template/docs/deploy/README.md`、`template/Dockerfile`、`template/README.md`、`template/VERSION`、`template/backend/Directory.Packages.props`、`Api/appsettings.json`（只改注释） | P2-9、P2-10、P2-11、P2-34、P2-35（VERSION 与 README 树）、P2-36（Packages.props）、P2-38＋P4-T2（README）、P7-4、P7-6、P7-7（先核实）、P7-8 | `python3 scripts/test-template-generation.py`；在 Email、ExternalLogin 场景下生成并检查 compose；`docker compose config` | 无 |
| **TB5** 宿主配置与测试结构 | ~55 | `Api/Auth/RemoteTokenAuthenticationExtensions.cs`、`Api/Hosting/WebHostExtensions.cs`、`Api/Controllers/ServiceInfoController.cs`、`Client/IMyProjectClient.cs`、WhoAmI DTO、`.template.config/template.json`、`scripts/template-matrix-scenarios.ps1`、`UnitTests/Registration/ServiceRegistrationTests.cs`、`DeploymentSafeguardsTests.cs`、`ServiceInvocationTests.cs`，以及 P2-26 迁移的 3 个测试类 | P2-6、P2-7（含 P2-28 的生命周期断言）、P2-8、P2-26、P2-27 | `dotnet test`；矩阵 pr（含 resource 场景）；`pwsh scripts/test-template-oidc-e2e.ps1` | TB2a |
| **FE1a** 字号、色板与组件用法 | ~50 | `template/frontend/src/**/*.html`，以及 `open-application-table.ts`、`faceted-filter.ts`、`workspace-nav.*`、`default-header.*`、`resource-login.*` | P3-11、P3-12（代码）、P3-14、P3-15、P3-16、P3-17 | `npm --prefix template/frontend run lint`、`test`、`build` | 无 |
| **FE1b** 间距与表单 | ~55 | 同 FE1a | P3-13、P3-18 | 同上 | FE1a |
| **FE2** 结构与错误分支 | ~45 | `frontend/src/app/**/*.ts`（DTO 迁移涉及的导入）、`core/errors/api-error-codes.ts`、`permission-grant-dialog.*`、`account-service.*`、`login.*`、`two-factor-setup.*`、`layout-service.ts` | P3-3、P3-4、P3-7（按 §3.6）、P3-19 | lint、test、build | FE1b |
| **FE3a** Mock 与服务单测 | ~45 | `frontend/_mock/**`、`role-service.spec.ts`、`tenant-service.spec.ts`、`popover-aria.spec.ts` | P3-5、P3-10 | `npm test`（Mock 相邻 spec） | FE2、TB3b |
| **FE3b** 表单与工具单测 | ~55 | 新增 spec：`zoned-time.spec.ts`、`operation-records.spec.ts`、7 个表单组件的 spec | P3-8、P3-9 | `npm test` | FE3a |
| **FE5** 浏览器验证 | ~60 | 不改文件；证据存 scratchpad | B-1～B-14（`p3-findings.md` §3），关闭 P3 矩阵中的 6 行 | agent-browser 有头会话；用户在 `identity-all-features` 生成项目上用 3 个账号 | 全部前端包与后端链 |
| **S1a** 后端规范 | ~45 | `template/docs/standards/{api,auth,coding-backend,coding-common,service-invocation,testing,project-structure}.md`、`template/docs/README.md` | P1-2（规范，§3.1）、P1-12（规范）、P2-5（规范）、P2-14、P2-19～P2-23、P2-31～P2-33（规范）、P2-35（结构）、P2-37、P4-T1、P4-T2（testing.md）、P7-3、P7-5 | `python3 scripts/measure-template-read-cost.py --check`；check-markdown-anchors；生成后检查条件块 | 裁定完成 |
| **S1b** 前端规范与 Skill | ~35 | `template/docs/standards/{frontend-ui,frontend-spartan,coding-frontend}.md`、`template/.agents/skills/leistd-project-workflow/**`、`docs/template/development-guide.md`（基于未提交的改动） | P3-1、P3-2、P3-6、P3-7（规范）、P3-12（规范）、P3-20、P7-1、P7-2、P2-12 | `pwsh scripts/validate-skills.ps1`；读取成本检查；锚点检查 | 裁定完成 |
| **G1a** 脚本可移植 | ~30 | `scripts/check-clock-access.py`，P4-34 列出的 6 个 ps1 首行，P4-35 列出的 Python 脚本 | P4-10、P4-34、P4-35 | 各脚本的 `--self-test`；用 `python -X warn_default_encoding` 运行 | 无 |
| **G2** 文档示例编译与闭包 | ~55 | 新增 `scripts/extract-doc-snippets.py`、`scripts/test-package-consumption.ps1`、`framework/build/check-docs-api-drift.ps1` | G-16、G-17 | 自检（注入缺 using 的代码块应失败）；`test-package-consumption.ps1` | F1、F4 |
| **G-TB** 模板随包闸门 | ~60 | 新增 `template/backend/.editorconfig`；`template/backend/Directory.Build.props`（加 `EnforceCodeStyleInBuild`，并完成 P2-36 中 Build.props 的注释）；新增约定测试 `UnitTests/Api/ControllerAuthorizationConventionTests.cs`、`IntegrationTests/RouteConventionTests.cs`、`IntegrationTests/EntityModelConventionTests.cs`；新增 `template/scripts/check-error-codes.py`，`check-i18n.py` 拆出判据 5 并完成 P4-36；testing.md §5 与 README 的验证列表各补一行 | G-01～G-06、P4-36 | 矩阵 pr（警告即错误）；`check-error-codes.py --self-test`；约定测试内置正例、反例与例外 | TB3c、TB4、S1a |
| **G-FE** 前端 lint 规则 | ~35 | `template/frontend/eslint.config.mjs`；新增 `scripts/test-template-eslint-rules.mjs` | G-07 | `node scripts/test-template-eslint-rules.mjs`（夹具在内存中，用 lintText）；`npm run lint` | FE2 |
| **G-X** 跨端仓库闸门 | ~50 | 新增 `scripts/check-spartan-customizations.mjs`、`scripts/check-template-mock-coverage.py` | G-10、G-11 | 两个脚本的 `--self-test` | FE3a、S1b |
| **G-FW** 框架分析器 | ~40 | `framework/.editorconfig`、测试的 `.editorconfig`，以及分析器报出的源码位置 | G-12 | Release 构建 0 警告；一次性变异（记入实施报告） | F2、F6b |
| **G1b** 闸门规则扩展与接线 | ~55 | `scripts/check-csproj-conventions.py`、`check-test-layout.py`、`check-retired-terms.ps1`、`check-test-names.py`、`check-i18n-keys.ps1`、`check-all.ps1`、`.github/workflows/ci.yml` | G-08、G-09、G-13、G-14、G-15、G-18；把 G2、G-X 的脚本接入 `check-all.ps1`；新增 windows-latest 作业 | `pwsh check-all.ps1`；各新规则的自检 | F6b、F3、G2、G-X、G-TB、TB4 |
| **Z** 收口 | ~30 | `docs/framework/upgrade-0.13.0.md`（§36、§37 补行，新增 §39）、release notes、计划 §5、本文件（删除） | 汇总各包报告的升级说明；矩阵状态改为"违规已修复"，并绑定最终候选 SHA | check-all；读取成本检查 | 全部 |

### 5. P6 闸门清单

| 闸门 | 规则或发现 | 工具与位置 | 分发 | 自检用例（合法、违规、例外） |
| --- | --- | --- | --- | --- |
| G-01 | 日志模板为常量、参数个数匹配（coding-backend §7） | `template/backend/.editorconfig`：CA2254、CA2017 设为 warning；`Directory.Build.props` 开 `EnforceCodeStyleInBuild`；矩阵把警告当错误 | 随模板 | 合法：矩阵全绿；违规：实施时做一次插值日志变异，确认构建失败；例外：`[SuppressMessage]` 必须带 Justification |
| G-02 | 文件范围 namespace、命名空间跟随目录、正文不内联全名、接口与实现参数名同步（P2-30、P2-33） | 同一份 .editorconfig：IDE0161、IDE0130、IDE0001/IDE0002、CA1725 | 随模板 | 合法：矩阵；违规：`ApiResource` 的旧命名空间（修复前应报错）；例外：`using 别名` |
| G-03 | 每个 Controller action 有显式授权；非 GET 端点要有策略或进白名单（P1-13/P2-1） | 单元测试 `ControllerAuthorizationConventionTests`：反射 Api 程序集，要求每个 action 生效 `[Authorize]` 或 `[AllowAnonymous]`；非 GET 的 action 必须带 Policy，或列在"匿名协议端点""本人自助端点（`/auth/me/**` 等）"白名单中（每项注明理由） | 随模板 | 测试内置夹具控制器：带策略的、只有类级 Authorize 的写端点（应报错）、白名单内的端点；回退 P1-13 应变红 |
| G-04 | 业务路由以 `/api/v1/` 开头（api §6） | 集成测试遍历 `EndpointDataSource`；`/connect/*`、`/hubs/*`、`/api/health/*`、SPA 回落进白名单 | 随模板 | 规则函数用合成端点做单测：合规、违规、白名单各一 |
| G-05 | 本项目实体实现 `IMultiTenant`；枚举属性按字符串持久化（auth.md、coding-backend §3.8） | 集成测试遍历 `MyProjectDbContext.Model`，只检查本项目 Domain 程序集中的类型，例外放进带理由的白名单 | 随模板 | 规则函数用合成模型做单测 |
| G-06 | 错误码：不写字面量、无零引用、格式为 `模块:语义`、前缀由一个模块独占（P2-2、P2-15，以及 `!Email` 下的死映射） | 新增 `template/scripts/check-error-codes.py`：从 `check-i18n.py` 判据 5 拆出，不随 `IncludeLocalization` 裁剪，在生成产物上运行 | 随模板（testing.md §5、README） | `--self-test`：字面量码、零引用常量、跨文件前缀应失败；被组件常量引用的码属于例外 |
| G-07 | OnPush、`inject()`、不按状态码分支、DTO 只放 `.dto.ts`、不自调 `Title`、模板类名档位（`space-*`、`font-bold`、具体色板、档外 `text-[Npx]`） | `template/frontend/eslint.config.mjs`：`prefer-on-push-component-change-detection`、`prefer-inject`、`no-restricted-syntax`、`no-restricted-imports`；豁免 `app.ts`、`empty-layout.ts`、`core/interceptors/**`、`startup-service.ts`、`hlm-spinner`、`ng-icon`。绑定的 `[class]` 与 TS 字符串不在覆盖内，作为已知边界写明 | 随模板；规则自检放仓库 `scripts/test-template-eslint-rules.mjs` | 用 ESLint `lintText` 对每条规则各跑一个合法片段、违规片段和豁免路径 |
| G-08 | compose 与 `.env.example` 的变量双向一致；`Section__Key` 能对上 Options；Serilog Override 前缀合法；生成项目根目录等于 project-structure §1（P2-9、P2-34、P2-35） | `scripts/test-template-generation.py`，在各有效形态的生成产物上运行 | 仓库 | 夹具：多出的键、缺少的变量、无效的 Override、多出的根文件（如 `VERSION`） |
| G-09 | 维护事实不进模板；源码和测试不记修复轮次（P2-36、P4-27） | `scripts/check-retired-terms.ps1` 新规则：`template/**`（不含 `.template.config`）禁止 `.github/workflows`、`仓库根`；`framework/`、`template/backend` 禁止 `\b[NP]\d{1,2}\s*(的|）|\))`、`上一轮`、`本轮修` | 仓库 | 正例、反例，以及 `leistd-net-framework` 例外 |
| G-10 | Spartan 登记表与 `libs/ui` 的实际差异一致（P3-1、P3-2） | 新增 `scripts/check-spartan-customizations.mjs`：用模板 `node_modules` 中锁定版本的 `@spartan-ng/cli`，经 `createStyleMap` 和 `transformStyle` 还原上游，Prettier 规整后 diff，再与 `frontend-spartan.md` 登记表的组件集合比较是否相等 | 仓库 | 改一个未登记组件应失败；删掉一行登记应失败；登记且存在则通过 |
| G-11 | 前端在用的后端端点都有 Mock（P3-5） | 新增 `scripts/check-template-mock-coverage.py`：后端路由（含 `ComponentEndpoints.cs`）、前端调用、`_mock` 键三方求差。跨端校验只放仓库闸门，符合"前后端测试互相独立"的约定 | 仓库 | 夹具：缺 Mock、多余 Mock、浏览器跳转型端点（例外） |
| G-12 | BCL 守卫、取消令牌传递、ParamName、不抛保留异常、死替身（P4-21、P4-30） | `framework/.editorconfig`：CA1510–CA1513、CA2016、CA2208、CA2201 设为 error；测试的 .editorconfig 开 CA1812 | 仓库（框架） | Release 构建；实施时做一次变异 |
| G-13 | 测试的一级划分守依赖方向；子目录名与替身位置（P4-9、P4-29） | `check-csproj-conventions.py`：`tests/components/**` 不得引用 `ddd-struct/`。`check-test-layout.py`：子目录名属于 {包后缀, Contracts, TestDoubles, TestResources, EndToEnd}，`Fake*`、`*Doubles*` 必须在 `TestDoubles/`；根目录文件数超过 10 只报告 | 仓库 | 两个脚本既有的 `--self-test` 各补正例、反例和例外 |
| G-14 | 跨平台（P4-10、P4-34、P4-35） | `check-all.ps1` 内联检查：入口 ps1 首行是 shebang（点源加载的库在白名单内）；Python 闸门统一以 `-X warn_default_encoding -W error::EncodingWarning` 运行；`ci.yml` 新增 windows-latest 作业跑 `check-all.ps1` | 仓库 | shebang 夹具；反斜杠路径夹具；windows 作业本身就是证伪 |
| G-15 | 框架错误码自带译文（P4-3） | `check-i18n-keys.ps1` 新规则：framework 中值为 `模块:语义` 的错误码常量，必须出现在同一个包的 `Resources/en.json` 与 `zh-CN.json` 里 | 仓库 | 夹具：缺译文应失败；宿主覆盖不算 |
| G-16 | 组件文档示例能编译（P4-5、P4-6、P4-7） | `scripts/extract-doc-snippets.py` 抽取 `## 注册`、`## 使用` 段的 csharp 代码块，补上桩类型，放进包消费项目只编译，由 `test-package-consumption.ps1` 执行；用 `<!-- no-compile: 理由 -->` 显式豁免 | 仓库 | 注入一个缺 using 的代码块应失败；带豁免标记的代码块跳过 |
| G-17 | 示例只用本组件闭包里的类型（P4-14） | `check-docs-api-drift.ps1` 复用标识符索引，代码块中的 Leistd 标识符必须属于本篇家族 csproj 的传递闭包；`## 注册` 段的组合行按修订后的 §5.1 豁免 | 仓库 | 跨闭包标识符应失败；组合行豁免 |
| G-18 | 前端用例名小写开头 | 扩展 `scripts/check-test-names.py` | 仓库 | 大写开头应失败；专有名词在白名单内 |

不做闸门、保留人工审查的：职责归属（P1 全部）、兄弟元素外边距、逻辑文件是否必须有 spec、重复调用契约、fixture 共享、删除幂等策略、审计覆盖面、生命周期选择、组合期读值、孤立的 `;`。这些要么没有稳定判据，要么只能做成关键词匹配，误报高、收益低。

### 6. P7 复测

Skill 与文档修复（S1a、S1b、TB4）：

| 失败或偏差 | 修复 | 所在包 |
| --- | --- | --- |
| 只审查场景没指出分层违规 | P7-1：`quality.md` 加入规范符合性；P7-3：coding-backend §3.4 写明判据 | S1b、S1a |
| 精简场景没处理预置的重复和过时条目 | P7-2：`documentation.md` 的精简范围，加上 `SKILL.md` 执行闭环第 4 条 | S1b |
| 部署场景的偏差 | P7-4（Dockerfile 注释）、P7-6（版本确认）、P7-7（HTTPS，先核实）、P7-8（预演命令） | TB4 |
| 生成文档标题是字面量 | P7-5 | S1a |

复测要求：

- **SKILL.md 有改动**，所以五个场景的路由输入都变了。按计划 §4"相关输入变化后重跑受影响项"，在最终候选上**重跑全部 5 个场景**。只审查、重复纠正与精简两个场景**各跑 2 次**，以确认稳定，其余各跑 1 次。
- 环境和判据与首轮相同：codex-cli 版本、模型和 reasoning effort 记入报告；每个请求一个独立会话；以轨迹、文件变化和独立重跑的测试判定；部署场景先探测 Docker 上下文，结束后按全局约定清理。
- 夹具：本地包源要用**最终 framework 重新打包**（首轮的 `.tmp/local-feed` 早于最后一次 framework 提交）；只审查和精简的植入内容与首轮相同，以便对比。
- 部署场景的通过条件补一项：报告的版本来自 deploy README 新写明的版本确认方式。

同期的其他复验：认证输入有改动（P1-1、P1-13、P2-6、P2-8），需要在最终候选上重跑 `test-template-oidc-e2e.ps1 -IncludeExpiryWait`（P8）；持久化有改动（P1-1、P1-3），需要跑 `test-template-postgresql-e2e.ps1`；FE5 完成浏览器验证。

### 附录 A　覆盖矩阵原文（证据）

以下内容原样复制自 scratchpad 中各审查的覆盖矩阵，只调整了标题层级，方便审核期间在仓库内查看证据。其中的发现编号与第 2 节对应。各矩阵状态列中的"违规（待修）"，在实施完成后由工作包 Z 改为"违规已修复"。

#### A.1 P1 模板后端职责（来源 `p1-findings.md` §1）

| 规则（文件 §） | 检查方法 | 范围 | 状态 | 证据 |
| --- | --- | --- | --- | --- |
| coding-common §3.1 开发前查找参考链路 | 不适用于静态审查（过程性要求） | — | 不适用 | 属开发流程约束，无可判定的代码产物；P7 路由抽查覆盖其在 Skill 中的落地 |
| coding-common §3.2 守卫按聚合范围分层 | 人工：逐方法记录约束对象→所需状态→归属层 | 全部实体、领域服务、应用服务、Api 协作类型 | 违规待修复 | P1-3（角色名唯一在应用服务）、P1-4（邮箱唯一三条路径）、P1-5（两步验证/邮箱已验证前置条件在应用服务）、P1-6（默认角色读外聚合在领域服务）、P1-7（领域服务做查询） |
| coding-common §3.3 验证在入口 DTO 层完成 | 人工：比对 DTO 注解与内层重复校验 | 全部应用服务入参 | 违规待修复 | P1-10（ClientId 空白、角色名长度、2FA 二选一、本人头像仅图片在服务内校验）；实体守卫合规：`User` 资源服务构造 `Domain/Users/Entities/User.cs:150`、`ExternalLoginConnection` ctor `:88-89` 属多入口共享守卫 |
| coding-common §3.4 扩展现有抽象、杜绝并行实现 | 人工：同一判断的多处实现 | 全部 | 违规待修复 | P1-4（邮箱唯一 3 处）、P1-6（默认角色分配 2 处）、P1-8（死代码 `CreateUserWithRolesAsync`/`AssignRolesToUserAsync`）、P1-9（口令校验绕过领域服务）、P1-14（超管标记绕过宿主守卫）、P1-16（会话过期判据 4 处、http 地址判定 2 处） |
| coding-common §3.5 抽象在内层、依赖只向内 | 证据复用 + 人工抽查 | 各层 using 与构造参数 | 证据复用 | 阶段二依赖方向审查（`fac8a15e`）；本轮抽查：Application 不引用 Infrastructure/EF Core；Api 直接调领域服务一处见 P1-12（属 §2 职责问题，不是依赖反向） |
| coding-common §3.6 删除幂等且保留真实失败 | 人工 | 删除类用例 | 合规 | `RoleAppService.DeleteAsync` 不存在时仍清授权并成功（`Application/Roles/AppServices/RoleAppService.cs:195-203`）；`ExternalAuthAppService.UnlinkCurrentUserAsync`、`UserSessionAppService.RevokeCurrentUserSessionAsync` 不存在静默成功；无外部资源删除。详细归 P2 |
| coding-common §3.7 统一开关决定是否触达外部 | 人工 | 发信、外部调用 | 合规 | 发信经 `IEmailSender`，无调用方 opt-in 参数；详细归 P2 |
| coding-common §3.8 枚举只在边界转换 | 人工抽查 | 领域枚举 | 合规 | `UserAccessStatus`/`CredentialValidationStatus`/`ChangePasswordStatus` 只在领域与应用内使用；`SecurityAlertKind` 仅在 Api 实现里 `ToString()` 进元数据。大小写契约归 P2 |
| coding-common §3.9 签名同步；短名导入 | 编译 + grep | 全部 | 证据复用（签名）／合规（短名） | 接口与实现由编译保证；正文无内联全限定名（抽查）。注：多处残留未用 `using Leistd.MultiTenancy.*`（如 `Domain/Users/Entities/Role.cs:2-6`），属清理项，不违反本条 |
| coding-backend §2 分层职责表 | 人工：逐类核对"做/不做" | 全部 | 违规待修复 | P1-7（Domain 做查询）、P1-11（Api 做多来源拼装）、P1-12（Api 直接调领域服务与仓储编排）、P1-13（授权以业务异常在应用层模拟） |
| coding-backend §2 目录归位 | 证据复用 | 目录 | 证据复用 | 阶段二结构审查；本轮未发现新偏差（`Api/Notifications` 见 P1-12 备注） |
| coding-backend §2 领域服务单向依赖并注释 | 人工 | 4 个领域服务 | 合规 | 仅 `ExternalAuthDomainService → UserDomainService`，类注释说明复用变更行为（`Domain/Auth/DomainServices/ExternalAuthDomainService.cs:22-26`），未经它读取 |
| coding-backend §3.1 语言特性与异步命名 | grep | 全部服务类 | 合规 | 服务类均主构造；无 `.Result`/`.Wait()`；返回 Task 的方法均以 `Async` 结尾（grep 零命中） |
| coding-backend §3.2 时间 | grep + 人工 | 全部 | 违规待修复（低） | 无 `DateTime.Now/UtcNow`；Api 认证回调用 `TimeProvider` 合规；实体时间均由参数传入。仅 P1-17：`RoleListChangedEvent` 走 `LocalEvent()` 无参构造，隐式取系统时间 |
| coding-backend §3.3 实体 | grep + 人工 | 5 个实体 | 合规 | 全部属性 `private set`（Domain 中 `{ get; set; }` 只出现在 Options）；均有 private 无参构造；Id 用 `Guid.CreateVersion7()`，唯一例外是资源服务形态 `User` 以签发方 `sub` 为主键（`User.cs:139-152`，有意设计）；状态只经公共方法；`Guid.NewGuid` 只用于安全戳与占位邮箱，不作 Id |
| coding-backend §3.4 领域服务 | 人工 | 4 个领域服务 | 违规待修复 | 命名与无接口合规；P1-7（查询）、P1-6（读外聚合）、P1-8（死代码）；副作用经本地事件见 P1-2 |
| coding-backend §3.5 应用服务 | grep + 人工 | 11 个应用服务 | 合规 | 11 个接口均 `: IAppService`，实现均 `: BaseAppService`；查询用 `GetQueryableAsync` + `IQueryableAsyncExecuter`；排序白名单末尾追加 Id（OpenApplication 为 ClientId，有注释） |
| coding-backend §3.6 事务与工作单元 | 人工：数提交边界 | 全部写方法 | 违规待修复 | P1-1（多次提交未标 `[UnitOfWork]`，且注释声称同一工作单元）、P1-15（写后回查） |
| coding-backend §3.7 Controller 与组件端点 | 人工 | 11 个 Controller | 违规待修复 | 继承与例外注释合规（`ConnectController` 例外已注释）；返回类型合规；P1-11、P1-13 |
| coding-backend §3.8 枚举持久化 | 证据复用 | 实体配置 | 不适用 | 当前实体无枚举列；归 P2 数据访问 |
| coding-backend §3.9 权限 | 证据复用 | 权限定义 | 证据复用 | `tests/…IntegrationTests/PermissionCatalogContractTests.cs`、`UnitTests/Application/PermissionContractTests.cs`；本轮输入未变 |
| coding-backend §3.10 设置与 Options | grep + 人工 | 服务读配置 | 合规 | 租户级经 `ISettingProvider`（`LoginSecurityPolicyProvider`、`UserRegistrationPolicyProvider`）；服务内无 `IConfiguration["键"]`（只在组合期与 DbMigrator）；值域校验在 `Settings/Validators`。详细归 P2 |
| api.md §4 异常用法（P1 相关部分） | 人工 | 抛异常位置 | 违规待修复 | P1-13（授权拒绝用业务异常）；其余：Domain/Application 业务规则用 `BusinessException(code, msg)` 合规；`CreateSuperAdminAsync` 租户上下文抛 `InvalidOperationException` 属编程契约，合规 |

#### A.2 P2 模板后端其余规范（来源 `p2-findings.md` §1）

##### 1.1 api.md（含 coding-backend §3.5/§3.7 交叉项）

| 规则(文件 §) | 检查方法 | 范围 | 状态 | 证据 |
| --- | --- | --- | --- | --- |
| api §1 破坏性操作具备认证与授权 | 人工：逐个写端点列 `[Authorize]`/`[AllowAnonymous]`/策略 | `Api/Controllers/*.cs` 全部 POST/PUT/PATCH/DELETE（43 个） | 违规待修复 | 匿名写端点只有登录、两步、登出、注册、发码、外部 complete（协议所需）；`SettingController.cs:16,21` 只有类级 `[Authorize]`，权限在应用服务里用业务异常判（见发现） |
| api §1 破坏性操作具备审计 | 人工：写端点 `[OperationRecordAction]` 与应用服务 `RecordSucceededAsync` 对照 | 同上 + `Application/**/AppServices` | 违规待修复 | 开放应用 Create/Update/Delete/ResetSecret（`OpenApplicationController.cs:51,63,76,86`）只有 Information 日志（`OpenApplicationAppService.cs:184,233,241,256`），`OperationRecordActions.cs` 无对应动作码；其余用户、角色、会话、外部账号、模拟登录有记录 |
| api §1 破坏性操作具备幂等策略 | 人工：读各 DeleteAsync 对"不存在"的处理 | Role/User/OpenApp/Session/ExternalLink 删除 | 违规待修复 | 角色 `RoleAppService.cs:195-203`、会话 `UserSessionAppService.cs:68-70`、外部账号 `ExternalAuthAppService.cs:114-116` 不存在即成功；用户 `UserAppService.cs:460`、开放应用 `OpenApplicationAppService.cs:238,260-266` 不存在即 404。策略不一致，规范未定哪种 |
| api §2 成功裸对象、不包 `Ok(...)`/信封 | grep `Ok(`、`success` | `src/**` | 合规 | 唯一 `Ok(` 在 `ConnectController.cs:288`（OIDC userinfo 协议端点，`/connect/*` 由协议固定）；无信封 |
| api §2.2 / backend §3.7 无响应体用 `Task`、`IActionResult` 仅限多协议结果或文件 | grep `IActionResult` 并逐个看用途 | Controllers | 合规 | `UserController.cs:160` 文件（应用服务返回字节与元数据 `UserAvatarOutputDto`）；`ConnectController` 全部为协议端点；`ResourceAuthController.cs:27,56`、`ExternalAuthController.cs:44,48` 为 Challenge/SignOut 协议结果（单一协议结果，规范字面只写"多种"，见发现的改规范项）；`ResourceAuthController`/`ExternalAuthController` 返回类型阶段二 T2-B3/B6 已改 |
| backend §3.7 有响应体返回 `Task<TOutputDto>` | 人工 | Controllers | 违规待修复 | `ServiceInfoController.cs:22,40` 同步方法直接返回 DTO（无 I/O）；见发现 |
| api §2.2 组件端点写操作 204 | 读 `Hosting/ComponentEndpoints.cs`；测试断言 | 组件端点 | 合规 | 由组件 `Map*` 产出；`IntegrationTests/EmailSettingsTests.cs:138` 等断言 `NoContent` |
| api §2.3 分页返回 `PagedResult<T>` | grep 控制器分页方法签名 | 3 个分页端点 | 合规 | `UserController.cs:28`、`RoleController.cs:25`、`OpenApplicationController.cs:24` |
| api §2.4/§2.5 ProblemDetails、traceId、校验 400 由框架统一产出 | 读组合根；测试 | `Program.cs` | 合规 | `Program.cs:99` `AddGlobalExceptionHandler(ApiExceptionMappings.Configure)`、`:200` `UseGlobalExceptionHandler()`；`ResourceBrowserSessionTests.cs:192,240`、`BrowserOriginTests.cs:30` 断言 `application/problem+json`；`TenancyTests.cs` 断言 traceId |
| api §2.4 `IncludeExceptionDetails` 生产不开 | grep 配置 | appsettings*.json | 合规 | 三份 appsettings 均未设置该键（默认 false） |
| api §3 状态码选用（不用 201/422 等非约定码） | 读全部 `MapCode` | `Hosting/ExceptionMappings/*.cs` | 合规 | 只用 401/403/404/409/429/503；无 201/422 |
| api §3 不重复登记 400 / 只登记真实码 | 现有测试 | 组合后的映射 | 证据复用 | `IntegrationTests/ApiExceptionMappingsTests.cs:87-123`（每条映射是已知常量且 ≠400）；输入自 fac8a15e 未变 |
| api §4 非默认状态按模块登记、组合根汇总；组件默认状态不由宿主重复登记 | 人工 | `Hosting/ApiExceptionMappings.cs`、`ExceptionMappings/*` | 合规 | 每模块一个文件，`ApiExceptionMappings.Configure` 汇总；6 个文件均不引用组件错误码 |
| api §4 只用 `BusinessException(code, safeMessage)`，无 `WithCode`/`WithDetails` | grep | `src/**` | 合规 | 无 `WithCode`/`WithDetails` 调用 |
| api §4 认证/授权拒绝不用业务异常模拟 | 人工：读全部 403/401 映射码的抛出点 | 401/403 映射码 15 个 | 违规待修复 | `EmailSettingsAppService.cs:33-35`（权限判定 → `AppSetting:ManagePermissionRequired` 403）；`TenantImpersonationAppService.cs:65-66`（不可达的"未认证"→ 401）；`ConnectController.cs:262`（不可达的 grant 类型）。`TwoFactorSetupEnforcementMiddleware.cs:35`、`UserAppService.cs:565-568`（依输入的附加权限）属合理例外但规范未登记 |
| api §4 错误码格式 `模块:语义`、常量名=语义后缀、前缀由一个模块独占 | 脚本：解析全部 `*ErrorCodes.cs`，比对类名前缀与成员名，统计前缀归属文件数 | 9 个 `*ErrorCodes.cs`，79 个码 | 合规 | 脚本输出 0 处不一致、0 个前缀跨文件 |
| api §4 不写字面量码 | grep `BusinessException("`、`new("X:Y"` | `src/**` | 违规待修复 | `Api/Controllers/ExternalAuthController.cs:160` `new("ExternalAuth:InvalidState", ...)` |
| api §4 码按模块与最低实际使用层放置；`Domain/Shared` 只放跨模块契约 | 脚本：统计每个码被哪些层引用 | 79 个码 | 违规待修复 | `Domain/Shared/Security/Errors/SecurityErrorCodes.cs` 两个码只被 `Application/Auth` 使用（`AuthAppService.cs:458,466`、`TwoFactorAppService.cs:153`）；`UserErrorCodes`（Domain）中 9 个码只在 Application 抛出——按"按类"读法合规、按"按码"读法违规，规范需澄清 |
| api §4 码不随本地化裁剪 | grep `#if (IncludeLocalization)` 于 `*ErrorCodes.cs`/抛出点 | `src/**` | 合规 | 无此类条件；`AppSettingErrorCodes.cs:6,11` 的 `#if (Email)` 是按功能裁剪 |
| api §4 一码一义、码均有读取方 | 脚本：引用计数 + 读资源文案 | 79 个码 | 违规待修复 | `ExternalAuthErrorCodes.ProviderNotSupported` 零引用（仅资源 `Resources/en.json:67`）；`!Email` 形态下 `AppSetting:ManagePermissionRequired` 只剩映射（`AppSettingExceptionMappings.cs:18`），抛出点随 `EmailSettingsAppService` 被裁掉；`ExternalAuthDomainService.cs:318-321` 用 `User:UsernameTaken` 表达"无法分配用户名"，本地化后显示"用户名 'x' 已存在"；`User:EmailAlreadyUsed`（`UserAppService.cs:255`）与 `User:EmailTaken`（`UserDomainService.cs:272,307`）同义两码 |
| api §4 匿名场景不回显可枚举的用户名/邮箱 | grep `WithData("Username"\|"Email"` 并追调用路径 | `src/**` | 合规 | 登录与发码失败文案不带账号（`EmailVerificationAppService.cs:82` 不回显）；匿名注册的 `UsernameTaken`（`UserDomainService.cs:265`）回显的是请求者自己的输入，注册本身必然暴露占用，不构成额外枚举 |
| api §5.1 offset/limit、limit 1–1000、offset ≥0 越界 400 | 读框架 `PageRequest` | `framework/components/data/Leistd.Data/Paging/PageRequest.cs` | 合规 | `[Range(0, int.MaxValue)]`、`[Range(1, 1000)]`，默认 10；三个分页 DTO 均继承 |
| api §5.1 sorting 白名单、越界 400、方向只认 asc/desc | 读 `SortingRequest` 与三处 ApplySorting | `Application/Shared/Paging/SortingRequest.cs`、User/Role/OpenApp | 合规 | `SortingRequest.cs:31-37` 方向；`UserAppService.cs:160-170`、`RoleAppService.cs:94-101`、`OpenApplicationAppService.cs:368-375` 白名单 + `UnknownField`（未映射 → 默认 400） |
| backend §3.5 排序末尾追加 `Id` | 读 ApplySorting | 同上 | 违规待修复 | `UserAppService.cs:173`、`RoleAppService.cs:104` 追加 `Id`；`OpenApplicationAppService.cs:378` 追加唯一的 `ClientId`（OpenIddict 应用在内存排序） |
| api §5.2 `Get{Entity}PagedInputDto : PageRequest`、`PagedResult<{Entity}OutputDto>` | 文件名与基类 | 3 个分页 DTO | 证据复用 | 阶段二 T2-B2 DTO 拆分与改名；本轮复核 `Users/Dtos/GetUserPagedInputDto.cs:10` 等均继承 `PageRequest` |
| api §6 业务路由以 `/api/v1/` 开头 | grep `[Route]`、`MapGroup` | 10 个控制器 + 组件端点 | 合规 | 9 个业务控制器 `api/v1/...`；组件端点 `ComponentEndpoints.cs:39` `MapGroup("/api/v1")`；`ConnectController` 为 `~/connect/*`（OIDC 协议地址，非业务 API） |
| api §6 CRUD 方法名与动词映射 | 人工：逐端点比对 | User/Role/OpenApp | 合规 | GET 列表 `GetPagedListAsync`、GET `{id}` `GetAsync`、POST `CreateAsync`、PUT `{id}` `UpdateAsync`、DELETE `{id}` `DeleteAsync`；业务动作用子路径（`{id}/enable` 等），规范为"建议" |
| api §7 API 文档按需 | 人工 | — | 不适用 | 模板无对外长期文档需求 |
| api §8 兼容性 | — | — | 不适用 | 针对后续变更流程，非静态可判定；阶段二破坏性变更已记 `upgrade-0.13.0.md` |

##### 1.2 auth.md 与 docs/template/browser-authentication.md

| 规则(文件 §) | 检查方法 | 范围 | 状态 | 证据 |
| --- | --- | --- | --- | --- |
| 侧别是 `AddPermission` 必填参数（auth.md 权限侧别；dev-guide §8） | 人工核对框架签名 | 框架 `PermissionDefinition.AddPermission` | 合规 | `framework/components/authorization/Leistd.Authorization.Core/Definitions/PermissionDefinition.cs:65` 的 `side` 参数没有默认值；编译器保证每条权限都选了侧别 |
| `App.Users` → Both（User 实现 `IMultiTenant`） | 逐条对照实体 | `PermissionDefinitionProvider.cs:44-46` | 合规 | `Domain/Users/Entities/User.cs:13` |
| `App.Roles` → Both（Role、UserRole 实现 `IMultiTenant`） | 同上 | `:61-63` | 合规 | `Role.cs:13`、`UserRole.cs:20` |
| `App.Tenants` → Host（租户注册表属宿主全局） | 同上 | `:73-75` | 合规 | 租户由组件 `MapTenantManagement` 管理（`Api/Hosting/ComponentEndpoints.cs:88-93`） |
| `App.OpenApplications` → Host（OpenIddict 表没有 TenantId） | 同上 | `:92-94` | 合规 | `Infra/Persistence/OpenIddictDbContext.cs`（固定连宿主库）。注释已过时，见 P2-13 |
| `App.OperationRecords` → Both | 同上 | `:106-108` | 合规 | 框架 `OperationRecord : IMultiTenant`（`Leistd.OperationRecords.EntityFrameworkCore/Entities/OperationRecord.cs:16`） |
| `App.Settings` → Both；宿主专属内容在服务内补校验（`Setting:HostOnly` 例外） | 同上，再 grep 服务内的租户判断 | `:117-119`、`EmailSettingsAppService.cs:38-40` | 合规 | `SettingRecord : IMultiTenant`；进程级设置由框架 `SettingManagementService.cs:69` 抛 `Setting:HostOnly`。模板发信测试补校验 `AppSetting:TestEmailHostOnly`，与"权限必须保持 Both、部分内容宿主专属"的例外一致 |
| 侧别为 Host 时不在应用服务里再拦一次 | grep `currentTenant.Id`、`IsHost`、`HostOnly` | `App/Tenants`、`App/OpenApplications`、对应 Controller | 合规 | `OpenApplicationAppService` 没有租户判断；`TenantImpersonationAppService` 里的 `currentTenant` 只用于切换上下文（`:84,175,188,250-260`），不做侧别拦截 |
| 收紧侧别要附撤销 SQL | 人工核对 | 本轮没有收紧侧别 | 不适用 | 自 `fac8a15e` 起 `PermissionDefinitionProvider` 未改动 |
| 被独立查询的实体必须实现 `IMultiTenant`（auth.md 实体租户维度） | 列出全部 DbSet 与 Configure* 实体 | `Infra/Persistence/MyProjectDbContext.cs:30-51`、Domain 实体 | 合规 | User、Role、UserRole、UserSession（`Auth/Entities/UserSession.cs:20`）、ExternalLoginConnection（`:20`）都实现了；组件实体 PermissionGrantRecord、SettingRecord、OperationRecord、NotificationRecord 也都实现了 |
| 宿主全局表不进租户过滤（例外登记） | 人工核对 | `OperationRecordArchive`、`RecurringJobState`、OpenIddict 四表 | 合规 | 归档表在 `MyProjectDbContext.cs:45-47` 注明了逐库归档、不参与日常查询；`RecurringJobState` 是各库自己的调度水位；OpenIddict 表走宿主控制库。这些都没有业务直查入口（实体本身归框架，属于 P4 范围） |
| 需要用户身份的接口校验认证状态；管理接口按权限隔离 | 逐个 Controller 动作核对 `[Authorize]`、`[AllowAnonymous]`、`Policy` | `Api/Controllers/*.cs` 全部动作 | 违规待修复 | 其余动作都有显式标注；`SettingController.cs:16-25` 只有类级 `[Authorize]`，权限在应用服务里用业务异常模拟，见 P2-1 |
| 认证失败统一返回 401，不重定向 | 读 Cookie 事件 | `LocalSessionAuthenticationExtensions.cs:96-98`、`RemoteTokenAuthenticationExtensions.cs:96-97` | 合规 | `OnRedirectToLogin` 写 401、`OnRedirectToAccessDenied` 写 403 |
| 会话按请求校验，确认结果缓存 1 分钟（LocalIdentity） | 读代码 | `UserSessionValidator.cs:105`、`UserSession.cs:35` | 合规 | `TouchInterval = TimeSpan.FromMinutes(1)` |
| 访问令牌只签名、不加密 | grep | `OpenIddictServerExtensions.cs:108` | 合规 | 调用了 `DisableAccessTokenEncryption()` |
| 授权与退出启用请求缓存并保留 passthrough | grep | `OpenIddictServerExtensions.cs:85-86,125-128` | 合规 | `EnableAuthorizationRequestCaching().EnableEndSessionRequestCaching()`；四个端点的 Passthrough 都已启用 |
| 退出确认使用官方 antiforgery，不用 `confirm=true` 一类查询参数 | grep | `ConnectController.cs:47,126,157`、`AuthController.cs:98` | 合规 | 注入了 `IAntiforgery`，确认页只带 `request_uri` 与凭据 |
| `sessionBound` 创建和更新都必填，缺失或 null 返回 400 | 读 DTO 与测试 | `Create/UpdateOpenApplicationInputDto.cs:64-66 / :56-58` | 合规 | `[Required]` + `required bool?`；`OpenApplicationSessionBindingTests.cs:152,160` 覆盖了显式 null |
| `leistd:session_bound` 只经 `OpenApplicationSettings` 读写（browser-auth） | grep 字面量 | src 与 tests | 合规 | 字面量只出现在 `OpenApplicationSettings.cs:16`；读写都经 `ReadSessionBound`、`Format`（`OpenApplicationAppService.cs:179,230`、`ConnectController.cs:272-274`、`OpenApplicationMappings.cs:43`） |
| 会话 Cookie：部署环境名为 `__Host-Http-CompanyName.ProjectName.Auth`，Secure、HttpOnly、`Path=/`、不带 Domain；Development 不带前缀、Secure 跟随请求 | 读代码与测试 | `LocalSessionAuthenticationExtensions.cs:68-72`、`RemoteTokenAuthenticationExtensions.cs:90-94` | 证据复用 | 阶段二 T2-B8（`fac8a15e`），输入未变。两处注册一致，没有设置 Domain；测试 `UserSessionTests.cs:45-52` 与 `ProjectWebApplicationFactory.AssertSessionCookieContract` 覆盖 |
| 测试与 e2e 的期望 Cookie 名各自集中维护，不从实现读取（browser-auth） | grep 字面量 | tests、`scripts/test-template-oidc-e2e.ps1` | 合规 | `ProjectWebApplicationFactory.cs:65` 的常量 `SessionCookieName`（14 处引用）；e2e 脚本自带字面量 |
| `SessionCookie:ExpireDays` 至少 1，启动期校验；会话空闲时限与 Cookie 滑动过期同源 | 读代码与测试 | `AuthenticationExtensions.cs:42-53`、`LocalSessionAuthenticationExtensions.cs:34-35` | 证据复用 | 阶段二 T2-B8，输入未变；测试 `DeploymentSafeguardsTests.cs` |
| `SessionCookie:SameSite` 只作用于应用会话 Cookie，默认 Lax；协议 correlation/nonce Cookie 保持官方默认 | 读代码 | `AuthenticationExtensions.cs:56`、`ExternalAuthenticationExtensions.cs:37` | 合规 | 只改了会话 Cookie 与外部票据 Cookie（`SessionCookieOptions` 注释已写明），没有触碰 `CorrelationCookie`、`NonceCookie`；测试 `ExternalAuthenticationTests.cs:287` |
| `SaveTokens` 与 `ITicketStore` 一起配置（browser-auth） | 读代码 | `RemoteTokenAuthenticationExtensions.cs:107`、`AuthenticationExtensions.cs:41,50` | 合规 | ResourceBrowserSession 形态同时满足 `SaveTokens=true` 与 `SessionStore=DistributedTicketStore` |
| 显式登录、MFA 与冒用切换更换票据引用版本；滑动续期保留版本 | 读代码 | `DistributedTicketStore.cs:26,37-49,66-73`；全部经 `SessionSignInService.SignInAsync` | 证据复用 | 阶段二 T2-B6（`SessionCookieIssuer`），输入未变 |
| Origin 与 Sec-Fetch-Site 判定：写请求和 `/hubs/**`（含握手）都检查；Hub 不因带 Authorization 头而跳过；cross-site、same-site 拒绝；两个头都没有时放行；拒绝时 Warning 日志给出 Origin 与本源 | 读代码与测试 | `Middlewares/BrowserOriginMiddleware.cs:22-41`、`BrowserOriginTests.cs:24,42,55,67,87,120` | 合规 | API 写请求带 Authorization 头时跳过检查（`:24`）。规范只说 Hub 不跳过，见 P2-14（只改文档） |
| SPA 形态的 Hub 只接受请求头里的 Bearer | 读 `Program.cs` 条件 | `Program.cs:218-221` | 合规 | `UseHubAccessToken`（查询串转请求头）只在 `!SpaFrontend` 下注册；本规则写在 auth.md 的 `#if (SpaFrontend)` 块里 |
| Resource 的 Smart scheme 按 Authorization 头选 Bearer 或 Cookie，DefaultPolicy 与 CurrentUser 都用 Smart，失败不回退 | 读代码 | `RemoteTokenAuthenticationExtensions.cs:81-87`、`Api/DependencyInjection.cs:54-63` | 合规 | 选择器是 `Headers.ContainsKey("Authorization")` |
| OIDC 使用 code flow + PKCE，并通过官方 FormPost 发出 | 读代码与测试 | `RemoteTokenAuthenticationExtensions.cs:106,111`；`ResourceBrowserSessionTests.cs:433` | 合规 | `ResponseType="code"`、`AuthenticationMethod=FormPost`，测试断言 S256 |
| GitHub `UsePkce=true`，`UserEmailsEndpoint` 为空，主邮箱与 verified 由 `OnCreatingTicket` 自取，失败时降级 | 读代码与测试 | `ExternalAuthenticationExtensions.cs:57-61,89-115` | 合规 | 测试 `ExternalAuthenticationTests.cs:48`（CodeVerifier 非空）；`ExternalOAuthTestHost.cs:24,42`（`/user/emails`） |
| 外部 scheme 名为 `ExternalProviderPrefix + 小写 provider`，`SignInScheme` 指向 ExternalCookie，`CallbackPath` 在 `/api/**` 下 | 读代码 | `ExternalAuthenticationExtensions.cs:26-33,49-56`、`AuthenticationSchemeNames.cs:28` | 合规 | `google`、`github`，回调路径为 `/api/v1/external-auth/{p}/signin` |
| 外部票据 5 分钟、一次消费，绑定执行自然人策略 | 读代码 | `ExternalAuthenticationExtensions.cs:16-17`；`ExternalAuthController.cs:46,86` | 证据复用 | 阶段二 T2-B6，验证用例为 `ExternalAuthenticationTests` |
| 响应、日志、错误消息里不出现密钥、Token、连接串；邮箱脱敏 | grep 日志模板里的 email/token/password/secret | Application、Domain、Api 全部 `Log*` 调用 | 合规 | 发信日志用 `TextRedactor.RedactEmail`（`EmailSettingsAppService.cs:55`）；没有发现日志输出令牌或口令。日志记了用户名，不属于联系方式（交由 coding-backend §7 分片判定） |
| 开放应用密钥只在创建或重置时返回一次 | 读代码 | `OpenApplicationAppService.cs:256`、`ResetOpenApplicationSecretOutputDto` | 合规 | 日志只记 Id |
| 仓库验收日志不分发进 template（browser-auth 末条） | grep `template/` 下有没有 `.tmp`、验收日志 | `template/` | 合规 | 没有发现 |

##### 1.3 coding-backend §1 §4–§8、testing.md（后端）、coding-common §3

| 规则（文件 §） | 检查方法 | 范围 | 状态 | 证据 |
| --- | --- | --- | --- | --- |
| 技术栈：.NET 10、EF Core 10、PostgreSQL、Redis、Mapster(`Leistd.ObjectMapping.Mapster`+`IObjectMapper`)、MS DI、Serilog（coding-backend §1） | 人工：csproj/引用 grep | src/*.csproj | 合规 | Application.csproj:22 `Leistd.ObjectMapping.Mapster`；Api/Program.cs:53 Serilog；版本核对归 tech-stack 行（另一子项） |
| 注册归属：每层 `DependencyInjection.cs` 注册本层类型（coding-backend §4） | 人工通读 4 个 DI 文件 + grep `Add(Singleton\|Scoped\|Transient)` | src | 证据复用 | T2-B4（`git show fac8a15e`）；抽查 Domain/DependencyInjection.cs:23-31、Application/DependencyInjection.cs:89-175 均为本层类型 |
| `Program.cs` 只组合各层、组件入口与管道（§4） | 人工通读 | Api/Program.cs | 证据复用 | T2-B4；Program.cs:78-145 只有入口调用与组件 Options；唯一业务逻辑是 :155-168 Redis 缺失告警（组合期观测，非注册） |
| Api 自有类型按关注点注册在 `Api/Auth`、`Api/Hosting` 扩展（§4） | grep | Api | 证据复用 | T2-B4；Auth/AuthenticationExtensions.cs、Hosting/WebHostExtensions.cs 等 |
| 生命周期表（Scoped/Singleton/Transient）（§4） | grep 全部注册 + 读构造函数 | src | 违规待修复 | AppService、领域服务、事件处理器全部 Transient；Scoped 有注释（Application/DependencyInjection.cs:129-130）；两处无状态服务登记为 Scoped 且无注释，见 P2-28 |
| Singleton 不捕获 Scoped（§4） | 读 6 个 Singleton 的构造参数 + 运行时 `ValidateOnBuild` | Api/Auth、HealthChecks | 合规 | DistributedTicketStore.cs:12、ConnectInteractionProtector.cs:19、ResourceSessionRefresher.cs:22、ResourceUserAccessTokenAccessor.cs:21 只依赖单例/访问器；Program.cs:67-73 非生产开启 `ValidateScopes`/`ValidateOnBuild`，集成测试宿主（Testing）随之校验 |
| Development 开启 `ValidateScopes` 与 `ValidateOnBuild`（§4） | 读 | Program.cs | 合规 | Program.cs:67-73 生产以外全部开启（比规范更严）；DbMigrator 由 MigratorRegistrationTests 覆盖构建期校验 |
| 注册方式 `TryAdd*`/`TryAddEnumerable`/`Replace`+注释（§4） | grep 非 Try 注册 | src | 违规待修复 | 证据复用 T2-B4 覆盖各层入口；残留 Hosting/WebHostExtensions.cs:33 `AddSingleton` 且整个入口重复调用会重复登记健康检查名，见 P2-27；Replace 两处均有注释（Api/DependencyInjection.cs:43-47、Infrastructure/DependencyInjection.cs:79-81、Hosting/NotificationExtensions.cs:57-59） |
| 相同登记重复调用不重复生效（§4） | 读测试 + 推演 | 各层入口 | 违规待修复 | Domain/Application/Api 授权有测试（UnitTests/Registration/ServiceRegistrationTests.cs:59、111、193）；`AddInfrastructureServices` 无重复调用测试；`AddMyProjectWebHost` 重复调用会因 `"self"` 健康检查重名在解析 `HealthCheckService` 时抛错，见 P2-27、P2-7 |
| 无约定式自动注册，每个 `IAppService` 实现显式注册；`[UnitOfWork]` 按实现类型（§4） | 脚本：列出全部 `*AppService` 类并在 DI 文件里匹配 | Application | 合规 | 11 个实现全部在 Application/DependencyInjection.cs 以 `TryAddTransient<I…, Impl>` 注册（AuthAppService 两次匹配是接口名），无工厂委托注册 |
| 部署基线 Options 在声明层或组合根绑定，需校验的 `AddOptions<T>()…ValidateOnStart()`（§4、§7） | 脚本：列出全部 `AddOptions<T>` 及其链 | src | 违规待修复 | PasswordHash、OAuth、SessionCookie、VerificationCode、UserRegistration、ExternalAuth 有 `ValidateOnStart`；`RemoteIdentityOptions` 绑定后在组合期自行读值校验，见 P2-6；DefaultAdmin 口令延后到初始化器校验并报键名（LocalSessionAuthenticationExtensions.cs:110-113 注释说明，合理） |
| DTO 命名表（§5） | 文件名与类型名对照 | */Dtos | 证据复用 | T2-B2；分页输入 3 个均 `Get{Entity}PagedInputDto : PageRequest`（OpenApplications/Roles/Users Dtos） |
| DTO 全部 record、一文件一个对外 DTO（§5） | 脚本：统计每个 Dtos 文件的类型数、class 数 | src/**/Dtos | 证据复用 | T2-B2；唯一多类型文件 Application/Auth/Dtos/ExternalLoginsOutputDto.cs:6/20/32，后两者只被它内嵌（AuthMappings.cs:22、ExternalAuthAppService.cs:70-75 仅为构造它） |
| 入参 DTO 属性式，不用位置记录（§5） | 脚本：匹配 `record X(` | src/**/Dtos | 合规 | 位置记录只有输出 DTO：ServiceInfoOutputDto、WhoAmIOutputDto、UserAvatarOutputDto 与 Client/Dtos 两个 |
| 校验只在入口 DTO（§5、coding-common §3.3） | grep 校验特性所在文件 | src | 合规 | Dtos 之外只有 Domain/Users/Options/UserRegistrationOptions.cs:22-41 的 `[Range]`，属 Options 启动校验（`ValidateDataAnnotations`），不是 DTO 校验 |
| 参与消息的属性写 `[Display(Name)]`，每个校验特性显式英文 `ErrorMessage` 且用 `{0}`（§5） | 脚本：逐属性解析特性块 | 25 个 Dtos 文件、61 个带校验的属性 | 合规 | 全部带 `Display`；无 `ErrorMessage` 的命中只有跨行写法（如 ChangePasswordInputDto.cs:15-16）与 Options；本地化形态另由 `scripts/check-i18n.py` 检查 |
| 变量命名：`input`、`query`/`xxxQuery`、`{entity}Repository`、`{entity}DomainService`（§5） | 脚本：正则抽取参数/变量名比对 | src | 违规待修复 | DTO 参数全为 `input`；领域服务注入名全部合规；仓储 8 处、`IQueryable` 2 处不合，见 P2-29 |
| Application 不用 EF Core 扩展与 `Include`，经 `IQueryableAsyncExecuter`（§6） | 引用 + grep | Application/Domain | 合规 | Application.csproj 无 EF Core 引用（:11-64）；grep `Include(`/`Microsoft.EntityFrameworkCore` 在 Application/Domain 无代码命中 |
| 业务库上下文不直接注入（§6） | 既有测试 + grep | Api/Application/Infrastructure | 合规 | IntegrationTests/DbContextAccessTests.cs:18-37 反射断言无构造注入；Api/Hosting/DatabaseSchemaVerificationExtensions.cs:21 是启动期宿主作用域校验迁移，不在请求路径 |
| 映射走 `Mappings/` 的 `IRegister`、不调无参 `Adapt<T>()`、无 `FromXxx`（§6） | grep | src | 合规 | grep `.Adapt<`、`static … From[A-Z]` 无命中；Application/DependencyInjection.cs:81-87 扫描 IRegister |
| 请求外异步、互斥、周期任务（`IBackgroundTaskQueue`/`IDistributedLock`/`AddRecurringJob` 显式 Cluster/EveryInstance），不另起线程或自造锁（§6） | grep `new Thread`、`Task.Run`、`SemaphoreSlim`、`lock(` | src | 合规 | 两个周期任务均显式 `RecurringJobScope.Cluster`（Application/DependencyInjection.cs:105-108、113-114）；唯一 `lock` 是 Api/Auth/SigningKeyRefresh.cs:114 的进程内限频状态（按副本计算，非跨实例互斥），见 P2-31 规范澄清 |
| Data Protection：构造时 `CreateProtector` 一次、用途带版本、解密只捕获 `CryptographicException`（§6） | grep `CreateProtector` 与相邻 catch | src | 合规 | 4 个用途均带 `.v1` 且在字段初始化（TwoFactorDomainService.cs:20-21、DistributedTicketStore.cs:14、ConnectInteractionProtector.cs:31/34）；catch 只并列相邻的 Base64/JSON 解析异常（TwoFactorDomainService.cs:60、ConnectInteractionProtector.cs:58/91），见 P2-31 |
| 异常按位置选型（§7） | 统计全部 `throw new` 并逐个看位置 | src | 合规 | 91 处 `BusinessException`；35 处 `InvalidOperationException` 均在启动/一次性作业/编程契约（如 Application/Initialization/SystemInitializer.cs:171 报键名、Auth/BackgroundJobs/ExpiredUserSessionCleanupJob.cs:56 汇总失败、Api/Controllers/ConnectController.cs:56 OpenIddict 请求缺失）；无 `throw new Exception` |
| 部署配置错误启动期失败；组合期只为选择实现读配置并报键名（§7） | grep `configuration[`、`GetSection`、`GetConnectionString` 并判别用途 | src | 违规待修复 | Redis/KeysPath/外部提供商/证书/ServiceAuth 是实现选择，合规；Api/Auth/RemoteTokenAuthenticationExtensions.cs:31-37、:76-77 在组合期读取并校验 Issuer/Audience/ClientId/ClientSecret 的值，见 P2-6 |
| 日志英文、结构化模板、不含敏感信息（§7、coding-common §1） | 脚本：抽取全部 75 处 `Log*` 调用的实参 | src | 合规 | 全部英文、无 `$"` 插值；邮箱经 `TextRedactor.RedactEmail`（Settings/AppServices/EmailSettingsAppService.cs:55）；只记 Id、用户名、ClientId、租户 Id，无口令/令牌/联系方式 |
| Api 目录表与命名空间跟随目录（§8） | 脚本：比对每个文件命名空间与目录 | Api | 违规待修复 | 顶层目录与表一致（Resources/ 为 JSON 词条）；Api/Localization/ApiResource.cs:1 命名空间仍是 `CompanyName.ProjectName.Api`，见 P2-30 |
| 周期任务放 Application 模块 `BackgroundJobs/`，无顶层 `Jobs/`；无 `Extensions` 命名空间（§8） | find + grep | src | 合规 | Application/Auth/BackgroundJobs/*.cs；grep `namespace .*Extensions;` 无命中 |
| 请求体上限沿用 Kestrel 默认，大上传单独放宽（§8） | grep | Api | 合规 | 无 `RequestSizeLimit`/`MaxRequestBodySize` 全局改动；Program.cs:80 注释 |
| 两个测试项目与目录分工（testing §2.1） | find 目录 | tests | 违规待修复 | UnitTests 多出顶层 `Mappings/`；两类纯逻辑测试放在 IntegrationTests，见 P2-26 |
| 集成测试真实 PostgreSQL，不用 EF InMemory/SQLite（testing §2.2） | grep 包引用与 `UseInMemoryDatabase`/`Sqlite` | tests | 合规 | IntegrationTests 引用 `Testcontainers.PostgreSql`；`InMemory` 命中全是 `AddInMemoryCollection` 配置源 |
| 每类一个 `IClassFixture<ProjectWebApplicationFactory>`，不每用例建宿主（testing §2.1） | grep 每个 *Tests.cs | IntegrationTests | 合规 | 41 处 `IClassFixture`；无 fixture 的两类不建宿主（见 P2-26）；`WithWebHostBuilder` 最多 TenancyTests 13 处，属配置变体，未逐个判别是否可归组（人工复查项） |
| 假时钟 `FakeTimeProvider`、`RemoveAll<TimeProvider>`（testing §2.2） | grep | tests | 合规 | 两项目都引用 `Microsoft.Extensions.TimeProvider.Testing`；`RemoveAll<TimeProvider>` 3 处；无手写 `TimeProvider` 子类 |
| 日志用 `FakeLogger`，不手写替身（testing §2.2） | grep `: ILogger`/`ILoggerProvider` | tests | 违规待修复 | IntegrationTests/Fixtures/WarningLogCapture.cs:9-47 手写 `ILoggerProvider`/`ILogger`，3 个测试类使用，见 P2-25；BrowserOriginTests.cs:159、SigningKeyRotationTests.cs:218 已用 FakeLogger |
| 测试方法名英文、下划线分隔（testing §2.2） | 既有闸门 + 脚本统计 | tests | 证据复用 | `scripts/check-test-names.py`（check-all）查中日韩字符；脚本统计 385 个 `[Fact]/[Theory]` 方法名全部含下划线 |
| 注册测试：各层入口覆盖结果、生命周期、重复调用，有意覆盖测两种顺序（testing §2.2） | 读 ServiceRegistrationTests | UnitTests/Registration | 违规待修复 | 证据复用 T2-B4 的 Domain/Application/Api 授权部分；Replace 三处均有两种顺序（:143、:169、:193）；`AddInfrastructureServices`/`AddPersistenceServices` 无结果、生命周期与重复调用测试，见 P2-7 |
| `PasswordHash:IterationCount` 在工厂调到 1000，默认值由 `PasswordHashingTests` 钉住（testing §2.1） | grep | tests | 合规 | Fixtures/ProjectWebApplicationFactory.cs:139；UnitTests/Infrastructure/PasswordHashingTests.cs |
| 删除幂等，只吞 NotFound（coding-common §3.6） | 读全部删除/撤销/解绑用例 | Application | 违规待修复 | Role（RoleAppService.cs:195-202）、会话撤销（UserSessionAppService.cs:68-70）、解绑（ExternalAuthAppService.cs:114-116）幂等；User（UserAppService.cs:460）与 OpenApplication（OpenApplicationAppService.cs:239）不存在时抛 NotFound，见 P2-5 |
| 外部调用由统一开关决定，不靠调用方 opt-in（coding-common §3.7） | grep `dryRun`、`bool Send…` 等入参 | src | 合规 | 无调用方开关入参；发信由 `Leistd:Email:Smtp` 配置决定（Api/appsettings.json:167-174） |
| 枚举强类型、只在边界转换，对外契约值小写（coding-common §3.8） | grep `enum` 及其出现在 DTO/实体的位置；读 JSON 配置 | src | 合规 | 4 个枚举均为内部状态，不出现在 DTO 或实体属性；对外分类字段（ApplicationType/ClientType）取 OpenIddict 小写常量（OpenApplicationAppService.cs:33-44）；WebApiJson.cs 用 camelCase 枚举转换器，多词枚举将得到 `twoFactorRequired` 而非全小写，见 P2-32 |
| 接口与实现签名同步；正文不内联全限定名（coding-common §3.9） | 编译器 + 正则 | src+tests | 证据复用 | T2-B7；残留 4 处均为同名歧义并就地注释（Api/Hosting/WebHostExtensions.cs:108、IntegrationTests/DeploymentSafeguardsTests.cs:121/145、ExternalAuthenticationTests.cs:577、DbContextAccessTests.cs:26），规范原文没有写这条例外，见 P2-33 |
| 枚举以字符串持久化 `HasConversion<string>().HasMaxLength(32)`（coding-backend §3.8） | grep 实体枚举属性与 `HasConversion` | Domain/Entities、Infrastructure | 不适用 | 模板实体没有枚举类型属性（4 个枚举只用作方法返回值，如 Domain/Users/Entities/User.cs:405）；`HasConversion` 无命中 |

##### 1.4 service-invocation、tech-stack、project-structure、deploy README、template README、docs/template/development-guide

| 规则（文件 §） | 检查方法 | 范围 | 状态 | 证据 |
| --- | --- | --- | --- | --- |
| service-invocation「调用其他服务」：工作负载身份全局注册一次，返回的 IHttpClientBuilder 上显式选认证模式 | 人工 grep `AddServiceAuthentication`/`AddClientCredentials` | Infrastructure、Client、集成测试 | 合规 | `Infrastructure/DependencyInjection.cs:178-183`（Resource 回源客户端 `AddRemoteTenantConnectionStore("Identity")` 后 `AddClientCredentials()`）；`IntegrationTests/ServiceInvocationTests.cs:49-51` |
| service-invocation：用户委托只取已验证令牌，不经 ICurrentUser/请求头伪造 | 人工 | Api/Auth | 合规 | `Api/Auth/RemoteTokenAuthenticationExtensions.cs:57-58` 注册 `ResourceUserAccessTokenAccessor`（读 Bearer 或服务端票据）；全库无 `X-User`/`X-Tenant` 委托头读取（唯一命中是 `UserSessionValidator.cs:70` 的 `X-Tenant-Invalid` 注释） |
| service-invocation：RemoteServiceException / ServiceClientException 映射 500/502/503/504，不透传远端状态 | grep 映射登记 | Api/Hosting | 证据复用 | 映射由框架 `AddServiceClient` 自动登记，模板没有覆盖登记（`Api/Hosting/ExceptionMappings/*` 无 `ServiceClientException`）；框架行为由阶段二 T2-K4 的测试覆盖（`git show fac8a15e`），输入未变 |
| service-invocation「被其他服务调用」：机器令牌不满足自然人默认策略 | 测试核对 | 集成测试 | 合规 | `ServiceInvocationTests.cs:56-59` 断言机器令牌调用 whoami 返回 403 |
| service-invocation：Token Exchange 令牌 120 秒、不超过 subject exp，去掉角色、超管与 auth_time，带 act | 人工 | ConnectController、TokenExchangeExpirationHandler | 合规 | `Api/Controllers/ConnectController.cs:231-238`；`Api/Auth/TokenExchangeExpirationHandler.cs` 取 min(subject exp, 输出 exp) |
| service-invocation：诊断入口 `GET /api/v1/service-info/whoami` | 对照代码条件 | ServiceInfoController、Client | 违规待修复 | 文档 `service-invocation.md:23` 无条件声明；代码 `Api/Controllers/ServiceInfoController.cs:31-43` 与 `Client/IMyProjectClient.cs:19-27` 只在 `LocalIdentity` 下生成，Resource 形态没有该端点（见 P2-8） |
| service-invocation「发布 Client 包」：只依赖 Refit 与服务客户端框架，不引用服务内部程序集；经 `AddRefitServiceClient` 注册，返回 IHttpClientBuilder | 读 csproj 与入口 | Client | 合规 | `Client/CompanyName.ProjectName.Client.csproj:15-20` 只引用 `Leistd.ServiceClient.Refit/OAuth`、`Refit`；`Client/DependencyInjection.cs:21-25` |
| service-invocation：Identity 与 Resource 的 issuer/audience 配置键 | 对照 appsettings | Api appsettings | 合规 | `appsettings.json:3-14`（Issuer/ClientId/ClientSecret/Audience）；`OAuth:ApiResources`、`OAuth:Resource` 在 `appsettings.json:34-37` 与 `OpenIddictServerExtensions.cs:52-58` 有校验 |
| tech-stack §2 后端：.NET 10+、EF Core 10+、PostgreSQL 15+、Redis 7+ | 读 csproj、Directory.Packages.props、compose | backend、deploy | 合规 | 8 个 csproj 均 `net10.0`；`Directory.Packages.props` 中 EF Core 10.0.11、Npgsql 10.0.3；`deploy/docker-compose*.yml` 为 `postgres:15-alpine`、`redis:7-alpine` |
| tech-stack §2 前端：Angular 22+、Spartan 1+、Tailwind 4+、Geist 5+、TS 6.0+、Transloco ~8.4.0、TanStack Table 9+ | 读两份 package.json | `frontend/package.json` 与 `.template.config/localization/frontend/package.json` | 合规 | Angular 22.2.0、brain/cli 1.5.0、tailwind ^4.3.3、geist ^5.3.0、TS ~6.0.3、transloco ~8.4.0（仅本地化版）、angular-table 9.2.4 |
| coding-backend §1 技术栈（Mapster 经 Leistd.ObjectMapping.Mapster、Serilog） | grep | backend | 合规 | `Directory.Packages.props` 有 `Leistd.ObjectMapping.Mapster`、`Serilog.AspNetCore`；`Program.cs:183` 用 Serilog 请求日志 |
| docs/template/development-guide §2：模板只通过 PackageReference 消费框架包 | grep `ProjectReference` | backend csproj | 违规待修复 | 无指向框架的 ProjectReference；但 `backend/Directory.Packages.props:11-12` 注释写"本地源码模式由 ProjectReference 取代"，与事实不符（见 P2-36） |
| development-guide §3.6：维护事实不写进 template 源码注释 | grep `release.yml`、`leistd-net` | template 全部 | 违规待修复 | `backend/Directory.Build.props:4-7` 引用仓库 `.github/workflows/release.yml` 与"仓库根 VERSION"，生成项目里不存在（见 P2-36） |
| development-guide §3.6：Identity 组件端点前缀与 Resource `RoutePrefix` 一致 | 读 ComponentEndpoints 与配置 | Api | 合规 | `ComponentEndpoints.cs:39,96` = `/api/v1/tenant-connections`；Resource 未覆盖 `RoutePrefix`，取框架默认同值 |
| development-guide §3.7：错误码不随本地化裁剪 | grep `#if (IncludeLocalization)` 包裹范围 | backend src | 合规 | 10 处守卫只包本地化注册与通知渲染（`Program.cs`、`NotificationSecurityAlertPublisher.cs`、`WebHostExtensions.cs:45`），无一处包住 `BusinessException` 或 `*ErrorCodes` |
| development-guide §3.7：`*ErrorCodes` 唯一、格式、资源键 | 闸门 | template 源码（全功能） | 证据复用 | `check-all.ps1` 的"i18n 词条键一致"调用 `template/scripts/check-i18n.py` 判据 5（`scripts/check-i18n-keys.ps1:30`），本轮输入未变 |
| development-guide §3：条件生成无残留、符号、条件块 | 闸门 | template | 证据复用 | `check-template-symbols.ps1`、`check-template-conditional-blocks.py`、`check-using-guards.py`、`test-template-generation.py`；阶段二结果 32 道通过（fac8a15e §6.4），后端输入未变 |
| development-guide §6：API 启动不执行 MigrateAsync/EnsureCreated；DbMigrator 先行 | grep | Api、Infrastructure | 合规 | `MigrateAsync`/`EnsureCreated` 只在 DbMigrator 出现；compose `backend.depends_on.db-migrator: service_completed_successfully`（`docker-compose.yml:147-148`） |
| development-guide §7：模板内既有反向决定（`RoleAppService.DeleteAsync` 不做成一个事务） | 对照代码与测试 | RoleAppService | 违规待修复 | 文档过时：`RoleAppService.cs:187-192` 标了 `[UnitOfWork]` 且注释写明整体回滚（见 P2-12） |
| development-guide §11：不留 TODO | grep `TODO\|FIXME\|HACK` | backend、deploy、Dockerfile | 合规 | 无命中 |
| development-guide §11：没有读取方的配置键与文件直接删 | 逐键 grep 读取方 | appsettings、compose、模板根 | 违规待修复 | `deploy/docker-compose.yml:125,128` 的 `ExternalAuth__*__RedirectUri` 无读取方（`ExternalAuthOptions.cs` 只有 ClientId/ClientSecret）；`docker-compose.yml:134` `Serilog__MinimumLevel__Override__MyProject` 匹配不到 `CompanyName.ProjectName.*` 日志类别；`template/VERSION`（1.11.6，自 init 未改）全仓无读取方（见 P2-9、P2-34、P2-35） |
| browser-authentication 末段：验收日志不分发进 template | find `*.log`/`.tmp`/`*.trx` | template | 合规 | 无命中 |
| project-structure §1 项目根 | 对照 `ls -a template` 与 template.json 排除 | 模板根 | 违规待修复 | 实际还有 `scripts/`（`IncludeLocalization` 或 `SpaFrontend && IncludeOperationRecords` 时生成，testing.md §5 引用）与 `VERSION`，树里都没有（见 P2-35） |
| project-structure §2 后端六个项目 | ls | backend/src | 合规 | Domain、Application、Infrastructure、Client、DbMigrator、Api |
| project-structure §2：测试项目按实际类型，不建空项目 | ls | backend/tests | 合规 | 只有 UnitTests、IntegrationTests |
| project-structure §3 前端分层 | ls | frontend | 违规待修复 | 实际还有 `libs/`（Spartan helm 副本，tech-stack.md:13 说明"复制进 libs/ui/"），树里缺；归 P3 复核，本处只登记结构漂移（P2-35） |
| project-structure §4：docs 只有 README、standards，按需目录不预建 | ls | template/docs | 合规 | `README.md`、`standards/`、`deploy/`（deploy README 是交付的部署说明） |
| project-structure §4：目录与普通 Markdown 小写 kebab-case | find | 模板根（不含 backend、frontend/src、libs） | 违规待修复 | `frontend/public/i18n/operationRecords`、`frontend/public/i18n/openApp` 为 camelCase；`_mock` 是规范自身登记的名字。归 P3 与 frontend-i18n 的 scope 命名一起判定（P2-37） |
| deploy README：Data Protection 非开发环境缺存储即启动失败 | 读代码 | Infrastructure | 合规 | `DataProtectionExtensions.cs:47-56` 抛 InvalidOperationException 并给出键名 |
| deploy README：缺 Redis 不阻止启动，缓存回落有 Warning | 读代码 | Program | 合规 | `Program.cs:151-168` |
| deploy README：ForwardedHeaders KnownProxies/KnownNetworks | 读代码与配置 | WebHostExtensions、appsettings | 合规 | `WebHostExtensions.cs:94-111` 读取并校验；`appsettings.json:62-66` |
| deploy README：令牌证书至少一项、缺 Path 指出带下标的键 | 读验证器 | Domain/Auth/Options | 合规 | `OAuthOptionsValidator.cs:36-48` 报 `OAuth:SigningCertificates:{index}:Path`；compose 以 secrets 挂载（`docker-compose.yml:104-109,139-141,234-238`） |
| deploy README：AccessTokenLifetime 整秒且长于 1 分钟，启动拒绝并指出该键 | 读验证器 | 同上 | 合规 | `OAuthOptionsValidator.cs:27-32` |
| deploy README：PasswordHash 600,000 默认、`PasswordHash__IterationCount` 可调 | 读 Options | Infrastructure | 合规 | `PasswordHashOptions.cs:14,17`；`DependencyInjection.cs:123` 校验 |
| deploy README：`SessionCookie:ExpireDays`/`SameSite` | 读 Options | Api | 证据复用 | 阶段二 T2-B8（`git show fac8a15e`）；`Options/SessionCookieOptions.cs`、`AuthenticationExtensions.cs:44,56` 未变 |
| deploy README：`Leistd:Lock:Redis:KeyPrefix` 配置文件给应用名 | 读 appsettings | Api | 合规 | `appsettings.json:103-107` |
| deploy README：操作记录保留默认关闭、配置键 | 读 appsettings | Api | 合规 | `appsettings.json:116-121` |
| deploy README：`ConnectionStrings:MigrationTarget` | grep | DbMigrator | 合规 | `DatabaseMigrationRunner.cs:128` |
| deploy README：API DML 身份、DbMigrator DDL 身份 | 读 compose 与初始化脚本 | deploy | 合规 | `docker-compose.yml:15`（migrator 用 postgres）、`:60`（APP_DB_USER）；`deploy/postgres/01-create-app-role.sh:16-19` 只授 DML |
| deploy README：K8s 探针 `/api/health/ready`、`/api/health/live` | grep | Program | 合规 | `Program.cs:206,210`；compose healthcheck `docker-compose.yml:160-162` |
| deploy README：SignalR 背板模板未内置 | grep `AddStackExchangeRedis`/Backplane | backend | 合规 | 只有 `AddStackExchangeRedisCache`（`Infrastructure/DependencyInjection.cs:106`），无 SignalR 背板 |
| deploy README：容器以 UID 1654 运行 | 读 Dockerfile | Dockerfile | 合规 | `Dockerfile:58,84` `USER $APP_UID` |
| deploy README：必填项 `${VAR:?}`，机密清单见 `.env.example` | 机械比对 compose 变量与 `.env.example` | deploy | 违规待修复 | 变量集合一致（compose 独有的只有注释行里的 `OAUTH_SIGNING_CERTIFICATE_NEXT_PASSWORD`）；但 SMTP 凭据与地址、`VerificationCodes:Key`、外部登录 ClientId/ClientSecret 既不在 `.env.example` 也不在 compose 变量里，生产 compose 的 SMTP 停在 `localhost:1025`（见 P2-10） |
| deploy README：生产 `Cors:AllowedOrigins` 默认空，不承担浏览器认证 | 读 appsettings 注释 | Api | 违规待修复 | `appsettings.json:68` 注释"前端部署在另一个源时列出它的地址"，与 auth.md「浏览器认证」及 deploy README 第 8、124 行的同源契约相反（见 P2-11） |
| deploy README：外部登录回调登记 `/api/v1/external-auth/{provider}/signin` | 对照 compose 注释与代码 | deploy、Api | 违规待修复 | `docker-compose.yml:124-128` 说回调是前端页、由前端把授权码提交给 API；实际 `ExternalAuthenticationExtensions.cs:47,70` 的 CallbackPath 为 `/api/v1/external-auth/{google,github}/signin`（见 P2-9） |
| deploy README 本地验证命令、文件存在 | ls | deploy | 合规 | `.env.example`、`docker-compose.yml`、`docker-compose.override.yml`、`docker-compose.dev.yml` 都在 |
| template/README 本地运行与验证命令 | 对照 testing.md 与 package.json | README | 违规待修复 | `README.md:211-220` 列了后端测试、前端 lint/build 和 check-i18n，漏了前端单测 `npm test` 与 `check-operation-action-i18n.py`（testing.md §3、§5 要求跟测试一起跑）（见 P2-38） |
| template/README 结构与能力描述 | 对照目录与条件 | README | 合规 | 能力列表的条件与 template.json 有效能力一致；树只列主要目录（`scripts/` 缺失随 P2-35 一并处理） |

#### A.3 P3 模板前端（来源 `p3-findings.md` §1）

##### 1.1 coding-frontend.md

| 规则（§） | 检查方法 | 范围 | 状态 | 证据 |
| --- | --- | --- | --- | --- |
| §1 禁 FormsModule/ReactiveFormsModule/ngModel | 闸门（eslint `no-restricted-imports`）+ grep | src | 证据复用 | `eslint.config.mjs` no-restricted-imports；grep `ngModel\|FormsModule` 0 命中 |
| §2 目录与依赖方向矩阵 | 闸门（`import-x/no-restricted-paths` + `local/feature-boundaries` + `no-unresolved`） | src、_mock | 证据复用 | 阶段二 `fac8a15e`；之后仅 `signalr-service.ts` 改动且仍在 lint 范围 |
| §2 路由懒加载例外 | 闸门（`isRouteLoader`） | `*.routes.ts` | 证据复用 | 同上 |
| §2 `_mock` 只由装配点与 environments 引入；业务用 `MOCKED_URL` | 闸门 + grep | src | 证据复用 | grep `_mock` 非 spec 命中仅 `app.config.ts`、`app.interceptors.ts`、`environments/environment.base.ts`、`core/mock/mocked-url.ts`（注释）；`MOCKED_URL` 注入点 5 处 |
| §2 `import()` 路径为字面量 | 闸门（`no-restricted-syntax`） | src | 证据复用 | 同上 |
| §3 命名表（文件后缀、类名后缀） | 人工脚本（扫描 `@Component/@Injectable/@Pipe/@Directive` 与文件名） | src/app | 违规→P3-4 | 组件无 `Component` 后缀、服务均 `-service.ts`+`Service`、管道 `app-date-pipe.ts`、守卫/拦截器/handler/layout 命名合规；`shared/models/permission.ts` 不是 `.model.ts` 且装 DTO；`core/interceptors/http-context-tokens.ts` 是令牌文件不属拦截器类型（合规） |
| §3 selector `app-`/`app`+camelCase | 闸门（angular-eslint） | src | 证据复用 | `component-selector`/`directive-selector` |
| §4 API 服务只封装 HTTP，返回 DTO Observable，不持状态 | 人工（逐个读 `features/*/services`） | 8 个 API 服务 | 违规→P3-19 | 7 个仅注入 `HttpClient`；`account-service.ts` 在 `tap` 里回写 `AuthService.setCurrentUser` |
| §4 应用级服务放 `core/services` | 人工 | core | 合规 | 认证、主题、语言、设置上下文、启动均在 `core/services`/`core/settings` |
| §4 页面状态组件 signal / 组件级 `@Injectable()` | 人工 | features | 合规 | `settings-page-state.ts` 为组件级 `@Injectable()` 并在组件 `providers` 提供 |
| §4 DTO 放 `dtos/`，models 不放 DTO | 人工脚本（扫 `export interface *Dto` 位置） | src | 违规→P3-4 | `shared/models/permission.ts:87-125`（6 个 DTO）、`core/services/signalr-service.ts:50` |
| §4 作用域选择（root/组件/路由 providers） | 人工 | src | 合规 | 页面状态组件级；`provideTranslocoScope` 在路由 |
| §4 一律 `inject()` | grep `constructor(` 带参、`@Inject(` | src、_mock | 合规 | 仅 `avatar-image.ts:19`（错误类）、`user.model.ts:18`（模型类）有参构造，非 DI；`@Inject(` 0 命中 |
| §4 构造函数只做属性赋值与生命周期接线 | 人工脚本（抽取 33 个构造函数顶层语句） | src | 违规→P3-7 | 首次加载/流程写在构造函数 10 处；`ngOnInit` 中无 `effect`/`takeUntilDestroyed`（不需传 injector） |
| §5 Signals 管状态 | 人工 | src | 合规 | — |
| §5 媒体查询用 `BreakpointObserver`，不手写 `matchMedia` | grep | src | 合规 | `matchMedia` 0 命中；`table-viewport.ts` 用 `BreakpointObserver` |
| §5 启动/拦截器不读 `Router.url` | grep | core/interceptors、启动 | 合规 | `router.url` 仅出现在 `layout-service.ts:32`、`system-settings.ts:95`、`settings-shell.ts:49`（导航后的页面/布局，不属启动与拦截器） |
| §5 列表分页/排序/筛选以 URL 为唯一来源、非法回退默认、不存 localStorage | 人工 + grep | 5 个列表页 | 合规 | `shared/utils/table-query-state.ts`（非法页码/页大小/排序列回退）；users/roles/tenants/open-applications/operation-records 均从 `queryParamMap` 派生；features 内 localStorage 0 命中 |
| §5 显示偏好同源（已做成 setting definition） | 人工 | language-switcher、偏好页 | 合规 | `language-switcher.ts:88-100` 已登录走 `setForCurrentUser(Display.Language)` 后再 `applyAccountLang` |
| §5 显示偏好同源（未做成 definition 只存 localStorage 且不进偏好页） | 人工 | theme | 合规 | 主题只在 `theme-service.ts` 存 localStorage；`features/settings` 无主题项 |
| §5 显示偏好同源（本服务不拥有） | 人工 | Resource 形态 | 不适用 | Resource 形态的偏好面板读写本服务自己的 `Display.*` 设置，模板里没有跨服务只读的显示偏好；账户资料/安全面板在 `!LocalIdentity` 下整体裁掉 |
| §6 拦截器归一化 Problem Details、401 处置 | 测试 | `http-error-interceptor` | 证据复用 | 阶段二补的拦截器单测；文件未改 |
| §6 feature 决定反馈；按 detail/title 取文案 | grep `toast.error(` 取值 | src | 合规 | 页面一律经 `applicationErrorMessage`；`GlobalErrorHandler` 只兜底非 HTTP 错误（阶段二单测） |
| §6 5xx 不展示技术细节、附 traceId | 测试 | `application-http-error` | 证据复用 | 阶段二 |
| §6 不吞错误；按稳定 code 分支，不按单个状态码 | grep `status ===`、`catchError` | src | 违规→P3-3 | `permission-grant-dialog.ts:293` 按 409 分支；`startup-service.ts:81` 的 401 属启动认证处置（合规）；静默 `catchError` 3 处均为"显示空状态/静默"允许形态并有注释 |
| §6 不兼容响应信封 | 人工 | interceptor | 合规 | — |
| §7 每个后端端点都有 Mock | 人工脚本（后端 `[Http*]`/`Map*` 路由 ↔ `_mock/api` 键） | 62 个控制器端点 | 违规→P3-5 | 前端实际调用而无 Mock 的 6 个端点；浏览器跳转型端点（challenge、`/auth/login` 302、avatar 图片）不适用 |
| §7 Mock 只放 `_mock`，单测相邻 | 人工 | _mock | 合规 | `_mock/api/tenant.spec.ts` 等与被测文件相邻 |
| §7 Mock 与后端可观察行为一致 | 抽查 | authorization、tenant | 合规 | 权限并发 409 带 `Permission:ConcurrencyConflict`（与框架 `PermissionErrorCodes` 一致） |
| §8 含映射/分支/状态的服务、pipe、复杂函数必须有单测 | 人工脚本（无相邻 spec 的逻辑文件 + 反查间接覆盖） | src | 违规→P3-8、P3-10 | `zoned-time.ts` 无任何 spec；`role-service.ts`、`tenant-service.ts` 有参数映射无自身 spec |
| §8 单测不触发真实下载/打印/跳转 | 人工 | spec | 证据复用 | 阶段一/二已建立 `download-file.spec.ts` 写法；未见新增下载点 |
| §9 业务日期用 `appDate`，不用 `date` | grep `\| date`、`DatePipe`、`toLocale*` | src | 合规 | `\| date` 0 命中；15 处 `appDate` 均传 `displayTimeZone()`、`displayLocale()`；`login-devices.ts:78` 用 `formatAppDate` |
| §9 时区候选按"能否渲染"判定 | 人工 | `setting-choices.ts` | 合规 | `supportedValuesOf` 只作候选来源，逐个构造 formatter 判定（:107、:177） |
| §10 推送只表示重新查询 | 测试 | roles | 证据复用 | `roles.spec.ts:174-204` 三条订阅用例；`signalr-service.spec.ts` 本阶段新增用例 |

##### 1.2 frontend-ui.md

| 规则（§） | 检查方法 | 范围 | 状态 | 证据 |
| --- | --- | --- | --- | --- |
| §1 加载中/失败/为空三态、失败可重试 | 人工抽查 + 浏览器 | 列表、通知、设备、两步验证 | 合规（静态）／待浏览器验证（交互） | `notifications.html:81-98`、各 table 空态；浏览器 B-9 |
| §1 可访问性：键盘可达、错误与字段关联 | eslint `templateAccessibility` + 浏览器 | html | 证据复用／待浏览器验证 | 键盘用例见 B-8 |
| §1 减少动效：不播位移/缩放，保留颜色与焦点 | Spartan 差异核对 + 浏览器 | libs/ui、layout | 合规（静态）／待浏览器验证 | §1.4 Spartan 行；B-5 |
| §1.1 TanStack 服务端分页/排序、复用 paginator/faceted-filter | 人工 | 5 个表 | 合规 | — |
| §1.1 列按 primary/secondary/tertiary 裁剪 | 人工（列 meta） | 5 个表 | 合规 | 各表 `priority` 定义；见 user-table.ts:168-187 等 |
| §1.1 带吸附操作列的表外框挂 `[appTableFit]`+`[appTableFitContent]` | grep | 4 个带操作列的表 | 合规 | users/roles/tenants/open-applications `.html:9-10`；operation-records 无操作列（不适用） |
| §1.1 被裁列由 TanStack 行展开补偿、按 `getRowId` 记，不另建展开状态 | grep | 5 个表 | 合规 | 各表 `getRowId`；`table-features.ts:47 autoResetExpanded:false`；无自建展开信号（permission-grant-dialog 的折叠组不是表格） |
| §1.1 操作列 ≤3 平铺、>3 两个高频+`…`；`<sm` 全收进 `…`；破坏性 destructive+分隔线 | 人工逐表 | 4 个表 | 合规（静态）／待浏览器验证 | users 7 项=2+…；tenants 5 项=2+…；roles、open-apps 3 项平铺；平铺按钮 `hidden sm:inline-flex`、菜单项 `sm:hidden`；删除项前 `hlm-dropdown-menu-separator` + `variant="destructive"` |
| §2.1 组件库优先，不在 shared 重建组件库 | 人工 | src | 违规→P3-15 | `resource-login.html:8` 原生 `<button class="rounded border">` 未用 `hlmBtn` |
| §2.2 Tailwind 原子类、不写具体色值 | grep 色板/hex/rgb | src | 违规→P3-20（规范缺例外） | 页面色值仅剩图像遮罩 `bg-black/25 text-white`、二维码 `bg-white`、Google 品牌 SVG `fill="#…"`，均为合理例外但规范未写 |
| §2.2 auth-shell 共用 | 人工 | account | 合规 | 登录、注册、强制两步验证均包在 `auth-shell` |
| §2.3 有限语义色、分类标签用中性色 | grep `hlmBadge` variant | src | 违规→P3-16、P3-2（规范例子缺 success） | 分类标签用 `default`（主色）：open-app 客户端类型、授权范围、faceted-filter 已选标签 |
| §2.4 共享与展示组件 OnPush | 脚本（所有 `@Component` 查 OnPush） | src、libs/ui | 合规 | 仅 `app.ts`（根）与 `layout/empty/empty-layout.ts`（布局）未设，二者不属共享/展示组件 |
| §2.4 单一职责、单向数据流 | 人工抽查 | widgets | 合规 | 表格 widget 只 `input`/`output`，请求在页面 |
| §2.5 图标 `provideIcons` 组件级按需注册 | 脚本（模板与 TS 中 `lucide*` ↔ 本组件 `provideIcons`；动态图标反查渲染组件） | 62 个组件 | 合规 | 无缺失（唯一告警 `operation-records` 为注释中的名字，日期组件自带注册）；导航动态图标由 `default-sidebar.ts`、`workspace-nav.ts`、`user-menu.ts` 注册；无全局注册 |
| §2.6 Signal Forms + Spartan Field 族 | grep `<label` 非 `hlmFieldLabel` | html | 违规→P3-18 | `two-factor-setup.html:59`、`open-application-edit-dialog.html:289` |
| §2.6 校验提示按 `error.kind` 取词条 | 闸门 | html/i18n | 证据复用 | `check-i18n.py`（Signal Forms 类型在 `validation` 段有句子） |
| §2.7 Spartan 维护：已定制组件登记 | 离线还原上游 1.5.0 + diff | libs/ui 38 组件 | 违规→P3-1、P3-2 | 见 §1.4 |
| §2.8 字号档位、无档外值（图标/spinner 除外） | grep `text-[..]`、`text-(base\|lg\|xl\|2xl\|3xl…)` | src | 违规→P3-11、P3-12 | `text-[10px]` 仅 `setting-section.html:182` 的 spinner（允许）；页面级违例见 P3-11；16px 标题字重规范与组件默认冲突见 P3-12 |
| §2.8 字重只用 400/500/600 | grep `font-(bold\|extrabold\|black\|light\|thin)` | src | 合规 | 0 命中 |
| §2.8 输入框 `text-base md:text-sm` 不改 | 上游 diff | libs/ui/input | 合规 | `hlm-input.ts` 与上游只差 `pointer-coarse:h-11` |
| §2.8 圆角三档 | grep `rounded*` | src | 违规→P3-15 | `resource-login.html:8 rounded`；`default-sidebar.html:57 rounded-md`（图标块）；组件内小元素 `rounded-sm` 4 处跟随所在组件（合规） |
| §2.8 当前项 `bg-primary/10 text-primary font-semibold` | grep `data-active`/`aria-[current` | 侧栏、顶栏、设置导航 | 违规→P3-17 | `default-sidebar.html:93`、`workspace-nav.html:40,75`、`settings-shell.html:30,67` 合规；`workspace-nav.html:97` 偏离 |
| §2.8 间距用 gap，不用 `space-*` 与逐元素外边距；页面外框 `p-4 sm:p-6` | grep `space-[xy]`、`m[trblxy]-`、页面外框 | src | 违规→P3-13、P3-14 | `space-*` 0 命中；兄弟间外边距约 60 处；dashboard ×2、workspace-placeholder 外框不符 |
| §2.8 暗色五层表面、页面不写 `dark:` 颜色覆盖 | grep `dark:` + 浏览器 | src | 违规→P3-17／待浏览器验证（层级） | `default-header.html:72`、`workspace-nav.html:97`；`faceted-filter.ts:152` 复刻上游 checkbox（组件内部，合规）；层级亮度见 B-4 |
| §2.8 图标+文案空态用 flex 列 gap-2，不靠 `block` | grep | src | 违规→P3-13 | 5 个表空态用 `mb-3`/`mt-1`/`m-0` 而无 `gap`；`ng-icon … block` 0 命中；通知面板空态合规 |
| §2.8 字段说明不做独立方框 | 人工 | 表单 | 违规→P3-18 | `open-application-edit-dialog.html:286-298` 会话绑定说明在带边框块内且不用 `hlm-field-description`（其余口令规则已在描述位） |
| §3 两区菜单定义只在 `navigation-service.ts` | 人工 | layout | 合规 | — |
| §3 分组表（含「个人（仅工作空间）」组） | 人工 + git 史 | navigation-service | 违规→P3-6（规范陈旧） | `e74bdf93` 删除了 `groupPersonal`，规范仍写该组与 `placement:'end'` 图标按钮 |
| §3 不设兜底组 | 人工 | 同上 | 合规 | Work/Identity/Developer/Audit/System；Work/Business |
| §3 平台菜单分组与后端权限分组同名同序；标题与根权限显示名一致 | 人工对照 | `navigation-service.ts` ↔ `PermissionDefinitionProvider.cs`、`en.json` | 合规 | 组序 Identity→Developer→Audit→System 一致；`User Management`/`Role Management`/`Tenant Management`/`Open Applications`/`Operation records`/`System settings` 逐字一致 |
| §3 菜单权限列进 `PLATFORM_ENTRY_PERMISSIONS`，`default-sidebar.spec.ts` 核对 | 人工 + 测试 | permission.ts、spec | 合规 | `permission.ts:69-84`；`default-sidebar.spec.ts:146-149` |
| §3 个人设置只有一处、头像菜单不放切换租户 | 人工 | user-menu | 合规 | `user-menu.ts:229-234` 进 `/workspace/settings`；无租户切换 |
| §3 按权限裁剪、整组为空整组消失 | 人工 + 测试 + 浏览器 | navigation-service | 合规（静态）／待浏览器验证 | `navigation-service.ts:307-310`；B-7 |
| §3 不从 `TenantContextService` 推导侧别 | grep | src | 合规 | 只在 auth-service、拦截器、account 使用；`user-menu.ts:276-282` 注释明确不取 |
| §3 页面标题只设 `LayoutService.title`，不自调 `Title` | grep `Title`、`title.set` | src | 合规 | `Title` 仅 `app.ts:157`；11 个布局页在构造函数/effect 设标题，`default-header.ts` 销毁时清空 |
| §3 设置面板走子路由 | 人工 | workspace.routes、platform.routes | 合规 | — |
| §3 布局按服务对象选 | 人工 | app.routes | 合规 | 平台 DefaultLayout、工作空间 WorkspaceLayout |
| §4（非本地化）界面文案 `englishText` | 闸门 | 非本地化形态 | 证据复用 | 仓库 `check-i18n-repo.py` 英文表判据 |

##### 1.3 frontend-i18n.md

| 规则（§） | 检查方法 | 范围 | 状态 | 证据 |
| --- | --- | --- | --- | --- |
| §1 Transloco、默认 en、scope 清单与文件布局 | 闸门 | public/i18n | 证据复用 | `check-i18n.py`（阶段二随模板分发） |
| §2 业务错误文案只在后端 | 闸门 + grep | src | 证据复用 | 同上（写死中文判据） |
| §3 关键接线（language-service、translation-scopes、loader、accept-language 链首） | 测试 | core/i18n、app.interceptors | 证据复用 | `language-service.spec.ts`、`translation-scopes.spec.ts`、`app.interceptors.spec.ts` |
| §4 词条两语言键集一致、占位符、静态引用、scope 登记 | 闸门 | 全部 | 证据复用 | `check-i18n.py` 与 `--self-test` |
| §4 模板用结构指令，不写返回译文的组件方法 | grep 返回 string 的 `*Label/*Text/*Title()` 方法 | src | 合规 | 0 命中 |
| §4 TS 文案用 `translateSignal`/`translateObjectSignal`，不在 computed 里读 activeLang+translate | grep | src | 合规 | 0 命中 |
| §4 留在界面上的文案存键 | grep `.set(…transloco.translate` | src | 合规 | 0 命中 |
| §4 不直接 `setActiveLang()` | grep | src | 合规 | 仅 `core/services/language-service.ts` |
| §4 数字/货币管道与 LOCALE_ID | grep | src | 不适用 | 模板未使用 number/currency 管道 |

##### 1.4 frontend-spartan.md（登记表 ↔ `libs/ui` 实际差异）

| 登记项 / 实际差异 | 检查方法 | 状态 | 证据（上游 1.5.0 还原 vs 本地） |
| --- | --- | --- | --- |
| `button` default/lg `pointer-coarse:h-11`，icon/icon-lg `pointer-coarse:size-11` | word-diff | 合规（已登记且存在） | `hlm-button.ts` |
| `input`、`input-group` `pointer-coarse:h-11` | word-diff | 合规 | `hlm-input.ts`、`hlm-input-group.ts` |
| `select` trigger `data-[size=default]:pointer-coarse:h-11` | word-diff | 合规 | `hlm-select-trigger.ts` |
| dialog、alert-dialog、sheet、popover、tooltip、select、combobox、navigation-menu 动画 `motion-safe:`；sheet 内容、nav-menu 内容与箭头 `motion-reduce:transition-none` | word-diff | 合规 | 对应 content/overlay/trigger 文件；仅有 `motion-safe:`/`motion-reduce:` 增量 |
| sidebar（`hlm-sidebar`、menu-button、group-label、group-action、menu-action、rail）`motion-reduce:transition-none` | word-diff | 合规 | 6 个文件仅此增量 |
| dropdown-menu trigger 调 CDK `ngOnChanges` | word-diff | 合规 | `hlm-dropdown-menu-trigger.ts`；`dropdown-side-switch.spec.ts` 钉住 |
| **未登记**：dialog-content 关闭按钮读屏名走 `injectHlmA11yLabels()`；sheet-content 关闭按钮同；sidebar-trigger 读屏名同；新增 `utils/src/lib/hlm-a11y-labels.token.ts` 并在 `utils/src/index.ts` 导出 | word-diff | 违规→P3-1 | `hlm-dialog-content.ts`（`closeLabel` 改 `string \| undefined`）、`hlm-sheet-content.ts`、`hlm-sidebar-trigger.ts` |
| **未登记**：button `destructive` 暗色 `dark:bg-destructive/20→/10`、`dark:hover:bg-destructive/30→/20`；badge `destructive` 暗色 `/20→/10`、新增 `success` 变体 | word-diff | 违规→P3-2 | `hlm-button.ts`、`hlm-badge.ts`（`9c7f86ad` 引入） |
| 登记了但实际不存在的定制 | 反向核对 | 合规 | 登记表每一行都能在差异中找到 |
| 升级流程（healthcheck、禁 migrate 覆盖已定制组件） | 人工 | 不适用 | 流程规则，本轮无升级动作 |

##### 1.5 testing.md §3（前端）与 coding-common.md

| 规则 | 检查方法 | 范围 | 状态 | 证据 |
| --- | --- | --- | --- | --- |
| 用例名英文、小写开头、写出行为 | 闸门 + 脚本 | 685 个 describe/it | 证据复用（英文）／合规（小写） | `scripts/check-test-names.py`；本轮脚本：无中文、无大写开头 |
| Vitest + Playwright Chromium 无头、isolate | 人工 | angular.json | 合规 | `runner: vitest`、`browsers: chromiumHeadless`、`isolate: true`、`setupFiles: storage-isolation.testing.ts` |
| 假计时器测防抖/最短时长 | 人工抽查 | users/roles spec | 合规 | — |
| service、pipe、复杂状态、共享组件覆盖输入/输出/空态/错误态 | 人工脚本（无 spec 清单 + 反查） | src | 违规→P3-8、P3-10 | `zoned-time.ts`、`operation-records` 页、`role/tenant-service`；`popover-aria.ts`、`confirm-dialog.ts` 共享件无 spec（列为 P3-10 低） |
| HTTP 用 mock | 人工 | spec | 合规 | `HttpTestingController` |
| 表单覆盖校验、提交、防重复、失败反馈 | 反查表单组件 spec | 表单组件 | 违规→P3-9 | 7 个表单组件无 spec |
| 未配置 E2E 不臆造 | 人工 | package.json | 合规 | 无 `e2e` 脚本，文档未引用 |
| §5 静态闸门带 `--self-test` | 人工 | template/scripts | 证据复用 | `check-i18n.py`、`check-operation-action-i18n.py` |
| common §1 注释中文、用户文案走本地化、不硬编码密钥 | 闸门 + 人工 | src、_mock | 证据复用／合规 | `check-i18n.py` 写死中文判据；`_mock/data/user.ts` 的演示口令仅为 Mock 夹具 |
| common §3.7 外部调用由统一开关决定、前端不传 opt-in | grep `dryRun\|simulate` | src | 合规 | 0 命中 |
| common §3.8 对外契约枚举串小写 | grep DTO 字面量类型 | dtos | 合规 | 未见大写契约值（`setting-section.ts:173` 的 `'Email'` 是设置分组键，不是枚举字段）；后端侧由 P2 |
| common §3.9 导入用短名 | 闸门 | src | 证据复用 | `import-x/order`、`no-duplicates` |
| common §3.1/3.2–3.6 | — | — | 不适用 | 后端职责类，由 P1/P2 |

##### 1.6 P5 增量规则中归 P3 的项

| 增量规则 | 最终位置 | 状态 |
| --- | --- | --- |
| `MOCKED_URL` | coding-frontend §2 | 证据复用 |
| 依赖矩阵与路由懒加载例外 | coding-frontend §2 | 证据复用 |
| `import()` 字面量 | coding-frontend §2 | 证据复用 |
| 单测服务范围（映射/分支/状态必测，原样转发由页面覆盖） | coding-frontend §8 | 违规→P3-10 |
| i18n 闸门随模板分发 | coding-frontend 头部、testing §5 | 证据复用 |
| 构造函数只做赋值与生命周期接线（`1b0cad7c` 引入） | coding-frontend §4 | 违规→P3-7 |

#### A.4 P4 Framework（来源 `p4-findings.md` §1）

| 规则（文件 §） | 方法 | 范围 | 状态 | 证据 |
| --- | --- | --- | --- | --- |
| DG §1 包名、目录、RootNamespace、子命名空间不自重复、不缩写 | 闸门 | 98 csproj | 证据复用 | check-csproj-conventions 通过；IDE0130=error；输入未变 |
| DG §1 注册入口命名，扩展类叫 `DependencyInjection` 并放包根 | 阶段二审查 | 52 个 DI 文件 | 证据复用 | fac8a15e 审过，framework 之后无改动 |
| DG §1 配置节以 `Leistd:` 为根 | grep `SectionName =` | 14 个常量 | 合规 | 全部以 `Leistd:` 开头 |
| DG §1 命名空间不与其中类型同名、不遮蔽 BCL | 阶段二 + 编译 | 全部 | 证据复用 | 输入未变 |
| DG §2 不重复声明共享属性、PackageReference 不写 Version、ProjectReference 用相对路径 | 闸门 + grep | 全部 csproj | 合规 | 三项 grep 均 0 命中 |
| DG §2 ASP.NET Core 能力用 FrameworkReference | 逐项目列出引用 | 63 个 src 项目 | 合规（措辞与 §6.1 冲突，P4-37） | 只直接引用了 DataProtection.Abstractions，这正是 §6.1 要求的写法 |
| DG §2 共享编译选项（CS1591、TreatWarningsAsErrors） | 读 common.props + 构建 | 全部 | 违规（P4-38） | `NoWarn CS0436` 是没有作用的陈旧抑制 |
| DG §3 所有项目都已加入 slnx | 脚本 | 98/98 | 合规 | 每个 csproj 都在 `Leistd.Framework.slnx` |
| DG §4 公共 API 有 XML（CS1591），cref 可解析（CS1574） | 编译器 | 全部 | 证据复用 | Release 下 TreatWarningsAsErrors |
| DG §4 XML 和代码注释用中文 | 脚本 + 人工 | 525 个文件 | 违规（P4-24） | 各 1 处英文 |
| DG §4 异常消息和日志消息用英文 | 脚本：字符串字面量里的汉字 | src | 合规 | 0 命中 |
| DG §4 联系方式不进日志 | 闸门 | src | 证据复用 | check-contact-info-logging 通过 |
| DG §4.1 接口或基类写契约，实现用 `<inheritdoc/>` | 脚本：实现成员与接口成员的 summary 相似度 | src | 违规（P4-25） | 2 组复述 |
| DG §4.1 XML 内不写 Markdown（`**`、反引号、链接、列表） | 闸门 + 脚本 | src | 合规 | 0 命中 |
| DG §4.1 `<para>` 只在两段以上时用 | 脚本 | src | 合规 | 0 命中 |
| DG §4.1 `<example>` 里的 API 真实存在 | 脚本做名称级回查 | 92 个示例 | 合规（签名级未做，靠 P6 的示例编译补） | 名称全部能找到 |
| DG §4.1 实施过程和历史不写进源码 | 脚本 + 人工 | src + tests | 违规（P4-26、P4-27） | "原先、曾经、此前"共 3 处，"N9、上一轮、（P4）"共 10 余处 |
| DG §4.1 行内注释写"为什么"，不复述代码 | 抽查 | 1253 条 `//` | 违规（P4-28） | 约 12 处 |
| DG §4.2 `///` 不挂在非公开成员上，无 `**` | 闸门 | src | 证据复用 | check-doc-comment-shape 通过 |
| DG §4.3 组件文档骨架、索引、无空壳段 | 闸门 | 27 篇 | 证据复用 | check-docs-skeleton、check-docs-sync 通过 |
| DG §4.3 `## 相关` 不放恒定链接，同一事实只写一次 | 人工逐篇 | 25 篇 | 违规（P4-15） | realtime 与 aspnetcore-signalr 写了两份 |
| DG §5 components 不依赖 ddd-struct（src） | 逐项目列出引用 | 62 个 csproj | 合规 | 0 引用 |
| DG §5 ddd-struct 内部层次方向 | 逐项目列出引用 | 4 个项目 | 合规 | Contracts→Data，Application→Contracts(+ObjectMapping.Core)，Infra→Domain |
| DG §5 Core 不依赖 Web/ORM，PackageReference 只用 Microsoft.Extensions.* 或 *.Abstractions | 闸门 + 人工 | *.Core | 合规 | 唯一的 Core→实现包引用是 ServiceClient.Core→Tracing.HttpClient，属同一技术，可接受 |
| DG §5 动态代理例外只限 UnitOfWork.Core | 逐项目列出引用 | *.Core | 合规 | 只有 UnitOfWork.Core 引用 DynamicProxy |
| DG §5 测试的一级划分也守依赖方向 | 读 csproj | tests/components | 违规（P4-9） | MultiTenancy.Tests 引用了 Ddd.* |
| DG §5.1 组件示例不出现 ddd-struct 类型，也不出现 `*AppService` | grep + 人工 | 25 篇 | 违规（P4-6） | 只有 unit-of-work.md:124-130 一处 |
| DG §5.1 示例只用本组件 csproj 闭包里的类型 | 人工对照闭包 | 25 篇 | 违规（P4-14） | 9 处跨组件调用 |
| DG §5.1 / §6.3 示例能编译 | 人工对照签名 | 25 篇 + ddd-struct.md | 违规（P4-5、P4-6、P4-7） | 另核对了约 40 个入口签名，均一致 |
| DG §5.1 安装段写的包真实存在 | 脚本 | 62 个包 | 合规 | 一一对应 |
| 文档与代码一致：默认值、配置键、命名空间、失败形态 | 人工 | 13 个 Options 与"注意事项"段 | 违规（P4-8、P4-17、P4-18、P4-19） | 默认值全部一致；依赖图、命名空间和表格有缺漏 |
| DG §6.1 名称：Async 后缀、I 前缀 | 正则 | src | 合规 | 只有 Hub 的 Subscribe/Unsubscribe 例外，那是协议规定的名字 |
| DG §6.1 入口只有一种形态 `(configure?, configSectionPath)`，先 Bind 再 configure，带 ValidateOnStart | 人工逐个读 | 85 个入口，其中 14 个绑定配置节 | 违规（P4-1、P4-11、P4-12）；规范有缺口（P4-13） | 详见 f3 |
| DG §6.1 不提供 IConfiguration 重载 | grep | DI 入口 | 合规 | 已在 fac8a15e 删除，升级清单 §35 有记录 |
| DG §6.1 只走委托的选项写明不绑定配置的原因 | 人工 | 18 个 Options 类 | 合规（有一处表述矛盾，P4-22） | RevalidationInterval 等已写明原因 |
| DG §6.1 组件发出的错误码自带 en/zh-CN 译文 | 脚本按包对照 | 14 个包 | 违规（P4-3） | OperationRecords 的 `Error:Forbidden` 缺译文 |
| DG §6.1 映射类型为 internal，用 `(int)HttpStatusCode`，不登记多余的 400，Core 的 XML 不写 HTTP 状态 | grep | 6 个映射类 + Core 的 XML | 合规 | — |
| DG §6.1 组件文档写明非默认 HTTP 状态 | 人工对照 MapDefault* | 6 个家族 | 违规（P4-18） | 少列一个 409 |
| DG §6.1 Data Protection 用法 | grep | 2 处 | 合规 | purpose 带版本，只捕获 CryptographicException |
| DG §6.1 不改写官方类型 | 阶段二 | — | 证据复用 | 输入未变 |
| DG §6.2-1 参数守卫用 BCL，不建 Check 类 | grep | src | 合规（2 处可替换，P4-21） | 没有 Check 或 Guard 类 |
| DG §6.2-2 Map/Use 的选项用 `internal Validate()`，不写私有的"为空就抛"辅助方法 | grep | 7 个选项 | 合规 | — |
| DG §6.2-3 IValidateOptions 单独成文件，`Fail(IEnumerable)`，消息以实际配置键开头 | 逐个读 | 19 个验证器 | 合规（缺 1 个验证器，P4-1） | 均接收 configSectionPath |
| DG §6.2 配置缺失不静默兜底，不 clamp | grep Math.Max/Min/Clamp 并人工核对 | src | 违规（P4-1、P4-2） | UoW Timeout 的亚秒值被截断成 0 |
| DG §6.2-4 异常类型按原因选；同一条件只在一处校验 | 人工核对全部 throw | src | 违规（P4-20、P4-23） | — |
| DG §6.2-5 能推断的宿主组合错误在启动期报出 | 阶段二 | 4 个检查器 | 证据复用 | 输入未变 |
| DG §6.3 带 `!` 的提交有脚注并指向升级清单 | git log | 1b0cad7c..HEAD | 合规 | fac8a15e 指向 upgrade-0.13.0.md §35–§38 |
| DG §6.3 删除成员不会静默绑定到基类同名成员 | diff 中的 `-public` 行 | 6 处 | 合规 | 均无基类同名成员 |
| DG §6.3 原子更新消费者 | grep 旧调用形态 | framework/docs | 合规（模板侧转 P2） | template/docs/standards/service-invocation.md:11-12 仍是旧写法 |
| DG §6.3 / 原则 §5.1 不保留兼容层 | grep Obsolete/Legacy/Compat/兼容/过渡 | src | 合规 | 0 命中 |
| DG §6.4 纵向切片：返回 RouteGroupBuilder、策略名必填、不用无参 RequireAuthorization、NamePrefix、不用 WithOpenApi、DTO 手写投影 | grep | 5 个端点类 | 合规 | — |
| DG §6.4 审计类保留期默认关闭、启用时天数必填 | 读选项和验证器 | 2 个保留期选项 | 违规（P4-2） | RetentionDays 默认 365 |
| DG §6.4 周期任务必填 RecurringJobScope | 读源码 | AddRecurringJob | 合规 | 用 `Enum.IsDefined` 强制 |
| DG §6.5 删除能力时给出具体替代入口 | 读升级清单 | §35/§36 | 合规 | — |
| DG §6.5 分发文档里写替代入口 | — | framework/docs | 不适用 | 分发面禁止升级表述（roadmap 规则），替代入口由升级清单承载 |
| DG §6.6 生命周期与注册方式 | 阶段二 | DI 文件 | 证据复用 | 输入未变 |
| DG §6.6 重复调用的契约写进 XML | 脚本 + 人工 | 67 个公开 Add* | 违规（P4-4） | 42 个没写 |
| DG §7.1 测试布局、命名、slnx 登记、WAIVERS | 闸门 | 29 个测试项目 | 证据复用 | check-test-layout、check-test-names 通过 |
| DG §7.2 用例多于约 10 个时分子目录，子目录按被测包命名，替身放 TestDoubles/ | 统计 | 29 个项目 | 违规（P4-29） | — |
| DG §7.2 文件名表达行为 | 与 src 类型名比对 | 230 个文件 | 违规（改规范，P4-37） | 57 个按类型命名 |
| DG §7.3 每个 DependencyInjection.cs 至少三条用例，使用 ServiceCollectionAssertions | 枚举全部入口并对照测试 | 52 个 DI 文件 | 基本合规；缺口见 P4-4、P4-31 | 25 个家族都在用 ServiceCollectionAssertions |
| DG §7.3 分支组合用 Theory | 启发式 + 抽查 | 全部 | 合规 | 2 个候选核对后都是不同行为 |
| DG §7.3 约束和过滤器用关系型 Provider | grep InMemory 用法 | 9 个文件 | 合规 | 索引和过滤器都用 Sqlite |
| DG §7.3 替身优先用官方实现 | grep | 全部 | 合规（有 1 个死替身，P4-30） | 手写的时钟、logger 都有官方替身做不到的行为 |
| DG §7.3 外部服务用 SkippableFact，CI 提供服务并探活 | grep + 读 ci.yml | Lock.Tests | 合规 | ci.yml:343、:369 |
| DG §7.3 被两个以上项目重复发明的替身上移 TestBase | 跨项目哈希比对 | 全部 | 违规（P4-30） | 3 组逐字相同 |
| DG §7.3 测试注释不记录修复轮次 | grep | tests | 违规（P4-27） | — |
| DG §7.4 必测项：注册面、安全、失败路径、第二入口、可翻译性 | 枚举入口 | Add/Map/Use | 违规（P4-31） | UseJsonRequestLocalization 0 用例 |
| DG §7.5 Add* 公共路径不做程序集扫描 | grep | src | 合规 | 0 命中 |
| DG §7.5 真实宿主用 fixture 共享 | 统计 | 25 个 TestServer 文件 | 违规 / 改规范（P4-32） | 只有 1 个文件用了 IClassFixture |
| QA：L0–L2 不靠真实时间等待；判据要能证伪 | grep Sleep/Delay | 全部测试 | 违规（P4-33） | DataFilterTests |
| DG §8 无 TODO/FIXME/HACK | grep + 闸门 | framework | 合规 | 0 命中 |
| DG §9 能用 dotnet CLI 的不包脚本 | 读 docs/CI | docs/framework | 合规 | — |
| DG §9 ps1 首行 shebang | 读首行 | 17 个 ps1 | 违规（P4-34） | 6 个入口脚本缺 shebang |
| DG §9 ps1 不硬编码 `\`，不用 Windows 专属命令 | grep | 17 个 ps1 | 合规 | validate-skills 里的 `python.exe` 只是跨平台候选之一 |
| DG §9 Python 调用入口探测解释器 | 读 | check-all.ps1:38-44、check-i18n-keys.ps1:23-25 | 合规 | — |
| DG §9 Python 内部不写死 `python3`，读写指定 encoding，路径统一成 `/` | grep | scripts/*.py、template/scripts/*.py | 违规（P4-10、P4-35、P4-36） | — |
| DG §9 三个新脚本（check-markdown-anchors.py、check-i18n-repo.py、template/scripts/check-i18n.py） | 读 | 3 个文件 | 前两个合规；check-i18n.py 违规（P4-36） | shebang 和 encoding 都齐 |
| DG §9 文档引用的脚本真实存在 | 抽取路径并检查 | docs/framework、framework/docs | 合规 | — |
| 原则 §1.1 组件文档示例只用真实依赖，DDD 示例只在 ddd-struct.md | 同 §5.1 | — | 违规（P4-6、P4-14） | — |
| 原则 §2.1 单一信息源 | 人工 | framework/docs | 违规（P4-8、P4-15） | 手画的依赖图成了第二份事实源 |
| 原则 §4 交付不留待办 | 闸门 | framework | 证据复用 | check-retired-terms 通过 |
| 原则 §5.3 不过度设计：没有读取方的配置项 | 脚本 | 37 个 Options | 合规 | — |
| 原则 §5.4、§5.6 默认不出错，报错指明缺哪个配置键 | 与 §6.2 一起审 | — | 违规（P4-1、P4-20） | — |
| 原则 §2.2、§3、§1.2、§1.3 | — | — | 不适用 | 不在 framework 代码范围，由 P2、P5、P7 负责 |

#### A.5 P7 项目 Skill 路由抽查（来源 `p7/p7-report.md` §0–§1）

##### 0. 环境

| 项 | 值 |
| --- | --- |
| CLI | codex-cli 0.160.0（`codex exec`，每个场景一个新会话；纠正场景用 `codex exec resume`） |
| 模型 | `gpt-6.1-sol`，reasoning effort `high`（取自会话 rollout 的 turn_context） |
| 沙箱 | 实现、审查、Python：`workspace-write`；部署：`--dangerously-bypass-approvals-and-sandbox`（隔离夹具） |
| Docker | `windows` 上下文探测失败，按规则改用 `orbstack`，仅以 `DOCKER_CONTEXT` 传入 |
| 夹具 | 用私有 hive 安装 `template/`，执行 `dotnet new fullstack-app -n Acme.Orders`（默认参数）。`.tmp/local-feed`（0.12.0，69 个包）拷到 `backend/.local-feed`，并在 `backend/NuGet.Config` 设置独立的 globalPackagesFolder。之后 `git init` 并提交 |

**全局环境因素（可能影响路由）**
- `~/.codex/AGENTS.md` 为空文件（0 字节）。`memories` 功能关闭。`~/.codex/hooks.json` 里是 Orca 钩子，没有 `ORCA_*` 环境变量时什么也不做。
- 会话里可见的 Skill 目录有：系统 Skill（imagegen、openai-docs、skill-creator、skill-installer），`~/.agents/skills`（agent-browser、computer-use、drawio-skill、orca-cli），插件 Skill（browser、chrome、documents、pdf、spreadsheets、presentations 等），以及项目内的 `leistd-project-workflow`。所有场景都只用了项目 Skill，没有调用其他 Skill。
- 生成项目根目录的 `AGENTS.md` 由 Codex 自动注入上下文，它直接指向 SKILL.md 和 `docs/README.md`。所以 .NET 场景的路由同时受 Skill 目录和 AGENTS.md 引导；Python 夹具没有 AGENTS.md，只靠 Skill 目录触发。
- `workspace-write` 沙箱禁止 testhost 建 socket，也连不上 Docker。实现场景因此无法执行 `dotnet test`，agent 在 `/private/tmp` 自写了一个进程内 xUnit runner（这是夹具环境问题，不算 Skill 缺陷；残留文件已由我删除）。

##### 1. 结果总览

| 场景 | 会话 | 结论 |
| --- | --- | --- |
| 实现 | 01a10f4e-9c58-7df0-8dc6-581e5a47df77 | **PASS** |
| 只审查 | 01a10f4e-a221-7512-9e12-13b51570d513 | **FAIL**：路由正确，文件也没变，但没有指出植入的分层违规 |
| 部署 | 01a10f4e-b506-7db0-885a-8a0bc228e762 | **PASS**（有偏差，见下文） |
| 无索引的其他项目 | 01a10f4e-aa90-7bd0-9351-212a2039a62c | **PASS** |
| 重复纠正与精简 | 与实现场景同一会话（resume 两轮） | **FAIL**：沉淀了规则，但预置的重复条目和过时条目都没处理 |

## 附录 B　浏览器验证用例 B-1～B-14（FE5 执行；Resource 浏览器会话相关用例在 `resource` 场景执行，其余在 `identity-all-features`）

### 3.1 环境与前置

- **生成项目**：`pwsh scripts/test-template-matrix.ps1 -Scenarios identity-all-features -GenerateOnly`（外部登录、通知、实时、本地化全开，默认多租户与操作记录）；另生成 `resource`（`pwsh scripts/test-template-matrix.ps1 -Scenarios resource -GenerateOnly`，带浏览器会话）只用于 B-11。前端连真实后端（PostgreSQL）而不是 Mock：模拟登录、两步验证等端点无 Mock（P3-5），按权限裁剪也要真实授权。
- **数据**：以启动初始化的管理员登录，经现有用户、角色与权限管理入口（`POST /api/v1/users` 及角色与权限授予接口或页面）准备 3 个账号：`admin`（超管）、`auditor`（只有 `operationRecords.default`）、`member`（无平台权限）；用户表造 ≥25 行，其中若干邮箱 ≥40 字符、角色 ≥4 个，用来触发 `appTableFit` 降档。
- **浏览器**：agent-browser，有头；`S="$(agent-browser session id --scope worktree --prefix leistd-p3)"`，每条命令带 `--session "$S" --restore`；本地账号密码站点，登录走 `auth login`（保险库）。
- **前置探针（每轮先跑，不过即判整轮无效）**：
  1. `requestAnimationFrame` 在 1s 内回调（显示器休眠会让 view transitions 挂起），长任务前用限时 `caffeinate -d -t <秒>`；
  2. 主题切换后**重载**再断言（agent-browser 模拟切暗色要重载）；
  3. 粗指针：仿真后先断言 `matchMedia('(pointer: coarse)').matches === true`，否则本用例记"环境不满足"，不得判通过（`set device` 不会给 coarse 指针，需经 CDP `Emulation.setTouchEmulationEnabled({enabled:true,maxTouchPoints:5})` 或 Playwright `hasTouch` 上下文）；
  4. 减少动效：仿真后断言 `matchMedia('(prefers-reduced-motion: reduce)').matches === true`；
  5. `h-svh` 布局下不用 `--full` 截图，按视口分段截。
- **视口**：桌面 1440×900、平板 834×1112（`md`，平板档）、手机 390×844（`<sm`）。
- **证据**：截图存 scratchpad，命名 `p3-<用例>-<视口>-<主题>.png`；断言结果 JSON 一并保存。

### 3.2 用例

| ID | 页面/组件 | 条件 | 操作 | 断言（全部须可证伪） |
| --- | --- | --- | --- | --- |
| B-1 | `/platform/users`（DefaultLayout、user-table、table-paginator、faceted-filter） | 3 视口 × 亮/暗 | 打开页面 | 表外框 `scrollWidth <= clientWidth`（无横向滚动）；可见列集合：1440 = 全部，834 = primary+secondary，390 = primary（username、status）+ 操作列；列头 `th` 数量与此一致；`document.documentElement.scrollWidth <= innerWidth` |
| B-2 | 同上，行展开 | 390、834 | 点第一行展开按钮；切下一页再回来 | 展开区出现被裁列（邮箱、角色、最后登录、创建时间）且值与 API 一致；展开状态按实体 id 保留（翻页回来仍展开第 1 行，且不是"第 1 个位置"）；`aria-expanded` 随之切换 |
| B-3 | 同上，操作列 | 1440 vs 390；users（7 项）、roles（3 项）、tenants（5 项）、open-apps（3 项） | 读行内按钮；打开 `…` | 1440：users/tenants 恰 2 个图标按钮 + `…`，roles/open-apps 3 个平铺且无 `…`（`hasRowActions` 为假时）；每个图标按钮 hover/focus 出现 tooltip 且有可访问名；390：行内只剩 `…`；菜单中破坏性项 `data-variant="destructive"` 且紧前一个兄弟是 separator，并处于末尾 |
| B-4 | 侧栏/顶栏/卡片/弹层（DefaultLayout 首页、通知 popover、用户编辑 dialog） | 暗色 | 读计算样式 | `sidebar`、`background`、`card`、`popover`、`muted` 五层背景按 oklch L 严格递增；没有任何可见元素背景为 `rgb(0,0,0)`；`html.dark` 存在；模拟登录徽标（以租户登录后）文字对比度 ≥ 4.5 |
| B-5 | dialog（用户编辑）、alert-dialog（删除确认）、sheet（手机侧栏抽屉、设置导航抽屉）、popover（通知）、tooltip、select、combobox、navigation-menu、侧栏折叠 | `prefers-reduced-motion: reduce` vs 默认 | 打开/关闭各浮层；`Ctrl/⌘+B` 折叠侧栏 | 仿真下打开后内容元素 `getComputedStyle().animationName === 'none'`，sheet 内容与侧栏 `transitionDuration` 为 `0s`；关闭后 300ms 内 DOM 移除（Brain 无动画时立即关闭）；默认动效下 `animationName` 不为 `none`（反例，证明判据有效）；焦点环仍可见（`outline`/`box-shadow` 非 none） |
| B-6 | 登录页、用户编辑对话框、列表工具栏、select、input-group | 粗指针 vs 细指针，390 与 1440 | 读尺寸 | 粗指针：`hlmBtn` default/lg 高 44px，icon/icon-lg 44×44，`xs`/`sm` 尺寸不变（反例）；`hlmInput`、`hlm-input-group`、`hlm-select-trigger[data-size=default]` 高 44；细指针下回到 32/36（证明判据可证伪）；输入框字号 `<768px` 为 16px、`≥768px` 为 14px |
| B-7 | 侧栏菜单、头像菜单、区域切换器 | `admin`、`auditor`、`member` 分别登录 | 展开侧栏与头像菜单 | `auditor`：侧栏只有「工作」「审计」两组，无空组标题；可进入 `/platform/operation-records`；直接访问 `/platform/users` 落到 `/403-forbidden`；`member`：无「管理平台」区域项，访问 `/platform` 落 403，品牌块为普通链接（无下拉）；`admin`：组序 工作→身份与访问→开发者→审计→系统，菜单标题与权限显示名逐字一致；头像菜单无「切换租户」 |
| B-8 | 登录表单、用户菜单、语言切换、用户编辑对话框、faceted-filter、表头排序 | 仅键盘 | Tab 序遍历登录页；Enter/Space 开菜单、方向键移动、Esc 关闭；对话框内 Tab 循环、Esc 关闭；筛选器方向键+Enter 勾选；表头 Enter 排序 | 登录页 Tab 序：租户（如有）→用户名→密码→显示密码→提交→外部登录；菜单 Esc 后焦点回到触发按钮；对话框焦点被困在内部、关闭后回到触发按钮；排序后 URL `sort`/`direction` 更新且表头 `aria-sort` 同步；所有可达元素有可见焦点 |
| B-9 | 用户列表、通知面板、登录设备 | 断网/接口 500（DevTools 拦截） | 加载、翻页、重试 | 首次失败显示错误态 + 重试按钮；已有数据时刷新失败保留旧行并 toast；快速翻页两次，加载态跟随最后一次请求；空结果与"加载失败"文案不同 |
| B-10 | 登录、用户列表、角色列表、设置（个人/系统）、仪表盘、落地页 | 390 与 1440，亮色，中英文各一次 | 脚本遍历所有可见文本元素（排除 `ng-icon`、`hlm-spinner`、`svg` 内） | 字号 ∈ {12,14,16,24}px，仅仪表盘数字允许 30px；字重 ∈ {400,500,600}；每页 `text-2xl` 标题至多 1 个；卡片/对话框/表格外框 `border-radius` 等于 `rounded-xl` 计算值，按钮/输入为 `rounded-lg`，徽章/头像为 9999px；页面主容器 padding 390 下 16px、≥640 下 24px；当前项（侧栏、设置导航、顶栏）背景 = primary 10% 混合、文字 = primary、字重 600。P3-11~P3-16 修复前此用例应失败（作为证伪），修复后通过 |
| B-11 | Resource 形态 `/auth/login`（resource-login） | 390、1440 | 打开 | 使用 `hlmBtn`，标题 24px，外框 `p-4 sm:p-6`（P3-15 修复后） |
| B-12 | 页面标题与语言 | 中英切换 | 进入用户列表；切换语言；进入登录页 | `document.title === "<页面标题> · <应用名>"`、面包屑末级同名；切换语言后两者同时变化；登录页标题只有应用名；对话框关闭按钮与侧栏开关的读屏名随语言变化（P3-1 定制的运行时证据） |
| B-13 | 设置页面板 | 刷新、头像菜单直达 | 进入 `/workspace/settings/preferences`，刷新 | 刷新后仍在同一面板；手机宽度下面板导航在 sheet 内且当前项高亮；从头像菜单「个人设置」进入的是同一路由 |
| B-14 | 空态 | 筛选出 0 行 | 输入不存在的关键字 | 空态图标与文字同列居中（图标中心 x 与文字中心 x 差 ≤1px），图标在文字上方（`icon.bottom <= text.top`） |

收尾：`agent-browser --session "$S" close`；清理本轮创建的容器（`docker rm -fv`）与生成目录。

---
