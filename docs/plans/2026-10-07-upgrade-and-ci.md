# 阶段六：升级说明分发、生成项目 CI、按需测试、复核遗留与注释精简

## 1. 目标

本计划合并了注释精简方案（`2026-10-07-concise-comments.md`，用户裁定合并后由本会话执行），并纳入 Codex 定向复核的三项遗留（D、E、F）。

| # | 缺口（阶段五回顾） | 决策 | 行业依据 |
| --- | --- | --- | --- |
| A | 下游项目升级 Leistd 版本没有闭环：升级说明 `docs/framework/upgrade-0.13.0.md`（约 130K，框架与模板混写）无版本化位置与发布链接；消费方 Skill 没有“升级版本”场景 | 每版一份升级指南留仓库，Release Notes 与包元数据 `PackageReleaseNotes` 链接到它；消费方 Skill 增加升级工作流（用户改定：不随包分发） | 微软 NuGet 包作者最佳实践（README + PackageReleaseNotes）；.NET 破坏性变更按版本在文档站发布；ABP 每版迁移指南 |
| B | 生成项目没有 CI，testing §1.1 因此要求每次阶段完成都本地跑受影响测试项目全量（含全部集成测试） | 生成项目增加平台无关的验证入口 `scripts/verify`；GitHub Actions 与 GitLab CI 只做薄壳调用它；模板参数 `ci`：`github`（默认）、`gitlab`、`none` | GitHub “Scripts to Rule Them All”（CI 只调 `script/cibuild`）；主流 .NET 模板自带 GitHub Actions |
| C | 框架测试全有或全无：本地 L1 与 CI 都在任何框架改动后跑 28 个测试项目 | 按 csproj 依赖图选择“受影响家族 + 反向依赖家族”的测试项目；全集留给 L3 与未知输入 | 与模板场景选择同一机制（已有 `plan-quality-checks.py`） |

| D | HostSettings 相同绑定重复登记在解析时抛 “already bound”，与“相同登记重复调用不重复生效”的规则矛盾；测试只比描述符，未解析绑定 | 相同绑定幂等，同名不同配置在登记时（而非首次解析时）以明确异常失败 | 开发指南 §6.6；ASP.NET Core `TryAdd*` 惯例 |
| E | 规范规定领域服务单聚合、`UserRole` 有独立仓储即按聚合根对待，但 `UserDomainService` 同时消费 User 与 UserRole 仓储，并接收 `Role` 实体直接增删 UserRole | 把 `UserRole` 归入 `User` 聚合：作为按 `RoleId` 引用角色的子实体，经 `User` 的方法分配与替换，持久化随 `User`；去掉 `IRepository<UserRole>` | ABP `IdentityUser.Roles`（子实体集合，按 RoleId 引用）；规范“子实体只经聚合根修改、聚合间按 Id 引用” |
| F | 读取成本脚本漏计间接必读文件：fullstack-crud、ui-change 只登记 Spartan `SKILL.md`，没有登记它按任务要求必读的 rules | 补登记任务对应的最小必读规则集，同口径重算基线与上限；在脚本里声明“文件 → 必读伴随文件”并自检集合闭合 | 统计口径应与真实读取链一致 |
| G | 注释与文档表达不统一（合并自 `2026-10-07-concise-comments.md`，原计划第 4 条“删除升级说明”经用户裁定不执行，由 A 取代） | 名称与类型优先，注释只补正确使用所需的契约；文档只描述当前产品 | .NET 命名指南、C# 编码约定、XML 推荐标签、Angular 风格指南、TypeScript JSDoc |

不在范围内：Package Validation（`versioning.md` 已裁定暂不引入）、NUKE 等构建框架（对当前规模属过度设计）。

## 2. A：升级说明

用户在复审后改定方案：不随 NuGet 包分发升级说明，避免包体积随历史版本增长。改用微软与生态的通行做法：包内只放 README 与 `PackageReleaseNotes` 链接；破坏性变更与迁移指南按版本发布在文档位置（.NET 的 learn.microsoft.com compatibility 页面、ABP 的每版迁移指南）。

### 2.1 位置

- 每个版本一份升级指南：`docs/framework/upgrades/<版本>.md`，仓库内维护，不进 NuGet 包。
- 文件内分“框架”和“模板（生成项目）”两部分；框架部分按家族分小节，便于消费方只读自己引用的家族。
- 0.13.0 现有内容：
  - `docs/framework/upgrade-0.13.0.md` 迁到 `docs/framework/upgrades/0.13.0.md`，按上述结构重排；
  - `upgrade-0.13.0-api-diff.md` 作为同目录附录 `0.13.0-api-diff.md`，由正文链接；
  - 逐项核对旧 44 节（含合入 develop 新增的 §44）与附录全部 212 条成员的去向，迁移动作原样保留，去向清单在评审时给出；
  - 仓库内对旧文件的引用全部更新（历史提交脚注不改写）。
- 包内家族文档（`framework/docs/components/*.md`）描述当前版本契约，不变。

### 2.2 格式

每条写四项：受影响的是什么、原来怎么写、现在怎么写、必须执行的迁移动作。每条标注不兼容类型：二进制、源码或行为。“只有调用方必须改代码才写”的规则不变。

### 2.3 链接与规则

- **版本映射**：预发布版本按去掉预发布后缀的基础版本对应指南，`0.13.0-beta.N`、`0.13.0-preview.<日期>.<序号>` 都对应 `upgrades/0.13.0.md`。
- **包元数据**：`PackageReleaseNotes` 由 `release.yml` 在打包时按渠道设置，消费方离线也能从已安装包的 `.nuspec` 读到：
  - 基础版本有升级指南时，所有渠道（stable、beta、nightly）一律指向该指南在本次发版 tag 下的固定 URL；
  - 没有指南时：stable 与 beta 指向本次 GitHub Release，nightly 只推送 tag、不建 Release，指向该 tag 的提交页。
  - 不用分支链接。
- **Release 正文**：`release.yml` 生成的 Release Notes 追加一行“升级指南”链接，指向 `docs/framework/upgrades/<版本>.md` 在该 tag 下的地址；该版本没有升级指南时不加。
- **`docs/framework/versioning.md`**：改写“破坏性变更怎么让下游知道”与升级清单一节，写明位置、格式与链接方式；脚注从本次切换起指向 `upgrades/<版本>.md` 的小节；文末列表链接 `upgrades/` 目录。
- **闸门**：
  - `check-retired-terms.ps1` 的前缀豁免改到新目录；
  - G5 白名单中指向 `upgrade-0.13.0.md` 的条目迁到新文件；
  - 链接政策必须有验证，不取决于是否抽成脚本：复用现有发布验证入口，覆盖 stable、beta、nightly 三个渠道，各验证有指南、无指南两种分支；并检查实际打出的 `.nuspec` 中的 URL 固定到本次 tag。
- **分发边界不变**：`docs/README.md` 仍禁止分发载荷写迁移步骤，升级指南留在仓库。

### 2.4 消费方 Skill（`skills/leistd-net-framework`）

新增“升级版本”工作流：

1. 确认当前版本与目标版本。
2. 取得目标版本的升级指南，二选一：
   - 读目标版本包 `.nuspec` 的 `PackageReleaseNotes`；
   - 执行 `gh release view "v<版本>" --repo zengqinglei/leistd-net --json body,url`，必须带 `--repo`，否则会查到下游仓库。
   链接指向 Release 时，继续读取正文中的升级指南链接。预发布版本按基础版本对应指南。按版本顺序读取当前版本之后各版的指南，框架部分只读项目引用的家族。
3. 先处理包级前置动作：包改名、移除、新增必需包。
4. 再按项目的 CPM 或统一版本属性修改版本与包引用，然后还原。
5. 逐条迁移源码与配置；有数据库模型变化时按项目规范生成并检查迁移。
6. 按受影响组件构建、测试，并启动真实宿主验证主路径。
7. 报告已迁移条目与未覆盖风险。取不到升级指南（无网络、无权限）时明确报告，不凭记忆迁移。

项目 Skill（`leistd-project-workflow`）保持技术栈无关，不增加 Leistd 专属内容。

## 3. B：生成项目 CI

### 3.1 验证入口

- 新增 `scripts/verify.ps1`（随模板分发，带 shebang）。它是生成项目“完整回归”的唯一实现，内容与 `testing.md` §1.1 一致：
  - 后端：还原、构建（警告即错误）、单元测试、集成测试（Testcontainers，需要 Docker）。
  - 前端（`SpaFrontend`）：`npm ci`、lint、测试、构建。
  - 随包静态检查：`check-error-codes.py`，以及按条件存在的 `check-i18n.py`、`check-operation-action-i18n.py`。
- 支持 `-List` 输出步骤清单，用于矩阵核对；失败即停并返回非零。

### 3.2 平台薄壳

- 模板参数 `ci`（choice）：`github`（默认）生成 `.github/workflows/ci.yml`；`gitlab` 生成 `.gitlab-ci.yml`；`none` 两者都不生成。
- 薄壳只负责准备环境并调用 `scripts/verify.ps1`，不写测试逻辑。准备清单：
  - .NET、Node、Python、PowerShell；
  - 与锁文件一致版本的 Playwright Chromium 及其系统依赖；
  - Docker：GitHub 托管 Runner 自带；GitLab 写明 Runner 须允许 Docker-in-Docker（privileged），并按 Testcontainers 官方的 CI 配置设置 `DOCKER_HOST` 与 TLS 变量。

### 3.3 文档

- `testing.md` §1.1 保留现行前提：只有流水线可运行、覆盖完整范围，并设为合并必过后，完整回归才移交 CI；在此之前仍由本地承担。
  - `ci=github/gitlab` 时：说明生成的流水线入口，并写明启用为必过的步骤（仓库保护规则或合并检查），作为项目确认“必过 CI 已就位”的依据。
  - `ci=none` 时：只保留本地全量规则。
  - 这样生成 CI 文件本身不会自动降低本地责任。
- 根 README 与 `docs/README.md` 登记 `verify` 入口；项目 Skill 不改（入口由项目文档索引提供）。

### 3.4 验证

- 矩阵在三个场景实际运行 `scripts/verify.ps1`：identity-all-features、resource-host-api-realtime（无前端）、identity-capabilities-08（关闭本地化）。
- 注入一个失败步骤，确认 `verify` 非零退出，且后续步骤不执行。
- 评估矩阵直接复用 `verify` 作为生成项目的测试阶段，以去掉重复实现；复用时保留运行时冒烟、测试发现范围与产物断言，不可行时说明理由。
- GitHub 薄壳用 actionlint；GitLab 薄壳做 YAML 结构与必需键检查。
- `test-template-generation.py` 覆盖三种 `ci` 取值的文件集合。

## 4. C：框架测试按需选择

- `scripts/plan-quality-checks.py`：
  - 由 `framework/**/*.csproj` 的 `ProjectReference` 构建依赖图。
  - 改动文件映射到所属项目，求其反向依赖闭包，再映射到测试项目，输出 `FrameworkTestProjects`。
  - 以下情形退回全集：
    - 共享输入改动：`framework/*.props`、`Directory.Packages.props`、`framework/build/**`、`framework/tests/Directory.Build.props`、`tests/shared/**`；
    - 文件无法映射到项目；
    - 项目或 `ProjectReference` 有增删、改名；
    - 引用指向不存在的项目；
    - 存在无法解析的属性或 `Import`；
    - 存在未建模的编译输入（链接文件、显式跨项目 `Compile Include`）。
  - 现有选择器的全部保护保留：不确定的基线、脏树、显式跨项目编译输入。
  - 选择结果绑定基线 SHA 与候选 SHA。
  - 测试项目与共享测试基座分开处理：改共享基座退回全集。
- CI 的 `test` 作业按清单运行，并输出逐项目回执；`template-matrix` 聚合核对回执与清单一一对应，漏跑任何一个选中项目即失败。
- 本地 L1：`docs/framework/quality-assurance.md` 与框架 Skill 改为“按清单运行受影响测试项目”，全集留给 L3。
- 自检用例：
  - 单家族改动；
  - 被多家族依赖的 Core 改动；
  - 共享 props 改动；
  - 测试项目自身改动；
  - 无法映射的文件；
  - 项目删除或改名；
  - 不可解析的引用；
  - 空选择；
  - 漏跑一个选中项目时被聚合拒绝。

## 5. D：HostSettings 登记语义

- 语义：
  - 绑定的身份为：设置名、**有序**配置键序列（成员已体现在 `BindOption` 派生的键中）、fallback、Options 类型。四者逐项相等才算相同登记，重复登记不产生任何效果。
  - 同一设置名下，任一项不同（包括只换 fallback、只换键的顺序）即为冲突。
  - 不同设置的绑定累加。
  - 冲突在 `AddHostSettings` 调用时抛出，异常信息给出两处登记的差异，不推迟到首次解析。
- 实现须保证 Options 的 Configure 回调与注册校验一致：重复调用不追加会再次 Bind 的回调。
- 测试用真实 ServiceProvider 解析绑定 Options 并启动宿主，覆盖：
  - 相同登记；
  - 不同设置累加；
  - 同名不同目标；
  - 只换 fallback；
  - 只换键顺序。
  - 共享注册断言不得掩盖消费者错误。
- 同步组件文档与 XML 注释。按复核反馈附带的最小复现（`.tmp/remaining-review/repro`）先确认失败，再修复。

## 6. E：User 聚合与角色

- `UserRole` 成为 `User` 的子实体集合，按 `RoleId` 引用 `Role`，不持有 `Role` 实体。
- 分配与替换经 `User` 的方法完成（如 `AssignRoles(IEnumerable<Guid>)`、`ReplaceRoles(...)`）；领域规则（超级管理员保护等）在实体或 `UserDomainService` 内，只操作 `User` 聚合。
- 角色存在性与默认角色解析在应用服务中完成，经 `IRoleRepository`，跨聚合协调。
- 持久化：
  - 采用普通一对多子实体关系（与 ABP 的 `HasMany` 映射一致），不用 `OwnsMany`。表结构不变。
  - Identity、Resource 两套模型与快照都核对。确有差异时，区分未部署时的基线重建与已部署时的正式迁移。
  - 公开 DbSet 会自动注册默认仓储：去掉 `UserRole` 的 DbSet 或改为不登记，并加容器断言“`IRepository<UserRole>` 无法解析”。
  - 修改角色前显式加载集合，在 `IUserRepository` 提供带角色的读取方法，不依赖 `GetByIdAsync` 自动加载。
  - 成员关系软删除的方式保持现状，在实体方法中显式表达。
  - 去掉 `IRepository<UserRole>` 的全部 7 处使用，逐处改走聚合或 `IUserRepository`、`IRoleRepository` 的自定义查询：
    - `Users/AppServices/UserAppService`
    - `Domain/Users/DomainServices/UserDomainService`
    - `Roles/AppServices/RoleAppService`（删除角色时的清理）
    - `Permissions/Provider/PermissionSubjectProvider`
    - `Tenants/TenantSeeder`
    - `Initialization/SystemInitializer`
    - `DbMigrator/ResourceAdminBootstrapRunner`
  - 资源管理员引导不得重新授予已撤销的 Admin 成员关系，保留现有历史语义。
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
- 基线 `e984db19` 用当时的读取集合加上相同的 rules 重算，不拿当前文件集合去读旧提交。
- 最终上限在所有文档改动完成后确定，留约 1% 余量。
- 脚本新增 `REQUIRED_COMPANIONS`（文件 → 按任务必读的伴随文件）。自检规则：登记集合含某文件时，其伴随文件必须同在，否则失败。自检含删掉一个必读伴随文件的反例。提交说明写明口径变化。
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
- 与 A 的关系：升级说明的内容与表达全部归 U1，G 不触碰 `docs/framework/upgrades/`。U1 按本节原则书写，保留必要的“旧用法 → 新用法”。

## 9. 工作包

| 包 | 文件 | 依赖 |
| --- | --- | --- |
| U1 | `docs/framework/upgrades/**`（新建并迁移 0.13.0）、删除 `docs/framework/upgrade-0.13.0*.md`、`docs/framework/versioning.md`、`release.yml` 的打包参数（`PackageReleaseNotes`）与 Release 正文链接、现有发布验证入口、`check-retired-terms.ps1`、`scripts/check-doc-references.py` 白名单、仓库内对旧文件的引用 | 无 |
| U2 | `skills/leistd-net-framework/**`、`.agents/skills/developing-leistd-framework/SKILL.md` 的升级说明规则、`docs/framework/development-guide.md` 的升级说明规则 | U1 |
| V1 | `template/scripts/verify.ps1`、CI 薄壳、`template.json` 参数与排除、`template/docs/standards/testing.md`、`template/README.md`、`template/docs/README.md`、矩阵与生成测试接线 | 无 |
| S1 | `scripts/plan-quality-checks.py`、`.github/workflows/ci.yml` 的 test 作业与聚合、`test-quality-validation-plan.py`、`test-workflow-change-scope.py`、`docs/framework/quality-assurance.md` 的框架部分、框架 Skill 的验证入口段 | U2（与它共用框架 Skill，在其后改） |
| D1 | `framework/components/settings/Leistd.Settings.Hosting/**`、`framework/tests/components/settings/Leistd.Settings.Tests/Hosting/**`、`framework/docs/components/settings.md` | 无 |
| E1 | 第 6 节列出的 7 个消费点及 `User`、`UserRole` 实体、EF 映射与 `DependencyInjection`、两套模型快照、相关测试；`coding-backend.md`、`coding-common.md` 的聚合条目 | 无 |
| F1 | `scripts/measure-template-read-cost.py`（口径与自检先行；最终上限在 V1、E1、G2 完成后确定） | 口径部分无；上限依赖 V1、E1、G2 |
| G1 | `framework/components/**`、`framework/ddd-struct/**` 的注释与 `framework/docs/**`；`docs/framework/development-guide.md` §4 | U1、U2（共用开发指南，在其后改）、D1 |
| G2 | `template/frontend/**` 自有源码注释与 README；`template/docs/standards/coding-common.md`、`coding-frontend.md` 的注释规则 | E1、V1 |
| Z | Codex 终审、全量验证、删除本计划、推送、更新 PR | 全部 |

约束：

- 每个包先核实前提。
- 新增类型、脚本与文件须报告依据，对照 A15。
- 模板条件块整行独占。
- 文档按阶段五的单源与精简原则，不新增重复。

验证：`check-all`、各脚本自检、读取成本、打包与隔离消费、矩阵 `-Tier pr`；S1 改动聚合逻辑，需运行 `test-workflow-change-scope.py`、`test-quality-validation-plan.py`，并以远端 CI 验收。
