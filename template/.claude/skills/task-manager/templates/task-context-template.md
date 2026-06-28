# Task Context: {req-id}

> 用途：任务恢复、阶段推进、证据追踪和验收收口。优先保存在项目内可追溯路径。

## 基本信息

- 需求 ID：{req-id}
- 任务名称：{taskName}
- 状态：planned
- Phase：0
- Phase 名称：requirement-plan
- 进度：0%
- 项目路径：{projectRoot}
- Plan 文档：docs/requirements/{req-id}-plan.md
- 最后更新：{iso-datetime}

## 最近交接包

```yaml
handoff:
  reqId: {req-id}
  phase: 0
  phaseName: requirement-plan
  gateStatus: needs-confirmation
  nextRecommendedSkill: task-manager
  userConfirmationRequired: true
  artifacts:
    - docs/requirements/{req-id}-plan.md
  evidence: []
  blockers: []
  assumptions: []
```

## 阶段报告

| Phase | 阶段 | 报告 | 状态 | 证据摘要 |
| --- | --- | --- | --- | --- |
| 3 | 开发自测 | docs/reports/development/{req-id}-dev-report.md | planned | 待填写 |
| 4 | 代码审查 | docs/reports/code-review/{req-id}-code-review.md | planned | 待填写 |
| 5 | 测试验证 | docs/reports/tests/{req-id}-test-report.md | planned | 待填写 |
| 6 | 部署发布 | docs/reports/deploy/{req-id}-deploy-report.md | planned | 待填写 |
| 7 | 验收收口 | docs/reports/acceptance/{req-id}-acceptance.md | planned | 待填写 |

## 验收映射

| Must have | 对应子任务/步骤 | 验证方式 | 证据 | 状态 |
| --- | --- | --- | --- | --- |
| 待填写 | 待填写 | 测试/审查/部署/人工验收 | 待填写 | planned |

## 子任务

| 子任务 | 依赖 | 状态 | 负责人 | 说明 |
| --- | --- | --- | --- | --- |
| 待填写 | 无 | planned | Agent | 待填写 |

## 决策记录

| 时间 | 决策 | 原因 | 决策者 |
| --- | --- | --- | --- |
| {iso-datetime} | 创建任务上下文 | Plan 已生成 | Agent |

## 阻塞问题

| 时间 | 类型 | 描述 | 责任方 | 所需动作 | 状态 |
| --- | --- | --- | --- | --- | --- |

## 时间线

- {iso-datetime}：创建任务上下文。

## 最近对话摘要

- 待填写
