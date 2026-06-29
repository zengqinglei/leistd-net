# 需求登记册

> 用于 AI Agent 和团队跟踪需求状态。状态枚举和阶段定义以 `docs/standards/agent-workflow.md` 为准。

## 活跃需求

| 需求 ID | 名称 | 状态 | 优先级 | Phase | 进度 | 负责人 | 模块 | Plan | Context | 最近更新 | 备注 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `{REQ-ID}` | `{需求名称}` | planned | P1 | 0 | 0% | `{Owner}` | `{module}` | `docs/requirements/{req-id}-plan.md` | `docs/requirements/context/{req-id}.md` | `{YYYY-MM-DD}` | `{备注}` |

## 已完成需求

| 需求 ID | 名称 | 完成时间 | 验收人 | 验收报告 | 关联文档 |
| --- | --- | --- | --- | --- | --- |

## 阻塞需求

| 需求 ID | 名称 | Phase | 阻塞原因 | 责任方 | 所需动作 | 下一步 |
| --- | --- | --- | --- | --- | --- | --- |

## 状态说明

- candidate：候选需求，尚未形成 Plan。
- planned：已形成 Plan，等待确认或排期。
- in-progress：实施中。
- blocked：阻塞。
- review：待审查或验收。
- done：完成。
- archived：归档。
