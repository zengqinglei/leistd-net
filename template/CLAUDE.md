# 本项目的 AI 协作约定（动手前先读我）

> 本文件是 AI 的入口指路牌，只做导航、不复述规范正文。**改任何代码前先看这里，再按图索骥读对应规范**——不要凭记忆自创风格或臆造 API。

## 动手前两步（无论走不走 skill）

1. **优先走工作流**：按意图选用 `.claude/skills/` 的阶段 skill——「改代码」= `coding`、「审查/review」= `code-review`、「跑测试」= `test-runner`、「理需求/出方案」= `requirement-plan`、「登记/推进/验收」= `task-manager`、「部署」= `deploy`。skill 会带你读该读的规范、按阶段留证据。
2. **改代码必读工程铁律**：即使是「随手改一下」，动手前也要读 `docs/standards/code-standard/common-develop.md` §5「工程铁律」——那是踩坑写死、联调期才炸的强约定（枚举大小写边界、删除幂等、依赖倒置、验证只在入口、连锁字段改全等）。**这一步不因改动"简单"而跳过。**

## 规范索引（按需展开，勿一次性全量堆读）

| 我要… | 读这份 |
| --- | --- |
| 了解工作流 / 阶段 / 门禁 / 项目根定位 | `docs/standards/agent-workflow.md` |
| 定 API 契约 / 路由 / 分页 / 响应格式 | `docs/standards/api-standard.md`（契约单一权威） |
| 写后端（.NET / EF Core / DDD） | `docs/standards/code-standard/backend-develop.md` |
| 写前端（Angular / PrimeNG） | `docs/standards/code-standard/frontend-develop.md` |
| 技术栈无关的通用工程铁律 | `docs/standards/code-standard/common-develop.md` §5 |
| 用 `Leistd.*` 框架组件的真实 API | `backend/CLAUDE.md`（勿臆造框架 API） |

> monorepo 布局（本项目在 `apps/<name>/` 下）时，以上 `docs/...` 相对**项目根**解析——项目根 = 含 `docs/`+`backend/`+`frontend/` 的那一层，详见 `agent-workflow.md §2.0`。

## 红线

- 生产部署 / 删数据 / 改密钥等高风险动作需人工显式确认。
- 不硬编码密钥、Token、真实客户数据。
