# 端到端更新闭环审视：文档 / 组件 / skill 的分发与回流

> 从「谁在什么位置、通过什么机制更新什么」的端到端视角，审视 leistd-net 全链路的一致性，找出断点，给出闭环策略。基于对 `common.props`、`release.yml`、`ci.yml`、`template/`、`skills/`、两个 CI 校验脚本的实测勘察（2026-07-10）。

---

## 一、场景全景：四条更新链路

两类角色 × 两类制品，构成四条链路：

| # | 角色 | 更新什么 | 现状机制 | 通/断 |
| --- | --- | --- | --- | --- |
| ① | **框架开发者** | 框架组件**文档** | 文档随 `dotnet pack` 打进同版本 NuGet 包（`common.props:64-84`，按家族名）；改 `framework/docs/` 会触发 stable 发版（`release.yml:60`）；CI 双闸门校验文档↔源码（`ci.yml:19-24`） | ✅ 基本通 |
| ② | 框架开发者 | 框架级 **skill** | `skills/leistd-net-framework/SKILL.md` 手写、纯索引；发布靠下游 `npx skills add zengqinglei/leistd-net`；**不进 CI、不进 release** | ⚠️ 脱离流水线 |
| ③ | **业务用户** | 框架**组件版本 + 用法文档** | 改 `Directory.Build.props` 的 `LeistdFrameworkVersion` → `dotnet restore` 拉新版包 → 组件文档随新包 `docs/` **拉动式**更新 | ✅ 通 |
| ④ | 业务用户 | 已生成项目里的 **skill + 规范快照** | `dotnet new` 一次性拷贝，**无任何回流机制**；上游演进后用户项目里的旧快照无法同步 | 🔴 断 |

**核心洞察**：制品分两类分发范式——
- **拉动式（pull，随版本自动到位）**：组件**代码 + 文档**随 NuGet 包版本走，用户升版本即得。这条设计正确、闭环成立。
- **快照式（snapshot，一次性拷贝、不回流）**：项目级 **skill + 规范**在 `dotnet new` 时拷入，之后与上游**永久脱钩**。这是端到端闭环最大的结构性缺口。

---

## 二、断点清单（按严重度）

### 🔴 高

**断点 I — 项目级 skill / 规范快照无回流机制**（链路 ④，全链路最严重）
`template/.claude/skills/`（6 个 skill）与 `template/docs/standards/`（11 项规范）在 `dotnet new` 时被拷贝为快照（`template.json` 无 `postActions`，仅排除 `.claude/scripts/`）。上游改进后，**已生成的用户项目没有任何同步路径**——不是 NuGet 包（不随框架版本流动）、无 postAction、无 npx 订阅。用户只能重新 `dotnet new` 再手工 diff/拷贝。
- **雪上加霜**：6 个 skill 中**只有 task-manager 有 version 字段**（`5.1.0`，且与框架 `VERSION` 无关联），其余 5 个 skill + 全部 standards **零版本标记**——用户连「自己项目里的这套是否过期、过期多少」都无从判断。
- **流程坐实**：`release.yml:58-61` 明确把模板/根 `*.md` 排除在发版触发外，从流程上确认 skill/standards 的演进与框架发版**解耦、不随包流动**。

**断点 A/B — 框架级 skill 无一致性校验；文档反向漂移无校验**（链路 ①②）
- **A**：`skills/leistd-net-framework/SKILL.md` 的家族清单/选型表与 `framework/docs/` 一致性**无任何机制保证**（skill 不被任何 workflow 引用）。新增/删除家族后 skill 表忘更新 → 漂移，靠人自觉。
- **B**：`check-docs-api-drift.ps1` 是**单向**校验（只查「文档写了源码没有的 API」=臆造），**不查反向**（源码新增/改名了公共 API、文档滞后）。这是「代码改了、文档没跟上」的结构性漏洞。

### ⚠️ 中

**断点 H — 生成项目默认不带框架级 skill，且无提示**（链路 ③）
框架级 skill 随 `npx skills add` 装、**不随模板**。生成项目只有 6 个项目级 skill，缺 `leistd-net-framework` 索引。
- **有兜底**：`template/backend/CLAUDE.md`（随模板生成）已引导 AI「去 NuGet 缓存读随包 `docs/*.md`」，所以 AI 查 API 不是完全无依据，只是少了 skill 层的家族索引与「框架未覆盖能力」防臆造清单。
- **但缺口在**：CLAUDE.md **没提示「先装 `leistd-net-framework` skill」**——价值主张（AI 按真实 API 编码、防臆造）与落地路径没接上。

**断点 C — `versioning.md` 与 `release.yml` 矛盾**（链路 ①）
`versioning.md:45` 称「`framework/docs/`、所有 `*.md` 改动均不发版」，但 `release.yml:60` 实际**会**因 `framework/docs/` 变更触发 stable 发版。文档漂移，实际以 `release.yml` 为准。

**断点 D — release 不复跑文档闸门**（链路 ①）
`ci.yml` 的 docs-sync 闸门只在 PR/develop push 跑；`release.yml`（push main 发包）**不复跑**。绕过 PR 直推 main 时，文档一致性无二次拦截。

**断点 E — 示例自包含/依赖方向约束靠人工**（链路 ①）
`development-guide.md §5.1` 的「组件示例只用自身 API、不引 ddd-struct 类型」只有部分被 drift 脚本间接覆盖（臆造），「示例引错层类型」无脚本校验。

### 低
- **F**：drift 脚本白名单需人工维护。
- **G**：文档粒度（家族）与包粒度（项目）不对齐——已知可接受取舍。
- **J**：monorepo 路径解析（§2.0）已主动防御，但依赖 AI 运行时遵守「找含 docs/backend/frontend 的项目根」——软性约定，非机制保证。
- **额外事实修正**：`LeistdUseLocalFramework` 本地调试开关**在整个仓库中不存在**（repo-wide grep 无命中）——过去多轮讨论曾假设它存在，实为误记。真实的本地框架联调是 `local-feed` 方式（`dotnet pack -o ./local-feed` + `dotnet restore --source ./local-feed`，见 `template/README.md:166-186`），不是 MSBuild 条件开关。相关文档/记忆若引用该开关需更正。

---

## 三、端到端闭环策略

按「拉动式 vs 快照式」两条范式分别收口，再补校验闸门。

### 策略 1：把「快照式」制品尽量转为「可感知、可回流」（治断点 I，最优先）

项目级 skill/规范本质是快照，无法像 NuGet 那样自动拉动，但可以让它**可感知版本差 + 有明确回流路径**：

1. **给快照打版本印记**：`dotnet new` 生成时，在项目 `docs/standards/` 或 `.claude/` 写入一个 `LEISTD_TEMPLATE_VERSION`（值 = 生成时的 `VERSION`）。让用户/AI 能判断「我这套 skill/规范是哪个模板版本的」。
2. **提供回流说明 + 命令**：在生成项目的 `docs/standards/README.md` 或根 README 写明「如何同步上游 skill/规范更新」——最务实的是引导重新 `dotnet new` 到临时目录后 diff `.claude/skills/` 与 `docs/standards/`，或（若框架级 skill 走 `npx skills`）把项目级 skill 也纳入某种可拉取的分发。
3. **长期方向**：评估把「工作流 skill + 规范」也做成可 `npx skills add` / 可版本化拉取的包，而非纯模板快照——这样链路 ④ 也能变拉动式。（需评估成本，见 `2026-07-10-manifest-driven-doc-discovery.md` 的渐进思路。）

### 策略 2：让「框架级 skill」进入流水线（治断点 A、H、②）

1. **CI 加 skill 一致性闸门**：新增一个校验，比对 `skills/leistd-net-framework/SKILL.md` 的家族清单与 `framework/components/` 实际家族目录一致（类似 `check-docs-sync.ps1` 的做法）——家族增删后 skill 表忘更新即 CI fail。
2. **明确 skill 发布触发**：skill 变更也应触发一次「skill 发布校验」（至少 CI 跑一次 `npx skills add --list` 冒烟，确认可被发现——呼应 skill 发布待办）。
3. **生成项目提示装框架级 skill（治 H）**：在生成项目的 README 或 `.claude/` 引导里加一句「查 Leistd 组件用法，请 `npx skills add zengqinglei/leistd-net`」，把价值主张与落地路径接上。

### 策略 3：补「文档反向漂移」校验（治断点 B、E）

1. **反向校验**：扩展或新增脚本，扫描 `framework/components` 的 public API（DI 扩展方法 `Add*/Map*/Use*`、public 接口），检查其是否在对应家族文档中被提及——source→doc 方向，抓「代码改了文档没跟」。可先做「DI 扩展方法必须在文档出现」这一最高价值子集。
2. **release 复跑闸门（治 D）**：`release.yml` 发包前复跑 `check-docs-sync.ps1` + `check-docs-api-drift.ps1`，作为直推 main 的二次防线。

### 策略 4：修文档矛盾（治断点 C，即时）

改 `versioning.md:45`，与 `release.yml:60` 对齐——明确「`framework/docs/` 变更会触发 stable 发版（文档随包送达），其余 `*.md`（模板/根文档）不触发」。

---

## 四、优先级建议

| 优先级 | 动作 | 治哪个断点 | 成本 |
| --- | --- | --- | --- |
| P0（即时、低成本） | 修 `versioning.md` 矛盾（策略 4）；生成项目 README 加「装框架级 skill」提示（策略 2.3） | C、H | 低 |
| P0（即时） | 给生成项目打模板版本印记 + 回流说明（策略 1.1、1.2） | I | 中 |
| P1 | CI 加 skill 家族清单一致性闸门（策略 2.1）；release 复跑文档闸门（策略 3.2） | A、D | 中 |
| P1 | 文档反向漂移校验，先做 DI 扩展方法子集（策略 3.1） | B、E | 中 |
| P2（长期） | 项目级 skill/规范转为可版本化拉取（策略 1.3） | I 根治 | 高 |

---

## 五、一句话结论

**「代码 + 文档」的拉动式闭环（随 NuGet 版本）已成立且正确；真正的缺口在「skill + 规范」的快照式分发——它们一次性拷入用户项目后与上游永久脱钩（断点 I），且框架级 skill 全程脱离流水线、无一致性校验（断点 A）。** 闭环策略的核心是：给快照打版本印记 + 明确回流路径，并把框架级 skill 纳入 CI 校验与生成项目的落地提示。
