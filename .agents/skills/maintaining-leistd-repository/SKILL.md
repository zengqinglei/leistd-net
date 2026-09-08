---
name: maintaining-leistd-repository
description: 在 leistd-net 仓库中处理跨 framework、template、skills、docs、CI、版本或发布流程的变更时使用，也适用于“梳理仓库架构”“同步框架与模板”“调整 Skill 或文档体系”等请求。负责统筹多个交付面及其验证证据；单独修改 framework 或 template 应使用对应专项 Skill，不用于生成后的业务项目开发或仅查询 Leistd NuGet 用法。
---

# 维护 leistd-net 仓库

## 先确定交付面

读取 `docs/architecture/collaboration-scenarios.md`，再按变更范围选择：

| 范围 | 同时使用 | 主要事实 |
| --- | --- | --- |
| `framework/` | `developing-leistd-framework` | 源码、测试、`framework/docs/`、`docs/framework/` |
| `template/` | `developing-leistd-template` | `template.json`、模板源码、实际生成结果 |
| 任一 Skill | 官方 `skill-creator` | `SKILL.md`、所属交付面的当前事实 |
| CI、发布或根文档 | 本 Skill | workflow、脚本、`docs/` 稳定入口 |

跨多个交付面时使用一个实施计划，但分别完成各层验证。

只有一个交付面受影响时直接使用对应专项 Skill，不额外加载本 Skill。

## 方案与实施计划

方案、计划和稳定规范的归属以 `docs/README.md` 为准：

| 信息 | 位置 |
| --- | --- |
| 现状诊断、候选比较和迁移建议 | `docs/assessments/YYYY-MM-DD-<topic>.md`，**选定方案后删除** |
| 已选方案的可执行任务、顺序和验收 | `docs/plans/YYYY-MM-DD-<topic>.md`，**任务全部完成后删除** |
| 长期有效的仓库、Framework 或 Template 维护规则 | `docs/architecture/`、`docs/framework/`、`docs/template/` |
| 确有长期价值的重大跨层验证结果 | `docs/reports/YYYY-MM-DD-<topic>.md` |

`assessments/` 与 `plans/` 只放在途工作：有效结论先上收到稳定文档，原文随即删除，Git 是历史的唯一归档。聊天中的临时分析不默认落盘；用户要求持久化或信息需要跨会话执行时，先查同主题最新文档，再按上表更新或创建。跨 Framework、Template 和 Skill 的选型或迁移只维护一份根仓库计划，不在每个交付面复制。

`framework/docs/` 和 `template/` 都是对外分发内容，不得写入 leistd-net 的方案比较、实施计划、任务状态、分支记录或仓库内部验证过程。实现完成后只把已经成立的公共契约和生成项目事实同步到对应分发文档。

## 工作流

1. 检查分支、工作区和用户已有改动，确认当前变更基线。
2. 读取相关稳定规范、邻近实现和最新同类文档，不把历史 assessment 或 plan 当作当前规则。
3. 确认每项事实的唯一维护位置，按交付面加载并应用对应专项 Skill。
4. 实施聚焦变更，并分别完成构建、测试、文档检查、打包或模板生成验证。
5. 汇总各交付面的真实结果、未验证项和残余风险；只有长期共享信息才更新稳定文档。

没有同类文档且信息需跨会话复用时，按 `docs/README.md` 创建最小权威文档；归属或关键决策不明确时询问用户。

发布正式包、生产操作、真实数据修改、密钥变更和破坏性 Git 操作必须先获得用户明确确认。
