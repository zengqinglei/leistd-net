# 项目目录规范

## 1. 顶层结构

```text
{project-root}/
├── backend/                  # 后端应用，可按项目调整
├── frontend/                 # 前端应用，可按项目调整
├── docs/                     # 项目文档
├── tests/                    # 跨端或端到端测试，可选
├── scripts/                  # 本地脚本和自动化工具
├── template/                 # 模板项目场景可选
└── README.md
```

## 2. 应用代码目录

### 2.1 前端目录

```text
frontend/
├── _mock/
│   ├── api/
│   ├── data/
│   └── index.ts
├── src/
│   ├── app/
│   │   ├── core/
│   │   ├── features/
│   │   ├── layout/
│   │   └── shared/
│   ├── assets/
│   └── environments/
└── package.json
```

### 2.2 后端目录

```text
backend/
├── src/
│   ├── {ProjectName}.Api/
│   ├── {ProjectName}.Application/
│   ├── {ProjectName}.Domain/
│   └── {ProjectName}.Infrastructure/
├── tests/
│   ├── UnitTests/
│   └── IntegrationTests/
└── {ProjectName}.sln
```

## 3. 文档目录

```text
docs/
├── quick-start/
├── guides/
│   ├── create-app/
│   └── frontend/
├── standards/
│   └── code-standard/
├── requirements/
│   └── context/
├── modules/
├── deploy/
├── reports/
│   ├── development/
│   ├── code-review/
│   ├── tests/
│   ├── deploy/
│   └── acceptance/
└── reference/
```

## 4. Skill 目录

如项目内置 Claude/Codex skill，可使用：

```text
.claude/
└── skills/
    └── {skill-name}/
        ├── SKILL.md
        ├── templates/
        ├── references/
        └── examples/
```

Skill 应优先读取 `docs/standards/`，不要把长期规范散落在 skill 内部模板中。每个 Skill 应按职责独立闭环，不依赖非标准共享目录。

## 5. 模块文档结构

```text
docs/modules/{module-name}/
├── design.md
├── api.md
├── plan.md
├── test-plan.md
└── status.md
```

## 6. 命名规则

命名规范以 `document-naming.md` 为单一权威来源（目录/文件 kebab-case、需求 `req-yyyymmdd-nnn-plan.md`、阶段报告 `{req-id}-{report-type}.md`、禁止项等），此处不重复维护。

## 7. AI 协作要求

AI 修改目录结构前必须说明：

- 为什么需要调整。
- 影响哪些引用、构建、部署和测试。
- 如何迁移旧路径。
- 如何回滚。
