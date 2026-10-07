# 阶段六：升级说明分发、生成项目 CI、按需测试、复核遗留与注释精简

## 1. 目标

本计划合并了注释精简方案（`2026-10-07-concise-comments.md`，用户裁定合并后由本会话执行），并纳入 Codex 定向复核的三项遗留（D、E、F）。

| # | 缺口（阶段五回顾） | 决策 | 行业依据 |
| --- | --- | --- | --- |
| A | 下游项目升级 Leistd 版本没有闭环：升级说明只在仓库 `docs/framework/upgrade-0.13.0.md`（约 130K，框架与模板混写），不随包分发；消费方 Skill 与项目 Skill 都没有“升级版本”场景 | 框架升级说明按家族拆分、随该家族的包分发，累计保留各版本；模板侧变更留仓库，由 Release 链接；消费方 Skill 增加升级工作流 | ABP 每版一份迁移指南加 `abp update`；.NET 按版本与技术领域分页，标注二进制、源码、行为三类不兼容 |
| B | 生成项目没有 CI，testing §1.1 因此要求每次阶段完成都本地跑受影响测试项目全量（含全部集成测试） | 生成项目增加平台无关的验证入口 `scripts/verify`；GitHub Actions 与 GitLab CI 只做薄壳调用它；模板参数 `ci`：`github`（默认）、`gitlab`、`none` | GitHub “Scripts to Rule Them All”（CI 只调 `script/cibuild`）；主流 .NET 模板自带 GitHub Actions |
| C | 框架测试全有或全无：本地 L1 与 CI 都在任何框架改动后跑 28 个测试项目 | 按 csproj 依赖图选择“受影响家族 + 反向依赖家族”的测试项目；全集留给 L3 与未知输入 | 与模板场景选择同一机制（已有 `plan-quality-checks.py`） |

| D | HostSettings 相同绑定重复登记在解析时抛 “already bound”，与“相同登记重复调用不重复生效”的规则矛盾；测试只比描述符，未解析绑定 | 相同绑定幂等，同名不同配置在登记时（而非首次解析时）以明确异常失败 | 开发指南 §6.6；ASP.NET Core `TryAdd*` 惯例 |
| E | 规范规定领域服务单聚合、`UserRole` 有独立仓储即按聚合根对待，但 `UserDomainService` 同时消费 User 与 UserRole 仓储，并接收 `Role` 实体直接增删 UserRole | 把 `UserRole` 归入 `User` 聚合：作为按 `RoleId` 引用角色的子实体，经 `User` 的方法分配与替换，持久化随 `User`；去掉 `IRepository<UserRole>` | ABP `IdentityUser.Roles`（子实体集合，按 RoleId 引用）；规范“子实体只经聚合根修改、聚合间按 Id 引用” |
| F | 读取成本脚本漏计间接必读文件：fullstack-crud、ui-change 只登记 Spartan `SKILL.md`，没有登记它按任务要求必读的 rules | 补登记任务对应的最小必读规则集，同口径重算基线与上限；在脚本里声明“文件 → 必读伴随文件”并自检集合闭合 | 统计口径应与真实读取链一致 |
| G | 注释与文档表达不统一（合并自 `2026-10-07-concise-comments.md`，原计划第 4 条“删除升级说明”经用户裁定不执行，由 A 取代） | 名称与类型优先，注释只补正确使用所需的契约；文档只描述当前产品 | .NET 命名指南、C# 编码约定、XML 推荐标签、Angular 风格指南、TypeScript JSDoc |

不在范围内：Package Validation（`versioning.md` 已裁定暂不引入）、NUKE 等构建框架（对当前规模属过度设计）。

## 2. A：升级说明

### 2.1 位置与打包

- **框架侧**：`framework/docs/upgrades/<版本>/<家族>.md`。家族划分与 `framework/docs/components/<家族>.md` 一致，DDD 基座为 `ddd-struct`。
- **打包**：`framework/common.props` 把本家族所有版本的升级说明打进包内 `docs/upgrades/<版本>.md`，与现有家族文档的打包规则同处维护。消费方只要读已安装或目标版本的包，就能拿到该家族全部历史版本的说明。
- **跨家族条目**（如命名空间搬迁、Core 契约）：放在受影响的每个家族，或放 Core 家族并由其他家族引用；以消费方不需要读无关家族为准。
- **模板侧**：模板 HTTP 契约、生成项目文件等变更移到 `docs/template/upgrade-<版本>.md`，仓库内维护，不进 NuGet。
- **0.13.0 现有内容**：
  - 按家族拆到新位置，模板条目移到模板文件，迁移要求一条不丢。
  - 原 `docs/framework/upgrade-0.13.0.md` 删除；`upgrade-0.13.0-api-diff.md` 的逐成员清单按家族并入对应文件的附录，或保留在仓库并由各家族文件链接，以篇幅和使用频率定。
  - 提交脚注里的“§n”引用指向已删除文件，属于历史提交，不改写；`versioning.md` 写明自 0.14 起脚注指向新位置。

### 2.2 格式

每个家族文件按条目写四项：受影响的是什么、原来怎么写、现在怎么写、必须执行的迁移动作。每条标注不兼容类型：二进制、源码或行为。“只有调用方必须改代码才写”的规则不变。

### 2.3 规则与链接

- `docs/framework/versioning.md`：改写“破坏性变更怎么让下游知道”与升级清单一节，写明新位置、格式、模板侧位置；文末列表改为链接两处目录。
- 包元数据：`PackageReleaseNotes` 链接到该版本 GitHub Release；Release 正文由 `release.yml` 追加两处升级说明的链接，不复制内容。
- 闸门：
  - `check-retired-terms.ps1` 现按前缀豁免升级文档，改为豁免新目录（路线图短语规则对升级说明不适用）。
  - 打包内容检查：`test-package-consumption.ps1` 或包内容检查断言有升级说明的家族包内含 `docs/upgrades/`。
  - G5 的扫描范围覆盖新目录。
- 框架开发指南与框架 Skill：破坏性变更的提交须同时写对应家族的升级说明，与提交脚注一致。

### 2.4 消费方 Skill（`skills/leistd-net-framework`）

新增“升级版本”工作流：

1. 确认当前与目标版本，遵循项目的 CPM 或统一版本属性，统一升级全部 `Leistd.*` 包并还原。
2. 枚举已安装目标版本包内的 `docs/upgrades/*.md`，按版本顺序读取当前版本之后的条目，只读项目实际引用的家族。
3. 逐条迁移；有数据库模型变化时按项目规范生成并检查迁移。
4. 按受影响组件构建、测试并启动真实宿主验证主路径。
5. 报告已迁移条目与未覆盖风险。

项目 Skill（`leistd-project-workflow`）的意图表不增加 Leistd 专属内容（保持技术栈无关）；升级框架依赖属于“实现”意图，由消费方 Skill 提供细节。

## 3. B：生成项目 CI

### 3.1 验证入口

- 新增 `scripts/verify.ps1`（随模板分发，带 shebang）。它是生成项目“完整回归”的唯一实现，内容与 `testing.md` §1.1 一致：
  - 后端：还原、构建（警告即错误）、单元测试、集成测试（Testcontainers，需要 Docker）。
  - 前端（`SpaFrontend`）：`npm ci`、lint、测试、构建。
  - 随包静态检查：`check-error-codes.py`，以及按条件存在的 `check-i18n.py`、`check-operation-action-i18n.py`。
- 支持 `-List` 输出步骤清单，用于矩阵核对；失败即停并返回非零。

### 3.2 平台薄壳

- 模板参数 `ci`（choice）：`github`（默认）生成 `.github/workflows/ci.yml`；`gitlab` 生成 `.gitlab-ci.yml`；`none` 两者都不生成。
- 薄壳只负责准备 .NET、Node、Python、Docker 并调用 `scripts/verify.ps1`，不写测试逻辑。GitLab 薄壳使用 Docker-in-Docker 服务供 Testcontainers 使用。

### 3.3 文档

- `testing.md` §1.1：有 `ci` 薄壳时，本地阶段完成跑目标测试与受影响范围，完整回归由 CI 的 `verify` 承担；`ci=none` 时保留现行规则。说明用条件块按 `ci` 取值生成。
- 根 README 与 `docs/README.md` 登记 `verify` 入口；项目 Skill 不改（入口由项目文档索引提供）。

### 3.4 验证

- 矩阵：一个 PR 档场景（identity-all-features）在生成产物上实际运行 `scripts/verify.ps1`，并核对其 `-List` 与矩阵阶段一致。评估矩阵直接复用 `verify` 作为生成项目的测试阶段，以去掉重复实现；不可行时说明理由。
- GitHub 薄壳用 actionlint；GitLab 薄壳做 YAML 结构与必需键检查。
- `test-template-generation.py` 覆盖三种 `ci` 取值的文件集合。

## 4. C：框架测试按需选择

- `scripts/plan-quality-checks.py`：
  - 由 `framework/**/*.csproj` 的 `ProjectReference` 构建依赖图。
  - 改动文件映射到所属项目，求其反向依赖闭包，再映射到测试项目，输出 `FrameworkTestProjects`。
  - 共享输入（`framework/*.props`、`Directory.Packages.props`、`framework/build/**`、`tests/shared/**`）或无法映射时，退回全集。
- CI 的 `test` 作业按清单运行；`template-matrix` 聚合校验清单与执行一致。
- 本地 L1：`docs/framework/quality-assurance.md` 与框架 Skill 改为“按清单运行受影响测试项目”，全集留给 L3。
- 自检：单家族改动、被多家族依赖的 Core 改动、共享 props 改动、测试项目自身改动、无法映射的文件。

## 5. D：HostSettings 登记语义

- 语义：
  - 绑定的身份为（设置名、配置键、目标 Options 类型与成员）。身份相同的重复登记不产生任何效果。
  - 不同设置的绑定累加。
  - 同一设置名绑定到不同目标的，在 `AddHostSettings` 调用时抛出，异常信息给出两处冲突，不推迟到首次解析。
- 实现须保证 Options 的 Configure 回调与注册校验一致：重复调用不追加会再次 Bind 的回调。
- 测试用真实 ServiceProvider 解析绑定 Options 并启动宿主，覆盖三种情形：相同登记、不同设置累加、同名不同目标冲突。共享注册断言不得掩盖消费者错误。
- 同步组件文档与 XML 注释。按复核反馈附带的最小复现（`.tmp/remaining-review/repro`）先确认失败，再修复。

## 6. E：User 聚合与角色

- `UserRole` 成为 `User` 的子实体集合，按 `RoleId` 引用 `Role`，不持有 `Role` 实体。
- 分配与替换经 `User` 的方法完成（如 `AssignRoles(IEnumerable<Guid>)`、`ReplaceRoles(...)`）；领域规则（超级管理员保护等）在实体或 `UserDomainService` 内，只操作 `User` 聚合。
- 角色存在性与默认角色解析在应用服务中完成，经 `IRoleRepository`，跨聚合协调。
- 持久化：
  - EF 映射为 `User` 拥有的集合，表结构不变；若模型快照有差异，按“改框架配置会让模板迁移快照过期”的经验核对基线迁移。
  - 去掉 `IRepository<UserRole>` 的全部 7 处使用：按角色统计、删除角色时的清理、种子与资源管理员引导，改走聚合或 `IUserRepository` 的自定义查询。
- 保持既有行为：
  - 角色整体替换在同一工作单元内完成；
  - 写方法不回查未落库关联；
  - 软删除与租户过滤语义不变。
- 规范：`coding-backend.md` 的聚合条目把 `UserRole` 示例改为“子实体”，并写明“有独立仓储即聚合根”的判据；`coding-common.md:32` 与之一致。
- 验证：
  - 受影响的单元与集成测试；
  - identity、identity-all-features、resource 三个矩阵场景（Resource 形态的角色授予同样经过该路径）；
  - PostgreSQL 端到端。

## 7. F：读取成本口径

- 任务集合补入间接必读文件：
  - fullstack-crud：Spartan `rules/styling.md`、`rules/composition.md`；
  - ui-change：再加 `rules/forms.md`。
  - 最小集合按 Spartan `SKILL.md` 的必读说明逐项核对。
- 基线 `e984db19` 按同一集合重算（这些文件在基线已存在），上限按新读数留约 1%。
- 脚本新增 `REQUIRED_COMPANIONS`（文件 → 按任务必读的伴随文件），自检：登记集合含某文件时，其伴随文件必须同在，否则失败；提交说明写明口径变化。
- 不改为实际 token 统计：字符数与 token 的比例在同一语言内稳定，比较的是相对变化；引入分词器的成本与收益不匹配。

## 8. G：注释与文档精简（合并方案）

原则表：

| 对象 | 保留 | 精简 |
| --- | --- | --- |
| 框架公共类型与成员 | summary 或显式 inheritdoc；单位、默认值、空值、失败形态、取消、顺序、作用域等非显然契约 | 名称翻译式套话、构造参数逐字复述、与组件文档重复的实现说明 |
| 内部成员与行注释 | 算法原因、并发与安全不变量、外部约束 | 步骤编号、代码复述、装饰分隔线、历史修复经过 |
| 前端 JSDoc、HTML、CSS 注释 | 服务边界、状态所有权、协议、订阅清理、竞态原因 | 逐成员注释、重复 TypeScript 类型、明显的区块标签 |
| 组件文档 | 安装与注册入口、最小示例、关键 API、配置、运行限制 | 过程记录、实现教学、夸张警示、与其他文档重复的事实 |

范围与约束：

- 精简以语义为单位，不按行数截断或批量删 remarks。
- 默认值、null/false、异常、租约、幂等、事务、租户、部署前提逐项对照源码。
- inheritdoc 只用于确有继承且语义一致的成员。
- 框架开发规范 §4 写统一规则与短示例；生成项目 `coding-common.md` 写同一原则；`coding-frontend.md` 只补 JSDoc 特有规则；Skill 只链接规范，不复述。
- 逐家族审视 `framework/components`、`framework/ddd-struct` 源码注释与组件文档、DDD 文档。
- 审视 `template/frontend` 自有源码、Mock、测试与 README，不改 `libs/ui`、版权声明与工具指令（模板条件、eslint、coverage、纯注解）。
- 只改注释、文档及必要引用；不改运行代码、不重命名 API。发现的行为问题单列。
- 验证：
  - 用 Roslyn 与 TypeScript 扫描器核对源码 token 不变（工具指令单独比对）；
  - 框架 Release 构建（XML、cref）与打包内容；
  - 生成场景的格式与 lint；
  - G5、锚点、读取成本、Skill 校验。
- 与 A 的关系：A 先完成升级说明的拆分与迁移，G 不删除升级说明，只按同一原则精简其表达。

## 9. 工作包

| 包 | 文件 | 依赖 |
| --- | --- | --- |
| U1 | `framework/docs/upgrades/**`、`docs/template/upgrade-0.13.0.md`、删除 `docs/framework/upgrade-0.13.0*.md`、`docs/framework/versioning.md`、`framework/common.props`、`release.yml` 的 Release 正文与包元数据、`check-retired-terms.ps1`、包内容检查 | 无 |
| U2 | `skills/leistd-net-framework/**`、`.agents/skills/developing-leistd-framework/SKILL.md`、`docs/framework/development-guide.md` 中的升级说明规则 | U1 |
| V1 | `template/scripts/verify.ps1`、CI 薄壳、`template.json` 参数与排除、`testing.md`、根 README、`docs/README.md`、矩阵与生成测试接线 | 无 |
| S1 | `scripts/plan-quality-checks.py`、`.github/workflows/ci.yml` 的 test 作业与聚合、`test-quality-validation-plan.py`、`test-workflow-change-scope.py`、`docs/framework/quality-assurance.md` 的框架部分 | 无 |
| D1 | `framework/components/settings/**`（Hosting 的注册、绑定构建器、测试、组件文档） | 无 |
| E1 | 模板 Users 模块的 Domain、Application、Infrastructure、DbMigrator 及相关测试；`coding-backend.md`、`coding-common.md` 的聚合条目 | 无 |
| F1 | `scripts/measure-template-read-cost.py` | 无 |
| G1 | `framework/components/**`、`framework/ddd-struct/**` 的注释与 `framework/docs/**`（不含 A 的 `upgrades/`）；`docs/framework/development-guide.md` §4 | U1、D1 |
| G2 | `template/frontend/**` 自有源码注释与 README；`template/docs/standards/coding-common.md`、`coding-frontend.md` 的注释规则 | E1、V1 |
| Z | Codex 终审、全量验证、删除本计划、推送、更新 PR | 全部 |

约束：

- 每个包先核实前提。
- 新增类型、脚本与文件须报告依据，对照 A15。
- 模板条件块整行独占。
- 文档按阶段五的单源与精简原则，不新增重复。

验证：`check-all`、各脚本自检、读取成本、打包与隔离消费、矩阵 `-Tier pr`；S1 改动聚合逻辑，需运行 `test-workflow-change-scope.py`、`test-quality-validation-plan.py`，并以远端 CI 验收。
