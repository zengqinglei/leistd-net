---
name: task-manager
description: |
  任务管家 Skill，用于 Plan 确认后的流程主控、任务识别、状态更新、上下文恢复、进度跟踪和验收收口。

  **当以下情况时使用此 Skill**:
  (1) 用户查询任务进度（如"定时发布功能进度如何？"）
  (2) 需要更新任务状态或进度
  (3) Agent 启动时需要恢复任务上下文
  (4) 需要列出所有活跃任务
  (5) 需要记录决策或阻塞问题
  (6) 对话结束时需要保存上下文
  (7) 需求包含多个子任务时，自动编排派发
  (8) Plan.md 已确认，需要初始化 registry/context 并推进 coding → code-review → test-runner → deploy → acceptance
  (9) 部署或测试完成后需要生成验收报告并收口任务

  **调用 Agent**：规划助手、开发助手、质量助手（所有 Agent 共用）
metadata:
  openclaw:
    requires: []
    skillKey: "task-manager"
version: "5.0.0"
user-invocable: true
disable-model-invocation: false
---

# 任务管家 (task-manager)

> Plan 确认后的流程主控：登记、拆解、编排、状态更新、验收收口

## 🚨 执行前必读

- ✅ **直接操作文件**：使用 `read/write/edit` 工具，不调用脚本
- ✅ **任务 ID 格式**：`REQ-YYYYMMDD-XXX` (全局) 或 `REQ-YYYYMMDD-XXX-FE/BE/QA` (模块)
- ✅ **进度范围**：0-100 整数
- ✅ **状态枚举**：`pending`, `in_progress`, `waiting_review`, `blocked`, `completed`
- ✅ **上下文文件位置**：优先使用项目级路径 `docs/requirements/context/{taskId}.md`；个人私有上下文可使用 `~/.openclaw/agents/{agentId}/task-context/{taskId}.md`
- ✅ **项目路径**：任务上下文中必须包含 `projectRoot` 字段
- ⚠️ **降级提示**：当项目无规范文档时，**必须提示用户**建议创建规范文档
- ✅ **自动保存**：对话结束时保存上下文
- ✅ **主控职责**：Plan 确认后必须初始化 registry/context，并按阶段状态机推进

---

## 📋 工作流程

```
1. 识别任务
   → 读取 registry.md
   → 匹配用户消息中的关键词
   → 确定任务 ID

2. 恢复上下文
   → 读取任务上下文文件
   → 读取 Plan.md

3. 执行操作
   → 查询进度：返回状态
   → 更新进度：编辑上下文文件
   → 保存对话：追加对话摘要
   → Plan 已确认：初始化任务并进入任务拆解
   → 阶段完成：更新 context、registry 和阶段报告索引
   → 全部完成：生成 acceptance report 并收口

4. 保存上下文
   → 更新任务上下文文件
```

---

## 🎯 核心能力

### 0. Plan 确认后的流程主控

**时机**：用户确认 Plan.md，或明确要求“开始实施/继续执行计划”。

**必须执行**：
1. 读取 Plan.md，提取 `req-id`、范围、实施步骤、Must have、风险。
2. 创建或更新 `docs/requirements/registry.md`。
3. 创建或更新 `docs/requirements/context/{req-id}.md`。
4. 生成子任务清单和 Must have 映射；复杂任务先让用户确认。
5. 按阶段状态机推进：`coding → code-review → test-runner → deploy → acceptance`。
6. 每个阶段完成后更新 context 的阶段、进度、报告路径和时间线。
7. 全部完成或阻塞时生成收口报告。

**阶段产物路径**：
| 阶段 | 报告路径 |
|------|----------|
| 开发自测 | `docs/reports/development/{req-id}-dev-report.md` |
| 代码审查 | `docs/reports/code-review/{req-id}-code-review.md` |
| 测试验证 | `docs/reports/tests/{req-id}-test-report.md` |
| 部署 | `docs/reports/deploy/{req-id}-deploy-report.md` |
| 验收收口 | `docs/reports/acceptance/{req-id}-acceptance.md` |

**收口条件**：
- `completed`：Must have 均有证据，测试/审查/部署满足项目门槛，用户验收或项目约定允许自动完成。
- `blocked`：任一阶段失败且无法自动恢复，或需要用户/外部系统决策。

### 1. 识别任务

**输入**：用户消息（如"定时发布功能进度如何？"）

**操作**：
```markdown
1. 使用 `read` 读取 registry.md
   - 优先路径：`docs/standards/agent-workflow.md` 中指定的路径
   - 默认路径：`docs/requirements/registry.md`
   - 如不存在，尝试：`docs/registry.md`

2. 提取任务列表（或解析 Plan.md）
   - 解析 Markdown 表格
   - 提取任务 ID、名称、状态
   - 若存在 Plan.md，按五段闭环结构解析：
     - `## 1. 需求&背景&目标`：理解范围和目标
     - `## 2. 核心策略`：识别模块边界和架构约束
     - `## 3. 实施步骤`：提取子任务清单和文件变更清单
     - `## 4. 风险及应对策略`：识别风险点
     - `## 5. 验收闭环`：提取验收项用于任务映射

3. 匹配关键词
   - 基础匹配：字符串包含
   - 语义匹配：同义词扩展（进度≈状态≈情况）

4. 降级处理
   - 如 registry.md 不存在：
     - 提示用户："⚠️ 未找到需求登记册，建议创建 docs/requirements/registry.md"
     - 返回空任务列表或建议用户创建新需求

5. 返回匹配结果
   - 任务 ID
   - 任务名称
   - 匹配度
```

**示例**：
```markdown
用户：定时发布功能进度如何？

匹配结果：
- 任务 ID: REQ-20260531-001
- 任务名称：定时发布功能
- 匹配度：0.95

# 无 registry.md 时的提示
⚠️ 未找到需求登记册 (docs/requirements/registry.md)。
建议创建需求登记册以跟踪任务进度。
或告诉我您的新需求，我可以帮您创建 Plan.md 并初始化登记册。
```

---

### 2. 恢复上下文

**输入**：任务 ID、Agent ID

**操作**：
```markdown
1. 读取任务上下文文件
   - 优先路径：`{projectRoot}/docs/requirements/context/{taskId}.md`
   - 兼容路径：`~/.openclaw/agents/{agentId}/task-context/{taskId}.md`

2. 读取 Plan.md（五段闭环结构）
   - 路径：`{projectRoot}/docs/requirements/{req-id}-plan.md`
   - `## 1. 需求&背景&目标`：理解需求范围
   - `## 2. 核心策略`：识别模块边界和架构约束
   - `## 3. 实施步骤`：提取子任务清单和文件变更清单
   - `## 4. 风险及应对策略`：识别风险点
   - `## 5. 验收闭环`：提取验收项用于任务映射

3. 组装上下文
   - 任务基本信息
   - 进度和状态
   - 完成项
   - 进行中项
   - 决策记录
   - 阻塞问题
```

**输出**：
```markdown
任务上下文：
- 任务名称：定时发布功能 - 前端 UI
- 进度：80%
- 状态：in_progress
- 完成项：["数据库设计", "后端 API", "列表页面"]
- 进行中：["时间选择器 (80%)"]
- 决策记录：[使用 BullMQ 延迟队列]
- 阻塞问题：无
```

### 2.1 中断恢复

**场景**：会话中断（长时间无操作、系统重启、网络断开）后重新启动，需要恢复任务上下文。

**流程**：
```markdown
1. 读取任务上下文文件
   → 确定上次执行的 Phase
   → 确定上次的进度和状态

2. 检查项目状态
   → 执行 `git status` 检查工作区状态
   → 执行 `git log --oneline -10` 检查最近提交
   → 执行 `git diff HEAD` 检查未提交变更

3. 判断是否有外部变更
   → 检查是否有他人提交（git log 中的非本人提交）
   → 检查是否有未合并的分支
   → 检查是否有冲突

4. 决定恢复策略
```

**恢复策略**：

| 情况 | 恢复策略 |
|------|----------|
| 无外部变更，上次 Phase 完成 | 从下一 Phase 继续 |
| 无外部变更，上次 Phase 未完成 | 从上次 Phase 继续 |
| 有外部变更，不影响当前任务 | 记录变更，从上次 Phase 继续 |
| 有外部变更，影响当前任务 | 评估影响，决定是否需要重新验证 |
| 有冲突 | 先解决冲突，再决定恢复策略 |

**恢复后操作**：
```markdown
1. 向用户报告恢复情况
   → 上次执行到哪个 Phase
   → 项目是否有外部变更
   → 恢复策略

2. 更新任务上下文
   → 追加时间线："{时间} - 中断恢复，从 Phase X 继续"
   → 如有外部变更，记录到"决策记录"

3. 继续执行
   → 按恢复策略继续执行
```

**示例**：
```markdown
中断恢复报告：
- 任务：定时发布功能 - 前端 UI
- 上次进度：80%（Phase 3 代码实现）
- 项目状态：有 2 个外部提交（他人修改了后端 API）
- 恢复策略：记录外部变更，从 Phase 3 继续
- 注意事项：需要检查后端 API 变更是否影响前端实现
```

---

### 3. 更新状态

**输入**：任务 ID、进度、状态、备注

**操作**：
```markdown
1. 读取当前上下文文件

2. 计算变更
   - 进度变更：50% → 80%
   - 状态变更：in_progress → in_progress

3. 编辑上下文文件
   - 更新进度字段
   - 更新状态字段
   - 更新时间戳

4. 追加时间线
   - 格式：`- {时间} - 进度更新：{old}% → {new}%`

```

**输出**：
```markdown
更新成功：
- 任务 ID: REQ-20260531-001-FE
- 进度变更：50% → 80%
- 更新时间：2026-06-07T12:00:00+08:00
```

---

### 4. 保存上下文

**输入**：任务 ID、对话摘要、决策、阻塞

**操作**：
```markdown
1. 读取当前上下文文件

2. 追加对话摘要
   - 位置："最近对话"部分
   - 格式：`{时间} - {摘要}`

3. 追加决策记录
   - 位置："决策记录"部分
   - 格式：`### 决策 N: {问题}`

4. 追加阻塞问题
   - 位置："阻塞问题"部分
   - 格式：`### 阻塞 N: {描述}`

5. 写入文件
```

---

### 5. 列出任务

**输入**：过滤条件（active/completed/all）

**操作**：
```markdown
1. 读取 registry.md

2. 解析 Markdown 表格
   - 提取需求 ID、名称、状态、优先级

3. 根据过滤条件筛选
   - active: 状态为 active 的任务
   - completed: 状态为 completed 的任务
   - all: 所有任务

4. 格式化输出
```

**输出**：
```markdown
活跃任务清单 (3 个)：
1. REQ-20260531-001 - 定时发布功能 - 75% - in_progress
2. REQ-20260601-001 - 用户登录 - 30% - in_progress
3. REQ-20260602-001 - 数据看板 - 10% - pending
```

---

### 6. 任务编排

**时机**：需求包含 2 个以上子任务，且子任务之间有明确执行顺序或依赖关系。

**前置条件**：用户已确认任务清单。

**流程**：制定子任务清单 → 用户确认 → 顺序执行 → 失败暂停 → 汇总验收 → 更新 registry。

**详细规则**：如需多子任务编排，读取 `references/task-orchestration.md`。

**核心规则**：
- 每次只派发一个子任务，等完成后再派发下一个
- 中间不向用户发送进度更新（避免打扰）
- 仅在以下情况暂停编排，等待用户：
  - 子任务失败且无法自动恢复
  - 需要用户确认方案（如技术选型、接口设计）
  - 发现需求不明确需要澄清
- 全部完成后发送一次性的汇总报告

**单 Agent 自驱动场景**：

小架作为单一 Agent 执行全流程时，按 AGENTS.md 的 Phase 0-6 顺序执行，不需要 `sessions_spawn` 派发子任务。详见 `AGENTS.md ## 自驱动工作流`。

**衔接规则**：
- 阶段失败时，先分析根因（见 `AGENTS.md ## 失败根因分析`），再决定回退方向
- 不要默认回退到阶段 1，可能是当前阶段的配置/环境问题

---

## 📂 文件结构

### 任务上下文文件

默认项目级路径：`docs/requirements/context/{taskId}.md`

个人私有兼容路径：`~/.openclaw/agents/{agentId}/task-context/{taskId}.md`

格式见 `templates/task-context-template.md`。创建或修复上下文文件时读取该模板。

### registry.md

默认路径：`docs/requirements/registry.md`

必备列：需求 ID、名称、状态、优先级、创建时间、模块。

### Registry 更新时机

| 阶段 | 更新内容 | 说明 |
|------|----------|------|
| Phase 0 | Plan 草案生成 | 记录为 `pending` 或等待确认 |
| Phase 1 | 需求登记完成 | 创建 registry/context，状态 `active` |
| Phase 2 | 任务拆解完成 | 写入子任务清单和 Must have 映射 |
| Phase 3 | 开发自测完成 | 写入 dev report 路径，进度建议 50%-60% |
| Phase 4 | 代码审查完成 | 写入 code-review report 路径，无 P0 才继续 |
| Phase 5 | 测试验证完成 | 写入 test report 路径，进度建议 80% |
| Phase 6 | 部署完成 | 写入 deploy report 路径，进度建议 90% |
| Phase 7 | 验收收口 | 写入 acceptance report，状态 `completed` 或 `blocked` |

---

## 🔧 工具使用

### 读取文件

```markdown
使用 `read` 工具：
- registry.md: `{projectRoot}/docs/requirements/registry.md`
- Plan.md: `{projectRoot}/docs/requirements/{req-id}-plan.md`
- 上下文文件：优先 `{projectRoot}/docs/requirements/context/{taskId}.md`，兼容 `~/.openclaw/agents/{agentId}/task-context/{taskId}.md`
- 阶段报告：`{projectRoot}/docs/reports/{development|code-review|tests|deploy|acceptance}/...`
```

### 编辑文件

```markdown
使用 `edit` 工具：
- 更新进度：编辑上下文文件的进度字段
- 追加时间线：在"时间线"部分追加
- 追加对话：在"最近对话"部分追加
```

### 写入文件

```markdown
使用 `write` 工具：
- 创建新任务上下文文件
- 创建新的 registry.md（如不存在）
- 创建阶段报告目录和验收收口报告
```

---

## 📚 参考模板（本 Skill 提供）

**相对路径引用**（从本 Skill 目录）：

| 模板 | 路径 | 用途 |
|------|------|------|
| **任务上下文模板** | `templates/task-context-template.md` | 任务上下文文件格式 |
| **验收收口模板** | `templates/acceptance-report-template.md` | 最终验收报告格式 |
| **降级策略** | `references/fallback-strategy.md` | 无规范时的降级处理 |

---

## ✅ 最佳实践

### 任务识别

1. **先读 registry**：用 `read` 读取 registry.md
2. **关键词匹配**：字符串包含 + 语义扩展
3. **多任务确认**：匹配多个任务时让用户确认

### 状态更新

1. **读取当前状态**：先读上下文文件
2. **计算变更**：记录进度/状态变化
3. **追加时间线**：自动记录变更历史

### 上下文保存

1. **对话结束必保存**：保存对话摘要
2. **决策立即记录**：发现决策就记录
3. **阻塞立即报告**：遇到阻塞就记录

---

## ⛔ 禁止事项

- ❌ 不要只复述任务列表
- ❌ 不要在未读取上下文的情况下更新状态
- ❌ 不要跳过时间线记录
- ❌ 不要忽略阻塞问题的跟踪
- ❌ 不要假设项目路径（从任务上下文读取）

---

*最后更新：2026-06-07 文档驱动版 v4.0.0*
*维护：通用任务管家*


