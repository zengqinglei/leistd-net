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

## 工作流

1. 检查分支、工作区和用户已有改动，确认当前变更基线。
2. 读取相关稳定规范、邻近实现和最新同类文档，不把历史 assessment 或 plan 当作当前规则。
3. 确认每项事实的唯一维护位置，按交付面加载并应用对应专项 Skill。
4. 实施聚焦变更，并分别完成构建、测试、文档检查、打包或模板生成验证。
5. 汇总各交付面的真实结果、未验证项和残余风险；只有长期共享信息才更新稳定文档。

仓库稳定规则、评估和实施计划的归属及命名以 `docs/README.md` 为准。确认信息确有跨会话复用价值后，没有同类文档时按该入口主动创建权威文档，只写已验证且必要的信息；不创建固定报告或规范模板，只有归类或关键决策不明确时才询问用户。

发布正式包、生产操作、真实数据修改、密钥变更和破坏性 Git 操作必须先获得用户明确确认。
