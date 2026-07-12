# {ProjectName} 文档

本目录只保存需要跨会话、跨成员或长期复用的项目知识。源码、配置、测试、Git 和 CI 已能表达的信息不重复写成文档。

## 文档入口

| 主题 | 文档 |
| --- | --- |
| 通用编码 | [`standards/coding-common.md`](standards/coding-common.md) |
| 后端编码 | [`standards/coding-backend.md`](standards/coding-backend.md) |
| 前端编码 | [`standards/coding-frontend.md`](standards/coding-frontend.md) |
| API 契约 | [`standards/api.md`](standards/api.md) |
| 测试 | [`standards/testing.md`](standards/testing.md) |
| 技术栈与目录 | [`standards/tech-stack.md`](standards/tech-stack.md)、[`standards/project-structure.md`](standards/project-structure.md) |
| UI 设计 | [`standards/ui-design.md`](standards/ui-design.md) |
| 部署 | [`deploy/README.md`](deploy/README.md) 与项目根 `deploy/` |

AI 协作流程由项目 Skill [leistd-project-workflow](../.agents/skills/leistd-project-workflow/SKILL.md) 提供。它按用户意图读取本索引和必要文档，不要求工具专属入口。

## 按需文档

- `requirements/`：长期需求决策和验收边界。
- `modules/`：稳定模块边界、模型和对外契约。
- `deploy/`：环境、发布和运维事实，不记录密钥。

只在产生对应长期信息时创建目录和文档。优先更新最新同类文件；一个事实只维护一处，其他位置使用链接。
