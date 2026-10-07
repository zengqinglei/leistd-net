# 项目目录

## 1. 项目根

```text
{project-root}/
├── .agents/skills/          # 跨工具项目 Skill
├── backend/                 # .NET 后端
<!--#if (SpaFrontend)-->
├── frontend/                # Angular 前端
<!--#endif-->
├── deploy/                  # 容器编排配置
├── docs/                    # 长期规范与按需沉淀文档
├── scripts/                 # 静态检查脚本（错误码闸门总在，其余按启用的功能生成）
├── AGENTS.md                # AI 协作入口指针（CLAUDE.md 引用它）
├── CLAUDE.md
├── Dockerfile
└── README.md
```

<!--#if (SpaFrontend)-->
项目根包含 `backend/`、`frontend/` 和 `docs/`；monorepo 中所有项目相对路径仍以该层为准。
<!--#else-->
项目根包含 `backend/` 和 `docs/`；monorepo 中所有项目相对路径仍以该层为准。
<!--#endif-->

## 2. 后端分层

```text
backend/src/
├── CompanyName.ProjectName.Domain/
├── CompanyName.ProjectName.Application/
├── CompanyName.ProjectName.Infrastructure/
├── CompanyName.ProjectName.Client/
├── CompanyName.ProjectName.DbMigrator/
└── CompanyName.ProjectName.Api/
```

Client 是提供给其他服务消费的强类型 SDK，DbMigrator 是独立的一次性数据库迁移入口；其余各层的职责、依赖方向与目录见 [后端开发规范 §2](./coding-backend.md#2-分层与目录)。

测试项目按实际测试类型放在 `backend/tests/` 或解决方案现有位置，不为目录完整性创建空项目。

<!--#if (SpaFrontend)-->
## 3. 前端分层

前端目录树、目录职责与依赖方向见 [前端开发规范 §2](./coding-frontend.md#2-目录与依赖方向)。

<!--#endif-->

## 4. 文档与 Skill

`docs/README.md` 是唯一文档索引。`docs/standards/` 保存长期规则；`docs/modules/`、`docs/requirements/` 和额外部署文档按需创建。

业务开发与环境交付由 `.agents/skills/leistd-project-workflow/` 按场景加载对应 reference，不携带固定文档模板。根目录 `AGENTS.md`（`CLAUDE.md` 引用它）只指向该 Skill 与 `docs/README.md`，不承载规则。

目录和普通 Markdown 文件使用小写 kebab-case（前端多语言 scope 目录名跟随 scope 名）；目录入口统一命名为 `README.md`。代码命名遵循对应语言规范。调整现有目录时同步检查项目引用、导入、构建、部署、测试和文档链接。
