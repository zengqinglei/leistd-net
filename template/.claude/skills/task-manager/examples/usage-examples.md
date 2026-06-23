# 任务管家 Skill 使用示例

---

## 场景 1: 用户查询任务进度

**用户消息**：定时发布功能进度如何？

**Agent 处理流程**：
1. 调用 `identifyTask()` 识别任务
   - 读取 `docs/requirements/registry.md`
   - 关键词匹配："定时发布" → REQ-20260531-001
   - 匹配度：0.95

2. 调用 `restoreContext()` 恢复上下文
   - 读取 `~/.openclaw/agents/architect/task-context/req-20260531-001.md`
   - 读取 `docs/requirements/req-20260531-001-plan.md`
   - 读取 `docs/modules/content-management/status.md`

3. 回复用户：
```
📊 定时发布功能进度：75% 🔄

已完成：
✅ 数据库设计
✅ 后端 API
✅ 列表页面

进行中：
🔄 前端 UI (80%)

阻塞：无
```

---

## 场景 2: 开发助手更新进度

**触发**：开发助手完成前端 UI 80%

**Agent 处理流程**：
1. 调用 `updateStatus()`:
```json
{
  "taskId": "REQ-20260531-001-FE",
  "progress": 80,
  "status": "in_progress",
  "updatedBy": "architect",
  "completedItems": ["列表页面", "创建表单", "时间选择器"],
  "note": "前端 UI 发布预览功能进行中"
}
```

2. 调用 `appendTimeline()`:
```json
{
  "taskId": "REQ-20260531-001-FE",
  "event": "进度更新",
  "agent": "architect",
  "details": "前端 UI 80%"
}
```

3. 调用 `saveContext()`:
```json
{
  "taskId": "REQ-20260531-001-FE",
  "agentId": "architect",
  "conversationSummary": "前端 UI 完成 80%，进入发布预览功能开发"
}
```

---

## 场景 3: 报告阻塞问题

**触发**：开发助手发现小红书 API 需要企业账号授权

**Agent 处理流程**：
1. 调用 `reportBlocker()`:
```json
{
  "taskId": "REQ-20260531-001-FE",
  "type": "resource",
  "description": "小红书 API 需要企业账号授权，当前测试账号权限不足",
  "reportedBy": "architect"
}
```

2. 更新任务状态为 `blocked`:
```json
{
  "taskId": "REQ-20260531-001-FE",
  "status": "blocked",
  "updatedBy": "architect",
  "note": "等待企业账号授权"
}
```

3. 回复用户（飞书通知）：
```
⚠️ 阻塞报告

任务：定时发布功能 - 前端 UI
问题：小红书 API 需要企业账号授权
影响：无法继续开发发布功能
建议：联系管理员申请企业账号权限
```

---

## 场景 4: 会话重置后恢复上下文

**触发**：凌晨 4 点会话重置，Agent 重新启动

**Agent 处理流程**：
1. 启动时调用 `listTasks({ filter: "active" })`:
   - 获取 3 个活跃任务

2. 对每个活跃任务调用 `restoreContext()`:
```json
{
  "taskId": "REQ-20260531-001-FE",
  "agentId": "architect"
}
```

3. 恢复内容：
   - 任务基本信息（进度 80%，状态 in_progress）
   - Plan.md 全文
   - 模块 status.md
   - 最近 5 条对话转录
   - 决策记录（使用 BullMQ）
   - 阻塞问题（API 权限）
   - 时间线事件
   - Worktree 路径

4. Agent 准备就绪，可继续开发

---

## 场景 5: 记录重要决策

**触发**：团队决定使用 BullMQ 作为定时任务调度方案

**Agent 处理流程**：
1. 调用 `recordDecision()`:
```json
{
  "taskId": "REQ-20260531-001",
  "question": "定时任务调度方案选择",
  "options": ["RabbitMQ", "数据库轮询", "Redis cron", "BullMQ"],
  "decision": "BullMQ",
  "reason": "基于 Node.js，与现有技术栈一致，支持重试和监控，社区活跃",
  "decidedBy": "boss",
  "timestamp": "2026-06-06T15:00:00+08:00"
}
```

2. 追加到上下文文件的"决策记录"部分

3. 追加时间线事件：
```json
{
  "taskId": "REQ-20260531-001",
  "event": "技术决策",
  "agent": "baiying-architect",
  "details": "确定使用 BullMQ 作为定时任务调度方案"
}
```

---

## 场景 6: 列出所有活跃任务

**用户消息**：/task-manager listTasks

**Agent 处理流程**：
1. 调用 `listTasks({ filter: "active" })`

2. 读取 `docs/requirements/registry.md`

3. 解析表格，筛选 `status=active` 的任务

4. 回复用户：
```
📋 活跃任务清单 (3 个)

1. REQ-20260531-001 - 定时发布功能
   进度：75% | 状态：in_progress | 模块：内容管理
   最后更新：2026-06-06 12:00

2. REQ-20260601-001 - 用户登录
   进度：30% | 状态：in_progress | 模块：平台账号
   最后更新：2026-06-06 10:30

3. REQ-20260602-001 - 数据看板
   进度：10% | 状态：pending | 模块：数据分析
   最后更新：2026-06-06 09:00
```

---

## 场景 7: 解决阻塞问题

**触发**：管理员完成企业账号授权

**Agent 处理流程**：
1. 调用 `reportBlocker()` 解决阻塞:
```json
{
  "taskId": "REQ-20260531-001-FE",
  "blockerId": "BLK-001",
  "resolved": true,
  "resolution": "企业账号授权已完成，API 权限已生效",
  "resolvedBy": "boss"
}
```

2. 恢复任务状态为 `in_progress`:
```json
{
  "taskId": "REQ-20260531-001-FE",
  "status": "in_progress",
  "updatedBy": "architect",
  "note": "阻塞已解决，继续开发"
}
```

3. 追加时间线事件：
```json
{
  "taskId": "REQ-20260531-001-FE",
  "event": "阻塞解决",
  "agent": "architect",
  "details": "企业账号授权完成，继续开发"
}
```

---

## 场景 8: 保存对话上下文

**触发**：对话结束（用户 5 分钟无新消息）

**Agent 处理流程**：
1. 调用 `saveContext()`:
```json
{
  "taskId": "REQ-20260531-001-FE",
  "agentId": "architect",
  "conversationSummary": "讨论了时间选择器实现方案，决定使用 Element Plus 的 DateTimePicker 组件",
  "decisions": [
    {
      "question": "时间选择器组件选型",
      "decision": "Element Plus DateTimePicker",
      "reason": "与现有 UI 框架一致，功能完整"
    }
  ],
  "completedItems": ["时间选择器原型"],
  "inProgressItems": ["时间选择器与表单集成"]
}
```

2. 追加到上下文文件的"最近对话"部分

3. 更新"进行中"部分

---

## 文件变更示例

### 更新前：task-context/REQ-20260531-001-FE.md

```markdown
## 基本信息
- **进度**: 50%
- **状态**: in_progress

## 进行中
- 🔄 列表页面 (50%)
```

### 更新后

```markdown
## 基本信息
- **进度**: 80%
- **状态**: in_progress
- **最后更新**: 2026-06-06T12:00:00+08:00
- **更新者**: architect

## 完成项
- ✅ 列表页面
- ✅ 创建表单
- ✅ 时间选择器

## 进行中
- 🔄 发布预览功能 (20%)

## 时间线
- 2026-06-06 12:00 - 进度 80%

## 最近对话
1. 2026-06-06 12:00 - 报告进度 80%
```

---

*最后更新：2026-06-06*
