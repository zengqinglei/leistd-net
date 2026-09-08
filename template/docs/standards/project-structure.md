# 项目目录

## 1. 项目根

```text
{project-root}/
├── .agents/skills/          # 跨工具项目 Skill
├── backend/                 # .NET 后端
├── frontend/                # Angular 前端
├── deploy/                  # 容器编排配置
├── docs/                    # 长期规范与按需沉淀文档
├── Dockerfile
└── README.md
```

项目根是同时包含 `backend/`、`frontend/` 和 `docs/` 的目录；monorepo 中所有项目相对路径仍以该层为准。

## 2. 后端分层

```text
backend/src/
├── {ProjectName}.Domain/
├── {ProjectName}.Application/
├── {ProjectName}.Infrastructure/
├── {ProjectName}.Client/
├── {ProjectName}.DbMigrator/
└── {ProjectName}.Api/
```

- Domain 保存领域模型和内层抽象。
- Application 编排用例并依赖 Domain，不依赖 Infrastructure。
- Infrastructure 实现持久化和外部适配。
- Client 提供给其他服务消费的强类型 SDK。
- DbMigrator 是独立的一次性数据库迁移入口。
- Api 是组合根，负责注册服务、映射端点和启动应用。

测试项目按实际测试类型放在 `backend/tests/` 或解决方案现有位置，不为目录完整性创建空项目。

## 3. 前端分层

```text
frontend/
├── _mock/
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
- `shared/` 只保存可跨功能复用的展示组件和工具。

## 4. 文档与 Skill

`docs/README.md` 是唯一文档索引。`docs/standards/` 保存长期规则；`docs/modules/`、`docs/requirements/` 和额外部署文档按需创建。

项目协作能力位于 `.agents/skills/leistd-project-workflow/`。`SKILL.md` 定义通用闭环，`references/` 按场景加载细节；不依赖 `CLAUDE.md`、`AGENTS.md` 等工具专属入口，也不携带固定文档模板。

目录和普通 Markdown 文件使用小写 kebab-case；目录入口统一命名为 `README.md`。代码命名遵循对应语言规范。调整现有目录时同步检查项目引用、导入、构建、部署、测试和文档链接。
