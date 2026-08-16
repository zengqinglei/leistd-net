# 仓库架构

本目录保存跨 `framework`、`template`、Skill 和文档体系长期有效的架构原则。这里的内容是当前约束，不是某次改造的过程记录。

| 文档 | 作用 |
| --- | --- |
| [设计原则](./design-principles.md) | 三层边界、文档、Skill 与验证的长期约束 |
| [三层交付与 AI 协作](./collaboration-scenarios.md) | Framework、Skills、Template 的场景、步骤与文档读写链路 |
| [前端组件库选型](./frontend-ui-library.md) | 模板前端采用 Spartan UI（替代 PrimeNG）的决定、依据、影响与备选 |
| [隔离与授权场景](./isolation-and-authorization-scenarios.md) | 多租户、功能权限、数据范围的适用边界、组合顺序与各自的验证策略 |

具体框架维护规范位于 [`docs/framework/`](../framework/README.md)，模板维护规范位于 [`docs/template/`](../template/README.md)。一次性诊断和实施计划分别进入 `docs/assessments/` 与 `docs/plans/`。
