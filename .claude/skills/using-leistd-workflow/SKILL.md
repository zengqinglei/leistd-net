---
name: using-leistd-workflow
description: |
  在本模板仓库（leistd-net）内开始任何需求/开发/审查/测试/部署工作前使用，作为项目工作流的发现入口。

  使用时机：
  (1) 会话开始、清空或压缩后的第一步
  (2) 不确定该用哪个项目 skill 时
  (3) 用户自然语言：「怎么开始」「用哪个 skill」「项目工作流是什么」
metadata:
  openclaw:
    requires: []
    skillKey: "using-leistd-workflow"
user-invocable: true
disable-model-invocation: false
---

# 项目工作流入口 (using-leistd-workflow)

> 本 skill 是 leistd-net 模板仓自身开发的工作流发现入口。动手前先读它，再按意图选择下面的阶段 skill。

## 唯一事实源

所有阶段 skill 启动后**优先读取** `template/docs/standards/agent-workflow.md`（8 阶段状态机、handoff 交接包、人工确认门禁）。本仓库的开发 skill 位于 `template/.claude/skills/`。

## 按意图选择 skill

| 你的意图 | 用哪个 skill |
| --- | --- |
| 有想法/需求，要理成方案 | `requirement-plan` |
| Plan 已确认，要登记/推进/验收 | `task-manager` |
| 按 Plan 写/改代码 | `coding` |
| 审查代码变更（需求对齐+规范+分级） | `code-review` |
| 跑测试、看覆盖率 | `test-runner` |
| 部署/健康检查/回滚 | `deploy` |

## 范围与边界

本套 skill 提供**特定项目治理闭环**（需求登记→开发→审查→测试→部署→验收，含报告沉淀与人工确认）。通用工程方法（头脑风暴、计划撰写、TDD、代码审查方法论等）不在本套范围，按你现有的通用工程实践处理。相邻能力的边界见各 skill description 的"不适用"段。

## 高风险动作必须人工确认

生产部署/回滚、数据迁移/删除、修改认证授权/密钥、引入外部费用——见 `agent-workflow.md` §8。
