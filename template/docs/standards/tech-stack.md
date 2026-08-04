# 技术栈规范

## 1. 用途

本文记录模板项目默认技术栈、版本约束、升级策略和例外处理。新项目可以覆盖本文件，但必须说明原因和影响。

## 2. 默认技术栈

| 分类 | 默认技术 | 版本 | 说明 |
| --- | --- | --- | --- |
| 前端框架 | Angular | 22+ | Web/UI 实现 |
| UI 组件库 | Spartan UI | 1+ | `@spartan-ng/brain` 无头基元 + helm 样式层（复制进 `libs/ui/`） |
| CSS | Tailwind CSS | 4+ | 原子化样式与布局 |
| 前端语言 | TypeScript | 6.0+ | 类型安全 |
| 前端状态 | Angular Signals | 当前框架版本 | 组件级/局部状态 |
| 前端表单 | Angular Signal Forms | 当前框架版本 | `@angular/forms/signals`（Angular 22 仍 experimental，需锁版本） |
| 数据表格 | TanStack Table | 8+ | `@tanstack/angular-table` headless 引擎；服务端 `manualPagination`/`manualSorting`，列表分页/排序/筛选状态以 URL query params 为唯一来源 |
| 后端框架 | .NET / ASP.NET Core | 10+ | API 与业务服务 |
| ORM | EF Core | 10+ | 数据访问 |
| 架构 | DDD 分层 | 项目约定 | Api/Application/Domain/Infrastructure |
| 数据库 | PostgreSQL | 15+ | 默认主数据存储，可覆盖 |
| 缓存 | Redis | 7+ | 可选缓存/队列 |
| 容器化 | Docker / Docker Compose | 当前稳定版 | 本地和中小规模部署 |

## 3. 项目可覆盖项

项目可以按实际情况覆盖：

- 数据库、缓存、消息队列。
- UI 组件库或设计系统。
- 部署平台。
- AI 编排或外部服务。

覆盖时必须在需求 Plan 或架构决策中说明：

- 为什么需要覆盖默认栈。
- 对开发、测试、部署、维护的影响。
- 迁移和回滚方案。

## 4. 选型原则

- 优先选择团队熟悉、可维护、生态稳定的方案。
- 优先使用框架和平台内置能力。
- 引入新依赖必须说明收益、成本、风险和替代方案。
- 版本升级必须验证构建、测试、部署兼容性。

## 5. 质量指标

- 构建可重复。
- 本地开发可快速启动。
- 测试命令可自动化执行。
- 部署流程可回滚。
- 文档和规范可被 AI Agent 直接读取。

## 6. 相关文档

- `docs/standards/coding-backend.md`
- `docs/standards/coding-frontend.md`
- `docs/standards/testing.md`
- `docs/deploy/README.md`
