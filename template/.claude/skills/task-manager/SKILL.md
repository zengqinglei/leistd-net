---
name: task-manager
description: |
  在 Plan 确认后需要任务登记、上下文恢复、阶段推进、进度跟踪或验收收口时使用；不直接替代开发/审查/测试/部署，只维护可追溯状态与阶段推进。

  使用时机：
  (1) Plan.md 已确认，需要初始化 registry/context 或开始实施
  (2) 需要查询、恢复、更新任务状态或保存阶段交接包
  (3) 需要拆解子任务、维护 Must have 映射或处理阻塞
  (4) 测试/部署完成后需要生成验收报告并收口
  (5) 用户自然语言：「这个需求进行到哪了」「继续上次的任务」「登记 / 验收这个需求」「拆一下子任务」

  不适用：还没有已确认 Plan（先用 requirement-plan）；具体阶段执行（用 coding/code-review/test-runner/deploy）。
metadata:
  openclaw:
    requires: []
    skillKey: "task-manager"
version: "5.1.0"
user-invocable: true
disable-model-invocation: false
---

# 任务管家 (task-manager)

> 负责 registry/context 的可追溯状态，不直接替代开发、审查、测试或部署 Skill。

## 必读顺序

1. 读取 `docs/standards/agent-workflow.md`，获取状态机、路径和阶段交接包格式。**并按其 §2.0 定位「项目根」**（含 `docs/`+`backend/`+`frontend/` 的那一层），本 skill 所有 `docs/xxx` 均相对该根解析——monorepo 布局下项目根是 `apps/<name>/` 而非仓库根。
2. 读取 `docs/requirements/registry.md`。
3. 读取目标任务的 `docs/requirements/{req-id}-plan.md` 和 `docs/requirements/context/{req-id}.md`（如存在）。
4. 仅在复杂多子任务编排时读取 `references/task-orchestration.md`。

## 核心职责

- **登记**：Plan 确认后创建/更新 registry 和 context。
- **拆解**：从 Plan 的实施步骤和 Must have 生成子任务、验证映射和执行顺序。
- **恢复**：根据 context、最近交接包和 git 状态判断从哪个 Phase 继续。
- **推进**：阶段完成后写入报告路径、进度、时间线和新的 handoff。
- **阻塞**：记录阻塞类型、责任方、所需动作和回退建议。
- **收口**：汇总 dev/review/test/deploy 报告，生成 acceptance report，更新状态为 `done` 或 `blocked`。

## 阶段门禁

| 场景 | 操作 |
| --- | --- |
| Plan 已确认 | 创建 registry/context，进入 Phase 2 |
| 子任务简单且无关键歧义 | 自动进入 Phase 3 `coding` |
| 子任务复杂或存在多方案 | `gateStatus: needs-confirmation`，等待用户确认 |
| 阶段报告为 pass | 更新 context，推荐下一 Skill |
| 阶段报告为 fail | 记录失败证据，将该阶段对的 retryCount 加 1 并写入 context；达到上限（默认 2）时置 gateStatus 为 needs-confirmation，推荐回退到责任阶段 |
| 阶段报告为 blocked | 更新 registry 为 `blocked`，说明责任方和下一步 |
| 所有 Must have 有证据 | 生成验收报告，registry/context 置为 `done` |

## Registry 规则

默认路径：`docs/requirements/registry.md`。

必备字段：需求 ID、名称、状态、优先级、Phase、进度、负责人、模块、Plan、Context、最近更新、备注。

状态只能使用：`candidate`、`planned`、`in-progress`、`blocked`、`review`、`done`、`archived`。

`done`/`archived` 后的需求方案、上下文、报告是历史证据，默认**扁平保留**（文件名内嵌 `req-YYYYMMDD-NNN` 自带时间序）。仅当用户显式要求归档，或需求 done 超保留期 / 单报告目录文件过多时，才执行物理归档（移入 `archive/` 子目录、文件名不变、registry 行移入「已完成需求」并更新路径列）——**新产物始终写扁平目录，不预建 `archive/`**。操作细节见 `references/archiving-policy.md`，规则源以 `docs/standards/agent-workflow.md` §12 为准。

## Context 规则

默认路径：`docs/requirements/context/{req-id}.md`。

必须记录：基本信息、当前 Phase、进度、最近 handoff、阶段报告、验收映射、子任务、决策、阻塞、时间线、最近对话摘要、阶段回退次数（retryCount，按阶段对累计）。

创建或修复 context 时读取 `templates/task-context-template.md`。

## 验收收口

收口前检查：

- Plan 的 Must have 均有实施、测试或运行证据。
- code-review 无 P0。
- 测试报告通过；未执行项有原因和风险说明。
- 部署报告存在，或 Plan/用户确认本次无需部署。

生成 `docs/reports/acceptance/{req-id}-acceptance.md`，模板见 `templates/acceptance-report-template.md`。

## 输出

- registry/context 变更摘要。
- 当前 Phase、状态、进度和下一步。
- 如有阻塞，输出责任方和所需动作。
- 最新阶段交接包。

## 按需资源

| 资源 | 路径 | 读取时机 |
| --- | --- | --- |
| Context 模板 | `templates/task-context-template.md` | 创建或修复 context 时 |
| 验收报告模板 | `templates/acceptance-report-template.md` | Phase 7 收口时 |
| 编排规则 | `references/task-orchestration.md` | 多子任务且有依赖时 |
| 对齐与降级策略 | `references/reconcile-strategy.md` | 缺少 registry/context，或状态与规范冲突时 |
| 归档策略 | `references/archiving-policy.md` | 用户要求归档、或历史文档到期/单目录膨胀时 |

## 禁止事项

- 不在未读取 context 的情况下更新状态。
- 不跳过时间线和 handoff 记录。
- 不用自定义状态替代 registry 状态枚举。
- 不把阻塞任务标记为完成。
- 不替代生产部署确认。
