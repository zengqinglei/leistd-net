# Agent 工作流规范

## 1. 定位

本文件是 AI Agent 在项目内执行需求、开发、审查、测试、部署和验收的项目级唯一事实源。所有 Skill 启动后优先读取本文件；若本文件缺失，才使用 Skill 内置降级规则。

适用范围：自然语言想法进入、需求分析与 Plan 沉淀、任务登记与拆解、代码开发与自测、代码审查、测试验证、部署发布、验收收口。

## 2. 路径与命名

### 2.0 先定位「项目根」（所有 `docs/` 路径的基准）

本文件及所有 Skill 中出现的 `docs/...` 路径，都相对**项目根**解析，而非相对当前工作目录或仓库根。**项目根 = 同时直接包含 `docs/`、`backend/`（或后端源码目录）、`frontend/`（若有前端）的那一层目录。**

- **扁平布局**（`dotnet new` 直接生成）：项目根即仓库根——`/docs/`、`/backend/`、`/frontend/` 同级。
- **monorepo 布局**（项目被并入 `apps/<name>/` 等子目录）：项目根是 `apps/<name>/`——此时 `docs/standards/agent-workflow.md` 实际位于 `apps/<name>/docs/standards/agent-workflow.md`，**不是**仓库根的 `docs/`（仓库根可能另有无关的 `docs/`）。

> 执行任何一步前，先确认当前业务项目的项目根：从当前文件/工作目录向上或向下，找到那个同时含 `docs/` 与后端/前端源码目录的层级。之后本文与各 Skill 里的 `docs/xxx` 一律相对该根解析。`.claude/skills/` 可能在仓库根、也可能随项目在 `apps/<name>/` 下——**skill 的位置不代表项目根**，务必以「含 docs/backend/frontend 的目录」为准。

### 2.1 路径表

| 类型 | 路径（相对项目根，见 §2.0） | 说明 |
| --- | --- | --- |
| 需求登记册 | `docs/requirements/registry.md` | 需求状态、优先级、阶段、负责人、关联文档 |
| 需求 Plan | `docs/requirements/{req-id}-plan.md` | 单个需求的五段闭环方案 |
| 任务上下文 | `docs/requirements/context/{req-id}.md` | 当前阶段、进度、交接包、决策、阻塞、报告索引 |
| 项目规范 | `docs/standards/` | 长期有效规则 |
| 模块文档 | `docs/modules/{module}/` | 模块设计、API、计划、测试、状态 |
| 部署文档 | `docs/deploy/` | 环境、发布、回滚 |
| 开发自测报告 | `docs/reports/development/{req-id}-dev-report.md` | 开发变更、自测结果和风险 |
| 代码审查报告 | `docs/reports/code-review/{req-id}-code-review.md` | 缺陷、规范、需求对齐和 P0/P1/P2 结论 |
| 测试报告 | `docs/reports/tests/{req-id}-test-report.md` | 测试命令、结果、覆盖率和验收映射 |
| 部署报告 | `docs/reports/deploy/{req-id}-deploy-report.md` | 部署步骤、健康检查和回滚信息 |
| 验收报告 | `docs/reports/acceptance/{req-id}-acceptance.md` | Must have 证据、未完成项和收口结论 |

需求编号：`REQ-YYYYMMDD-NNN`。文件名使用小写：`req-yyyymmdd-nnn-plan.md`。

## 3. 阶段状态机

Skill 之间不直接调用彼此；大模型根据用户意图、当前上下文、阶段交接包和 Skill `metadata/description` 选择下一能力。

| Phase | 阶段 | 推荐 Skill | 输入 | 必须产物 | 通过条件 | 下一步 |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | 需求进入 | `requirement-plan` | 用户想法/背景/目标 | Plan 草案 | Plan 已生成并等待确认 | Phase 1 |
| 1 | 需求登记 | `task-manager` | 确认后的 Plan | registry + context | 任务已登记，阶段上下文已创建 | Phase 2 |
| 2 | 任务拆解 | `task-manager` | Plan + context | 子任务清单 + Must have 映射 | 简单任务自动通过；复杂任务经用户确认 | Phase 3 |
| 3 | 开发自测 | `coding` | Plan + context + 项目规范 | 代码变更 + dev report | 代码完成，最小验证通过或阻塞已记录 | Phase 4 或回退 |
| 4 | 代码审查 | `code-review` | diff + Plan + dev report | review report | 无 P0；P0 必须回退开发 | Phase 5 或 Phase 3 |
| 5 | 测试验证 | `test-runner` | Plan + 测试配置 + review report | test report | 测试通过，Must have 覆盖有证据 | Phase 6 或 Phase 3 |
| 6 | 部署发布 | `deploy` | 部署配置 + test/review report | deploy report 或部署方案 | 健康检查通过；生产变更已确认 | Phase 7 或阻塞 |
| 7 | 验收收口 | `task-manager` | Plan + 所有阶段报告 | acceptance report | registry/context 更新为 `done` 或 `blocked` | Done |

## 4. 阶段交接包

每个阶段结束时，必须在回复和任务上下文中沉淀同一份交接包，供下一 Skill 低成本读取。

```yaml
handoff:
  reqId: REQ-YYYYMMDD-NNN
  phase: 3
  phaseName: coding
  gateStatus: pass | fail | blocked | needs-confirmation
  nextRecommendedSkill: code-review | coding | test-runner | deploy | task-manager | none
  userConfirmationRequired: false
  artifacts:
    - docs/reports/development/req-yyyymmdd-nnn-dev-report.md
  evidence:
    - type: build | test | review | deploy | manual
      status: pass | fail | skipped
      path: docs/reports/...
      note: 简短说明
  verification:
    executed: true | false        # 验证是否真实执行
    type: build | test | review | health-check | manual | none
    degraded: false               # 是否降级（未按项目门槛/命令执行）
    note: 未执行或降级时必填原因
  retryCount: 0                   # 同一阶段回退次数，达到上限转 needs-confirmation
  blockers:
    - type: requirement | technical | permission | environment | external
      owner: user | agent | external
      action: 需要的下一步
  assumptions:
    - 已采用的关键假设
```

门禁规则：

- `pass`：下一阶段可继续。
- `fail`：当前阶段失败，优先回到产生问题的阶段修复。
- `blocked`：Agent 无法继续，必须记录责任方和下一步。
- `needs-confirmation`：需要用户确认后才能继续，例如复杂任务拆解、接口契约、数据迁移、生产部署。

Verifier 规则（loop 工程：verifier 是闭环瓶颈）：

- `verification.executed: false` 或 `degraded: true` 时，`gateStatus` 记为 `pass`，但必须在 `evidence` 中以 `status: skipped` 显式标注该验证未执行或降级，并将该风险纳入 acceptance 检查项；不得作为无风险的纯通过对待。
- 不得声称执行了未执行的验证。

Stop condition（回退上限）：

- 同一阶段回退（如 code-review→coding→code-review）累计达到 `retryCount >= 2` 时，`gateStatus` 升级为 `needs-confirmation`，交人工决策，避免无限循环。
- `retryCount` 上限默认 2，项目可在本文件覆盖。

## 5. Skill 输出契约

| Skill | 必须输出 | 默认下一步 |
| --- | --- | --- |
| `requirement-plan` | Plan 路径、需求 ID、范围、风险、验收标准、交接包 | 用户确认后 `task-manager` |
| `task-manager` | registry/context 更新、子任务清单、阶段状态、阻塞记录、验收收口结果 | 按 phase 推荐 |
| `coding` | 代码变更摘要、最小验证结果、dev report、交接包 | `code-review` |
| `code-review` | P0/P1/P2 结论、需求对齐、review report、交接包 | 无 P0 到 `test-runner`，有 P0 回 `coding` |
| `test-runner` | 测试结果、失败详情、覆盖率、Must have 证据、交接包 | `deploy` 或验收 |
| `deploy` | 部署方案或结果、健康检查、回滚信息、deploy report、交接包 | `task-manager` 验收收口 |

## 6. 需求 Plan 五段闭环

```markdown
# REQ-YYYYMMDD-NNN - {需求名称}

## 1. 需求&背景&目标
## 2. 核心策略
## 3. 实施步骤
## 4. 风险及应对策略
## 5. 验收闭环
```

最低要求：

- 需求&背景&目标：说明为什么做、做什么、谁使用、交付目标和范围边界。
- 核心策略：说明总体策略、目录/模块影响、核心流程、关键风险或待确认决策。
- 实施步骤：每步包含实施内容、产出物、验证方式，并列出文件变更清单。
- 风险及应对策略：记录风险、概率、影响、应对策略和触发后的处理。
- 验收闭环：Must have 3-7 个、Nice to have 0-3 个；每个 Must have 映射实施步骤、验证方式和证据。

## 7. Registry 与 Context 状态

Registry 状态枚举：

| 状态 | 含义 |
| --- | --- |
| candidate | 候选需求，尚未形成 Plan |
| planned | 已形成 Plan，等待确认或排期 |
| in-progress | 正在实施 |
| blocked | 被外部依赖、环境或决策阻塞 |
| review | 待审查或验收 |
| done | 已完成并通过验收 |
| archived | 已归档，不再活跃（物理归档动作见 §12） |

Context 单独记录 `phase`、`phaseName`、`progress` 和最近一次 `handoff`，不要另起与 registry 冲突的状态枚举。

## 8. 自动推进与人工确认

可自动推进：

- 用户已确认 Plan，且任务拆解无重大歧义。
- 开发只影响 Plan 范围内文件，不涉及安全、数据、生产配置或外部费用。
- 代码审查无 P0，测试通过，且项目配置允许进入下一阶段。

必须人工确认：

- 需求冲突、范围扩大、复杂任务拆解存在多种路径。
- 数据库迁移、批量更新、删除数据。
- 修改认证、授权、加密、审计、安全边界。
- 生产部署、回滚、重启服务。
- 引入新外部服务、费用、权限或合规风险。
- 使用真实密钥、账号、客户数据。
- 同一阶段回退达到 `retryCount` 上限（默认 2）。

## 9. 项目规范读取策略

按需读取，避免一次性加载所有文档：

1. 先读本文件，确定路径、状态机和交接包。
2. 根据当前 Skill 读取必要规范：技术栈、代码规范、测试规范、部署文档。
3. 优先读取项目级规范；缺失时按 Skill 降级规则结合现有配置和邻近代码推断。
4. 无技术栈信息时，只使用通用工程原则，不把模板默认 .NET/Angular 规范当作事实。

## 10. 项目级文档按需沉淀

项目级文档是**渐进沉淀**的，不要求初始齐全：

1. **初始可缺失**：除本文件（`agent-workflow.md`，工作流唯一事实源）与 `document-classification.md`/`document-naming.md` 建议尽早具备外，其余项目级规范（技术栈、编码、API、测试、部署、UI 等）与需求/模块/报告文档，**允许初始不存在，随项目推进按需创建**。缺失时不阻塞，按下面 §10.0 的沉淀式降级处理。

**§10.0 沉淀式降级（缺文档时的总则，优先于任何"内置默认"）**：项目缺某类规范文档时——
   - **结构骨架类**（报告/Plan/context/交接包该含哪几节/哪些字段，与阶段契约绑定）：直接用 skill 内置骨架继续（骨架是机制，不是偏好）。
   - **具体内容/配置类**（编码/测试/API 的具体规则、覆盖率门槛、lint/测试配置数值等项目偏好）：**不预设任何具体默认值**；用 skill 的**提问清单/结构骨架引导用户现场约定**，把结果**沉淀到对应 `docs/standards/*`**，之后**以项目沉淀的那一版为唯一范本，不再回落 skill 内置**。skill 里的具体规则/配置样例仅作**同技术栈参考**（标注"非通用默认"），套用前须按项目约定核对替换；非本栈项目据同类思路自行制定。
   - 首次无上一版可参考时，靠"结构骨架 + 提问清单"引导从零制定，不靠 AI 凭记忆自编默认值。
2. **何时沉淀**：当出现“通用/内置默认与本项目实际不一致（冲突）”或“反复需要某规范却无处可依（缺失）”时，主动**提示用户并沉淀/更新到对应项目文档**，把一次性判断变成可复用规则。
3. **沉淀落点**：按各 Skill `references/reconcile-strategy.md` 的「冲突沉淀落点矩阵」确定目标文件（技术栈→`tech-stack.md`；编码→`code-standard/`；API→`api-standard.md`；测试门槛→测试配置；命名→`document-naming.md`；部署→`docs/deploy/`）。
4. **沉淀原则**：只沉淀通用可复用规则，不写一次性内容；同一规则单一来源、不多处重复（见 `document-naming.md`、`document-classification.md`）。
5. **哪些必备 vs 按需**：见 `document-classification.md` §1「初始态」列。

## 11. AI 执行约束

- 不得跳过规范读取直接实施复杂需求。
- 不得声称执行了未执行的测试、构建、部署或健康检查。
- 不得在未确认时执行生产部署、删除数据、迁移数据或修改密钥。
- 不得将具体项目私密信息写入模板文档。
- 修改已有代码时，不得回滚用户未授权的变更。
- 阶段失败时先记录证据和根因，再决定回退方向。

## 12. 文档归档机制

需求方案、任务上下文、阶段报告都是**一次性、随时间累积**的产物（`requirements/`、`requirements/context/`、`reports/**`）。默认组织方式是**扁平平铺**——文件名内嵌需求 ID（`req-YYYYMMDD-NNN`）已自带时间序，无需按迭代/年月预建目录层级，也不要为空项目预建归档目录。归档是**到期后的物理动作**，而非初始结构。

**归档触发**：需求在 registry 置为 `done` 或 `archived` 后，其方案/上下文/报告即成为历史证据。当满足下列任一条件时，执行一次物理归档：

1. 需求 `done`/`archived` 后**超过约定保留期**（默认 3 个月，项目可在本文件调整）；
2. 某一报告目录（如 `reports/code-review/`）**单目录文件数过多**（默认超过约 50 个）影响检索。

**归档动作**（低频、可由人工或 task-manager 收口时触发，不在日常阶段流程里自动做）：

- 需求方案：`requirements/{req-id}-plan.md` → `requirements/archive/{req-id}-plan.md`
- 任务上下文：`requirements/context/{req-id}.md` → `requirements/context/archive/{req-id}.md`
- 阶段报告：`reports/{type}/{req-id}-*.md` → `reports/{type}/archive/{req-id}-*.md`
- 归档只**移动文件、保持原文件名不变**（ID 内的日期即归档批次依据）；`registry.md` 中该需求行状态置 `archived`、并从「活跃需求」移入「已完成需求」表，路径列更新为 archive 后的路径。

**约束**：

- 归档目录（`archive/`）**按需创建**——第一次归档时才建，不预置空目录。
- 归档**不改变命名规范**（仍是 `document-naming.md` 的 `{req-id}-{report-type}.md`），只增加一层 `archive/` 前缀路径；Skill 的产出路径逻辑**不受影响**（新产物始终写扁平目录，只有历史文件才进 `archive/`）。
- 归档是**证据留存**，不是删除；除非用户显式要求，不删除任何归档文件（见 §11）。
- 若项目确需按迭代/批次聚合视角，**优先在 `registry.md` 增加“迭代/批次”列**做逻辑聚合，而非改物理目录层级——保持 Skill 路径稳定。
