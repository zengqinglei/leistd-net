---
name: deploy
description: |
  在需要准备/执行部署、健康检查、日志/状态检查或回滚时使用；验收收口交给 task-manager，生产变更须先获用户显式确认。

  使用时机：
  (1) Plan/context 指向 Phase 6 部署发布
  (2) 测试和代码审查通过后需要发布、重启或健康检查
  (3) 用户自然语言：「部署上线」「发布一下」「回滚」「看服务日志 / 状态」「重启服务」

  不适用：仅需汇总验收（用 task-manager）；测试/审查尚未通过（先用 test-runner / code-review）。
metadata:
  openclaw:
    requires: []
    skillKey: "deploy"
user-invocable: true
disable-model-invocation: false
---

# 部署发布 (deploy)

> 生产部署是高风险动作；默认先准备方案，只有获得用户显式确认后才执行变更。

## 必读顺序

1. `docs/standards/agent-workflow.md`：Phase 6 门禁、确认点和交接包。**先按其 §2.0 定位「项目根」**（含 `docs/`+`backend/`+`frontend/` 的那一层），本 skill 所有 `docs/xxx` 均相对该根解析——monorepo 布局下项目根是 `apps/<name>/` 而非仓库根。
2. Plan、context、code-review report、test report。
3. `docs/deploy/`、compose、Dockerfile、CI/CD 或项目声明的部署配置。

## 动作类型

| 动作 | 说明 |
| --- | --- |
| `deploy` | 预检查、构建/拉取镜像、备份、重启、健康检查、报告 |
| `health-check` | 只做健康检查、日志检查和核心路径验证 |
| `logs` | 查看服务日志，必须脱敏 |
| `status` | 查看容器、进程或服务状态 |
| `rollback` | 回滚到备份配置或上一版本，必须确认 |
| `config-check` | 仅验证部署配置 |

## 环境确认规则

- `development` / `local`：项目配置允许时可自动执行低风险命令。
- `staging` / `test`：可按项目配置执行，但涉及数据迁移、重启共享服务时先确认。
- `production`：部署、回滚、重启、迁移、密钥或环境变量变更必须先获得用户显式确认。

## 预检查

- code-review 无 P0。
- test report 通过，或未执行原因和风险已被用户接受。
- 工作区状态符合项目发布规范。
- 目标环境、版本、服务器、命令、影响范围和回滚方式明确。
- 缺少服务器、端口、环境变量或回滚方案时，只输出部署方案，不直接执行。

## 工作流程

1. 识别动作和目标环境。
2. 读取部署配置和阶段报告。
3. 做预检查并列出风险。
4. 对生产或高风险动作请求确认。
5. 执行动作；每步失败即停止并汇报。
6. 做健康检查、日志检查和核心功能验证。
7. 写入 `docs/reports/deploy/{req-id}-deploy-report.md`。
8. 输出 handoff，成功后推荐 `task-manager` 验收收口。

## 输出

- 部署/检查目标、版本、命令摘要和结果。
- 健康检查、日志检查、访问地址或失败原因。
- 回滚状态和人工介入建议（如需要）。
- deploy report 路径。
- 阶段交接包。

```yaml
handoff:
  phase: 6
  phaseName: deploy
  gateStatus: pass | fail | blocked | needs-confirmation
  nextRecommendedSkill: task-manager
```

## 按需资源

| 资源 | 路径 | 读取时机 |
| --- | --- | --- |
| 部署工作流参考 | `references/deploy-workflow.md` | 执行 deploy/rollback 或生成详细报告时 |
| 对齐与降级策略 | `references/reconcile-strategy.md` | 缺少部署配置，或实际与部署文档冲突时 |
| 服务器配置模板 | `templates/server-config-template.md` | 用户要求创建部署文档时 |
| Docker Compose 模板 | `templates/docker-compose-template.yml` | 用户要求创建 compose 示例时 |

## 禁止事项

- 未确认时不执行生产部署、回滚、重启、迁移或密钥变更。
- 不泄露密码、Token、证书私钥、SSH 私钥。
- 不执行破坏性命令，如删除 volume、清理生产数据、强制重置仓库，除非用户明确授权。
- 不猜测服务器、端口、环境变量或回滚方案。
