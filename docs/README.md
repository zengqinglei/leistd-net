# 仓库维护文档

> 根 `docs/` 保存 leistd-net 仓库自身的长期架构规范与工程变更记录，不随 NuGet 包或 `dotnet new` 生成项目分发。

| 文档树 | 职责 | 受众 | 分发方式 |
| --- | --- | --- | --- |
| `docs/` | 仓库架构、框架/模板维护规范、评估与计划 | 仓库维护者 | 不分发 |
| `framework/docs/` | Leistd.* 公共 API、注册与运行时语义 | NuGet 使用者 | 随对应包分发 |
| `template/docs/` | 生成应用的工程规范与按需沉淀的项目文档 | 下游项目团队与 AI | 随模板生成 |

## 稳定文档

| 子目录 | 内容 |
| --- | --- |
| `architecture/` | 跨框架、模板、Skill 和文档体系的长期设计原则 |
| `framework/` | 框架开发、版本与发布规范 |
| `template/` | 模板维护、条件生成和验证规范 |

## 在途文档

| 子目录 | 内容 | 生命周期 |
| --- | --- | --- |
| `assessments/` | 现状诊断与方案比较 | **选定方案后即删除**，有效结论上收到稳定文档 |
| `plans/` | 已选方案的可执行任务分解 | **全部任务完成后即删除**，有效结论上收到稳定文档 |
| `reports/` | 重大跨层改造的专项验证报告 | 有真实需要时按需创建 |

用 `YYYY-MM-DD-<topic>.md` 命名。评估与计划只保留在途工作，完成后将有效结论写入稳定文档并删除原文；历史由 Git 保留。
专项报告仅按实际需要保留。目录按需创建，不预建空目录或无内容的分类。

## 入口

- [总体设计原则](./architecture/design-principles.md)
- [三层交付与 AI 协作](./architecture/collaboration-scenarios.md)
- [框架开发文档](./framework/README.md)
- [模板开发文档](./template/README.md)
