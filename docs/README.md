# 仓库元工作文档（repo meta-work）

> 本目录（仓库根 `docs/`）存放**对 leistd-net 仓库自身**的规划、评估与设计产物——即"如何改进这个框架/模板/工具链"的文档。与另两棵文档树职责不同：

| 文档树 | 职责 | 受众 | 是否随交付分发 |
| --- | --- | --- | --- |
| `docs/`（本目录） | 仓库元工作：架构评估、优化设计、实现计划 | 仓库维护者 | 否（不进包、不进模板生成） |
| `template/docs/` | 生成应用的工程治理规范（工作流/标准/需求/报告） | 下游 app 团队 + 其 AI | 随 `dotnet new` 生成 |
| `framework/docs/` | 框架组件用法（`Leistd.*` 包） | 框架仓开发者 + 包消费者 | 随 NuGet 包分发 |

## 目录

| 子目录 | 内容 |
| --- | --- |
| `assessments/` | 架构评估与设计方案（现状诊断 → 问题 → 目标架构 → 迁移路径） |
| `plans/` | 实现计划（可执行任务分解） |
| `specs/` | 设计规格（如有；设计方案产出的文档） |

## 命名与生命周期约定

- 文件名：`YYYY-MM-DD-<主题-kebab-case>.md`（日期前缀便于排序与追溯）。
- 评估/设计入 `assessments/`；实现计划入 `plans/`；规格入 `specs/`。
- 生命周期：评估/计划是**某一次改进的过程产物**，完成后保留作历史追溯，不作长期规范；长期规则应沉淀到 `template/docs/standards/` 或 `framework/docs/`，不留在本目录。
- 不重复维护规则：本目录不复述 `template/docs/standards/document-classification.md` / `document-naming.md` 的分类命名规则——那两份治理的是**生成应用**的文档树;本约定只治理仓库元工作树。

## 当前文档

- **`assessments/2026-07-10-design-principles.md` — 文档与 skill 设计原则（权威清单，单一出处）。** 三层分层 / 模板规范 / 三套 skill / 方法论的所有原则以此为准。
- `assessments/2026-07-10-manifest-driven-doc-discovery.md` — 文档发现与防漂移的架构方案（「文档即索引」，否决 index.json；含迁移路径 P0–P4）。
- `assessments/2026-07-08-docs-skills-ia-redesign.md` — 文档与 skills 信息架构重设计。
- `assessments/2026-07-08-skills-docs-architecture-design.md` — skills 与文档体系的分层架构与引用关系设计（含实施计划 P1–P8）。
- `assessments/2026-07-08-skills-components-tests-assessment.md` — docs 规范 / 组件知识交付 / 框架单测三项评估。
- `plans/2026-07-08-p1-framework-docs-backfill.md` — P1 框架文档补齐实现计划。
- `plans/2026-07-06-skills-superpowers-alignment.md` — skills 对齐最佳实践实现计划。
