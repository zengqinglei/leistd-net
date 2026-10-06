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

项目根包含 `backend/` 和 `docs/`，启用交互前端时同时包含 `frontend/`；monorepo 中所有项目相对路径仍以该层为准。

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

- Domain 保存领域模型和内层抽象。
- Application 编排用例并依赖 Domain，不依赖 Infrastructure。
- Infrastructure 实现持久化和外部适配。
- Client 提供给其他服务消费的强类型 SDK。
- DbMigrator 是独立的一次性数据库迁移入口。
- Api 是组合根，组合各层与组件的注册入口、映射端点并启动应用。

各层目录与职责见 [后端开发规范](./coding-backend.md#2-分层与目录)。

测试项目按实际测试类型放在 `backend/tests/` 或解决方案现有位置，不为目录完整性创建空项目。

<!--#if (SpaFrontend)-->
## 3. 前端分层

```text
frontend/
├── _mock/
├── libs/ui/                 # Spartan helm 组件（CLI 复制进来的自有代码）
├── public/
├── src/app/
│   ├── core/
│   ├── features/
│   ├── layout/
│   └── shared/
└── package.json
```

- `core/` 保存单例服务、认证和全局基础设施。
- `features/` 按业务能力组织页面和局部服务。
- `layout/` 保存应用壳与导航。
- `shared/` 保存无状态的展示组件、工具与跨功能契约，不依赖 `core`。

目录职责与依赖方向见 [前端开发规范](./coding-frontend.md#2-目录与依赖方向)。

<!--#endif-->

## 4. 文档与 Skill

`docs/README.md` 是唯一文档索引。`docs/standards/` 保存长期规则；`docs/modules/`、`docs/requirements/` 和额外部署文档按需创建。

业务开发与环境交付由 `.agents/skills/leistd-project-workflow/` 按场景加载对应 reference，不携带固定文档模板。根目录 `AGENTS.md`（`CLAUDE.md` 引用它）只指向该 Skill 与 `docs/README.md`，不承载规则。

目录和普通 Markdown 文件使用小写 kebab-case（前端多语言 scope 目录名跟随 scope 名）；目录入口统一命名为 `README.md`。代码命名遵循对应语言规范。调整现有目录时同步检查项目引用、导入、构建、部署、测试和文档链接。
