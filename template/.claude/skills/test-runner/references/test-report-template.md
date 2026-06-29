# 测试报告模板

> 仅在生成详细测试报告时读取。

# {req-id} 测试验证报告

## 1. 总结

- 需求 ID：{req-id}
- Phase：5 test-runner
- 结论：pass / fail / blocked
- 时间：{iso-datetime}

## 2. 测试范围

- Plan：docs/requirements/{req-id}-plan.md
- Review report：docs/reports/code-review/{req-id}-code-review.md
- 测试类型：单元 / 集成 / 业务场景 / E2E / 覆盖率

## 3. 执行结果

| 类型 | 命令/方式 | 结果 | 通过 | 失败 | 跳过 | 耗时 | 证据 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 单元测试 | 待填写 | pass/fail/skipped | N | N | N | N 秒 | 待填写 |

## 4. Must have 覆盖

| Must have | 测试类型 | 测试结果 | 证据 | 状态 |
| --- | --- | --- | --- | --- |
| 待填写 | 单元/集成/E2E/人工 | pass/fail/skipped | 测试文件/报告/日志 | pass/fail/blocked |

## 5. 覆盖率

| 类型 | 覆盖率 | 项目门槛 | 状态 |
| --- | --- | --- | --- |
| 行覆盖率 | xx% | 项目门槛或无 | pass/fail/advisory |
| 分支覆盖率 | xx% | 项目门槛或无 | pass/fail/advisory |
| 函数覆盖率 | xx% | 项目门槛或无 | pass/fail/advisory |

## 6. 失败与阻塞

| 测试/命令 | 文件 | 错误摘要 | 建议 |
| --- | --- | --- | --- |
| 待填写 | path:line | 待填写 | 待填写 |

## 7. 阶段交接包

```yaml
handoff:
  reqId: {req-id}
  phase: 5
  phaseName: test-runner
  gateStatus: pass | fail | blocked
  nextRecommendedSkill: deploy | coding | task-manager
  userConfirmationRequired: false
  artifacts:
    - docs/reports/tests/{req-id}-test-report.md
  evidence: []
  blockers: []
  assumptions: []
```
