# {ProjectName} 文档

本目录只保存需要跨会话、跨成员或长期复用的项目知识。源码、配置、测试、Git 和 CI 已能表达的信息不重复写成文档。

## 文档入口

| 主题 | 文档 | 核对重点 |
| --- | --- | --- |
| 通用编码 | [`standards/coding-common.md`](standards/coding-common.md) | 邻近实现、依赖方向 |
| 后端编码 | [`standards/coding-backend.md`](standards/coding-backend.md) | 同类测试、授权与租户边界 |
<!--#if (SpaFrontend)-->
| 前端编码 | [`standards/coding-frontend.md`](standards/coding-frontend.md) | lint、测试、构建；交互变化看浏览器 |
<!--#endif-->
| API 契约 | [`standards/api.md`](standards/api.md) | 调用方、错误和兼容性 |
| 服务间调用 | [`standards/service-invocation.md`](standards/service-invocation.md) | 调用链、失败路径与配置 |
| 测试 | [`standards/testing.md`](standards/testing.md) | 复现、回归与真实依赖语义 |
| 技术栈与目录 | [`standards/tech-stack.md`](standards/tech-stack.md)、[`standards/project-structure.md`](standards/project-structure.md) | 当前版本与实际目录 |
<!--#if (SpaFrontend)-->
| UI 设计 | [`standards/ui-design.md`](standards/ui-design.md) | 现有设计系统与交互 |
<!--#endif-->
| 部署 | [`deploy/README.md`](deploy/README.md) 与项目根 `deploy/` | 产物、迁移、健康与回滚 |

业务开发和环境交付由项目 Skill [leistd-project-workflow](../.agents/skills/leistd-project-workflow/SKILL.md) 按任务处理，并读取本索引与必要章节。

## 按需文档

- `requirements/`：长期需求决策和验收边界。
- `modules/`：稳定模块边界、模型和对外契约。
- `deploy/`：环境、发布和运维事实，不记录密钥。

先参考已有同类实现，再按任务读取相关规范。只在产生已确认、需复用的信息时创建目录和文档；优先更新最新同类文件，并在本索引登记入口。一个事实只维护一处，其他位置使用链接。

<!--#if (SpaFrontend)-->
[浏览器认证](standards/api.md#浏览器认证)：官方协议处理器、服务端票据、浏览器会话与部署契约。
<!--#endif-->
