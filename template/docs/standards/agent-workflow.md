# Agent 工作流规范

## 1. 适用范围

本规范是 AI Agent 执行需求、开发、审查、测试、部署和验收的项目级唯一事实源。所有 Skill 启动后应优先读取本文件；若本文件缺失，Skill 才使用自身降级规则。

适用于：

- 自然语言想法或需求进入。
- 需求分析、方案设计和 Plan 沉淀。
- 任务拆解、上下文恢复和进度跟踪。
- 代码开发、自测、代码审查和测试验证。
- 部署准备、部署执行、健康检查和验收收口。

## 2. 文件路径与命名

| 类型 | 路径 | 说明 |
| --- | --- | --- |
| 需求登记册 | `docs/requirements/registry.md` | 需求状态、优先级、负责人、关联文档 |
| 需求 Plan | `docs/requirements/{req-id}-plan.md` | 单个需求的五段闭环方案 |
| 任务上下文 | `docs/requirements/context/{req-id}.md` | 阶段、进度、决策、阻塞和报告索引 |
| 模块文档 | `docs/modules/{module}/` | 模块设计、API、计划、测试、状态 |
| 项目规范 | `docs/standards/` | 长期有效规则 |
| 部署文档 | `docs/deploy/` | 环境、发布、回滚 |
| 开发自测报告 | `docs/reports/development/{req-id}-dev-report.md` | 开发变更、自测结果和风险 |
| 代码审查报告 | `docs/reports/code-review/{req-id}-code-review.md` | 缺陷、规范、需求对齐和 P0/P1/P2 结论 |
| 测试报告 | `docs/reports/tests/{req-id}-test-report.md` | 测试命令、结果、覆盖率和验收映射 |
| 部署报告 | `docs/reports/deploy/{req-id}-deploy-report.md` | 部署步骤、健康检查和回滚信息 |
| 验收报告 | `docs/reports/acceptance/{req-id}-acceptance.md` | Must have 证据、未完成项和收口结论 |

需求编号：`REQ-YYYYMMDD-NNN`。文件名使用小写：`req-yyyymmdd-nnn-plan.md`。

## 3. 阶段状态机

阶段表描述推荐能力与产物契约。实际触发由大模型根据当前上下文、用户意图和各 Skill metadata/description 自动选择；Skill 之间不直接调用或依赖彼此。

| Phase | 阶段 | 推荐能力 | 输入 | 必须产物 | 完成条件 | 后续上下文 |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | 需求进入 | `requirement-plan` | 用户想法/背景/需求 | `docs/requirements/{req-id}-plan.md` | Plan 草案生成并等待确认 | 1 |
| 1 | 需求登记 | `task-manager` | 确认后的 Plan.md | `registry.md` + `context/{req-id}.md` | registry/context 已创建或更新 | 2 |
| 2 | 任务拆解 | `task-manager` | Plan.md + context | 子任务清单 + Must have 映射 | 子任务清单已确认，简单任务可自动通过 | 3 |
| 3 | 开发自测 | `coding` | Plan.md + 子任务清单 + 项目规范 | 代码变更 + dev report | 代码完成，最小验证通过或明确阻塞 | 4 |
| 4 | 代码审查 | `code-review` | 代码变更 + Plan.md + dev report | code-review report | 无 P0 问题；有 P0 记录修复要求 | 需要测试验证或返回开发修复 |
| 5 | 测试验证 | `test-runner` | Plan.md + 测试配置 + review report | test report | 测试通过，Must have 覆盖已标记；失败记录修复要求 | 需要部署发布或返回开发修复 |
| 6 | 部署发布 | `deploy` | 部署配置 + test report + review report | deploy report | 健康检查通过，或因缺配置生成部署方案等待确认 | 7 |
| 7 | 验收收口 | `task-manager` | Plan.md + 所有阶段报告 | acceptance report | registry/context 更新为 `done` 或 `blocked` | Done |

## 4. Skill 输出契约

- `requirement-plan`：输出 Plan 草案、需求 ID、范围、风险和验收标准；上下文进入“等待确认/需求登记”。
- `task-manager`：输出 registry/context 更新、子任务清单、阶段状态、阻塞记录和验收收口结果。
- `coding`：输出代码变更、最小验证结果、dev report 和是否需要正式代码审查的上下文。
- `code-review`：输出 P0/P1/P2 结论、需求对齐结果和是否具备测试验证条件的上下文。
- `test-runner`：输出测试结果、失败详情、覆盖率、Must have 证据和是否具备部署条件的上下文。
- `deploy`：输出部署方案或部署结果、健康检查、回滚信息和是否具备验收收口条件的上下文；生产变更前必须获得用户确认。

## 5. 需求 Plan 五段闭环

```markdown
# REQ-YYYYMMDD-NNN - {需求名称}

## 1. 需求&背景&目标
## 2. 核心策略
## 3. 实施步骤
## 4. 风险及应对策略
## 5. 验收闭环
```

### 5.1 需求&背景&目标

必须包含：

- 背景：为什么做，当前问题是什么。
- 需求：做什么、谁使用、核心场景是什么。
- 目标：可衡量的交付结果。
- 范围边界：本次包含和本次不包含。

### 5.2 核心策略

必须包含：

- 总体策略：怎么做，为什么这样做。
- 调整内容树形目录结构。
- 核心架构图或流程图。
- 重点注意事项及策略。
- 需要人工确认的风险或决策。

### 5.3 实施步骤

每个步骤必须包含：

| 步骤 | 实施内容 | 产出物 | 验证方式 |
| --- | --- | --- | --- |
| 1 | {内容} | {产出} | {验证} |

同时必须提供文件变更清单：

| 文件 | 操作 | 说明 |
| --- | --- | --- |
| `{path}` | 新增/修改/删除 | `{说明}` |

### 5.4 风险及应对策略

| 风险 | 概率 | 影响 | 应对策略 | 触发后的处理 |
| --- | --- | --- | --- | --- |
| `{risk}` | 高/中/低 | 高/中/低 | `{mitigation}` | `{fallback}` |

### 5.5 验收闭环

必须包含：

- Must have：必须满足，3-7 个。
- Nice to have：可选增强，0-3 个。
- 验收映射：每个 Must have 对应实施步骤、验证方式和证据。
- 完成定义：代码、审查、测试、构建、部署、用户验收。

验收映射格式：

| 验收项 | 对应实施步骤 | 验证方式 | 证据 |
| --- | --- | --- | --- |
| {验收项} | 步骤 1 | 测试/构建/API/页面验证 | {证据} |

## 6. Registry 状态定义

| 状态 | 含义 |
| --- | --- |
| candidate | 候选需求，尚未形成 Plan |
| planned | 已形成 Plan，等待确认或排期 |
| in-progress | 正在实施 |
| blocked | 被外部依赖、环境或决策阻塞 |
| review | 待审查或验收 |
| done | 已完成并通过验收 |
| archived | 已归档，不再活跃 |

`context` 可单独记录 `phase` 和 `progress`，不要另起一套与 registry 冲突的状态枚举。

## 7. 更新规则

- 新需求进入时写入 `docs/requirements/registry.md`，状态为 `candidate` 或 `planned`。
- Plan 确认后创建或更新 `docs/requirements/context/{req-id}.md`，状态改为 `in-progress`。
- 每个 Phase 完成后更新 context 的阶段、进度、报告路径和时间线。
- 进入审查或用户验收时 registry 状态改为 `review`。
- 完成后记录完成时间、验收人和关联报告，状态改为 `done`。
- 阻塞时记录阻塞原因、需要谁处理和下一步，状态改为 `blocked`。

## 8. AI 执行约束

- 不得在未确认时执行生产部署、删除数据、迁移数据或修改密钥。
- 不得将具体项目私密信息写入模板文档。
- 不得跳过规范读取直接实现复杂需求。
- 不得声称执行了未执行的测试、构建、部署或健康检查。
- 遇到需求冲突时，应先记录假设并请求确认。
- 修改已有代码时，不得回滚用户未授权的变更。
- 没有项目级规范时，Skill 可以按自身降级策略执行，但必须说明假设和建议补齐规范。

## 9. 高风险人工确认点

以下事项必须人工确认：

- 数据库迁移、批量更新、删除数据。
- 修改认证、授权、加密、审计、安全边界。
- 生产部署、回滚、重启服务。
- 引入新外部服务、费用、权限或合规风险。
- 使用真实密钥、账号、客户数据。

