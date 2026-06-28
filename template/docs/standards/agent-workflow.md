# Agent 工作流规范

## 1. 定位

本文件是 AI Agent 在项目内执行需求、开发、审查、测试、部署和验收的项目级唯一事实源。所有 Skill 启动后优先读取本文件；若本文件缺失，才使用 Skill 内置降级规则。

适用范围：自然语言想法进入、需求分析与 Plan 沉淀、任务登记与拆解、代码开发与自测、代码审查、测试验证、部署发布、验收收口。

## 2. 路径与命名

| 类型 | 路径 | 说明 |
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
| archived | 已归档，不再活跃 |

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

## 9. 项目规范读取策略

按需读取，避免一次性加载所有文档：

1. 先读本文件，确定路径、状态机和交接包。
2. 根据当前 Skill 读取必要规范：技术栈、代码规范、测试规范、部署文档。
3. 优先读取项目级规范；缺失时按 Skill 降级规则结合现有配置和邻近代码推断。
4. 无技术栈信息时，只使用通用工程原则，不把模板默认 .NET/Angular 规范当作事实。

## 10. AI 执行约束

- 不得跳过规范读取直接实施复杂需求。
- 不得声称执行了未执行的测试、构建、部署或健康检查。
- 不得在未确认时执行生产部署、删除数据、迁移数据或修改密钥。
- 不得将具体项目私密信息写入模板文档。
- 修改已有代码时，不得回滚用户未授权的变更。
- 阶段失败时先记录证据和根因，再决定回退方向。
