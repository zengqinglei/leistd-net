# 部署工作流参考

> 仅在执行 `deploy`、`rollback` 或需要生成部署报告时读取。

## 1. 预检查

- code-review report 无 P0。
- test report 通过，或未执行原因和风险已被用户接受。
- 工作区状态符合项目发布规范。
- Plan 的 Must have 已验证或明确标记未覆盖。
- 目标环境、版本、服务器、命令、影响范围和回滚方案明确。
- production 部署、回滚、重启、迁移、密钥或环境变量变更已获得用户确认。

## 2. 配置读取

1. `docs/standards/agent-workflow.md`。
2. `docs/deploy/` 相关文档。
3. 常见配置：`docker-compose.yml`、`compose.yml`、`Dockerfile`、CI/CD 文件、`.env.deploy`。
4. 缺少配置时，只准备方案，不猜测生产服务器。

## 3. 执行顺序

1. 记录目标环境、版本和命令。
2. 备份将被修改的配置。
3. 构建或拉取镜像/产物。
4. 标记版本，优先使用 commit/tag。
5. 更新配置或发布产物。
6. 重启服务或切换流量。
7. 健康检查、日志检查、核心功能验证。
8. 写入部署报告和 handoff。

## 4. 回滚

触发条件：健康检查失败、核心功能不可用、关键日志异常、用户要求回滚。

回滚前确认：目标环境、回滚版本、数据影响、回滚命令。production 回滚必须确认。

## 5. 报告模板

# {req-id} 部署报告

## 1. 总结

- 需求 ID：{req-id}
- Phase：6 deploy
- 结果：pass / fail / blocked / rolled-back
- 环境：development / staging / production
- 版本：commit/tag
- 时间：{iso-datetime}

## 2. 部署步骤

| 步骤 | 命令/动作 | 结果 | 证据 |
| --- | --- | --- | --- |
| 预检查 | 待填写 | pass/fail | 待填写 |

## 3. 健康检查

| 检查项 | 结果 | 证据 |
| --- | --- | --- |
| 服务状态 | pass/fail | 待填写 |
| 日志检查 | pass/fail | 待填写 |
| 核心路径 | pass/fail | URL/日志/截图 |

## 4. 验收闭环对照

| Must have | 状态 | 验证方式 | 证据 |
| --- | --- | --- | --- |
| 待填写 | pass/fail/skipped | 健康检查/API/页面验证 | 待填写 |

## 5. 回滚信息

- 回滚方案：待填写
- 是否执行回滚：是 / 否
- 回滚结果：待填写

## 6. 阶段交接包

```yaml
handoff:
  reqId: {req-id}
  phase: 6
  phaseName: deploy
  gateStatus: pass | fail | blocked | needs-confirmation
  nextRecommendedSkill: task-manager
  userConfirmationRequired: false
  artifacts:
    - docs/reports/deploy/{req-id}-deploy-report.md
  evidence: []
  blockers: []
  assumptions: []
```
