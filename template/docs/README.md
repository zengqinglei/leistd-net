# {ProjectName} 文档中心

> 本目录是项目的规范、需求、模块、测试、部署与审查文档入口，供人类团队与 AI Agent 共同读取和维护。

## 1. 文档目标

- 为 AI 提供稳定上下文，减少重复澄清和错误实现。
- 为人类提供决策依据，确保需求、设计、实现、测试、部署可追溯。
- 将项目规范沉淀为可复用模板，而不是记录某个具体项目的临时过程。
- 明确高风险动作的人类确认点，避免 AI 越权修改生产、密钥、权限和数据。

## 2. 推荐阅读路径

| 场景 | 必读文档 |
| --- | --- |
| 第一次接入项目 | `quick-start/quick-start.md`、`quick-start/ai-native-model.md` |
| 提新需求 | `standards/agent-workflow.md`、`requirements/req-template-plan.md` |
| 开发实现 | `standards/code-standard/`、`standards/project-structure.md` |
| 创建前端项目 | `guides/create-app/frontend-create.md` |
| 前端错误处理/Mock | `guides/frontend/global-error-handling.md`、`guides/frontend/mock-development.md` |
| 设计 API | `standards/api-standard.md`、`modules/_template/api.md` |
| 编写模块文档 | `modules/README.md`、`modules/_template/` |
| 执行测试 | `standards/test.md` |
| 部署发布 | `deploy/README.md`、`deploy/server-config.md` |
| 代码审查 | `reviews/README.md`、`standards/code-standard/` |

## 3. 目录结构

```text
docs/
├── quick-start/              # AI 协作快速开始
├── guides/                   # 创建项目、前端工程化等操作指南
├── standards/                # 项目级规范
│   └── code-standard/        # 编码规范
├── requirements/             # 需求登记册与需求 Plan
├── modules/                  # 模块设计、API、计划、测试与状态
├── deploy/                   # 部署规范与环境配置
├── reviews/                  # 代码审查报告输出目录
└── reference/                # 可选参考资料索引
```

## 4. 协作原则

1. **人定目标，AI 出方案**：人类提供业务目标、边界和优先级，AI 负责澄清、拆解和生成 Plan。
2. **先确认，后实施**：涉及架构、数据库、权限、部署、外部接口的变更必须先确认 Plan。
3. **小步交付**：每个需求应有清晰范围、验收标准、非目标和回滚方案。
4. **规范优先**：AI 执行前应读取 `docs/standards/` 与相关模块文档。
5. **证据闭环**：实现完成后应留下测试结果、审查结论或未验证风险。

## 5. 文档维护规则

- 新需求写入 `requirements/registry.md`，再创建 `requirements/{req-id}-plan.md`。
- 新模块从 `modules/_template/` 复制文档结构到 `modules/{module-name}/`。
- 代码审查报告写入 `reviews/{feature}-code-review.md`。
- 部署相关变更同步更新 `deploy/`。
- 规范变化优先修改 `standards/`，不要在需求文档中重复定义长期规则。
