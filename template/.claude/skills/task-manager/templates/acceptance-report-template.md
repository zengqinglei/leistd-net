# {req-id} 验收收口报告

## 1. 总结

- 需求 ID：{req-id}
- Plan 文档：docs/requirements/{req-id}-plan.md
- 结论：done / blocked
- 收口时间：{iso-datetime}
- 验收人：用户 / Agent / 项目约定

## 2. 阶段结果

| Phase | 阶段 | 报告 | 状态 | 关键证据 |
| --- | --- | --- | --- | --- |
| 3 | 开发自测 | docs/reports/development/{req-id}-dev-report.md | pass/fail/skipped | 待填写 |
| 4 | 代码审查 | docs/reports/code-review/{req-id}-code-review.md | pass/fail/skipped | 待填写 |
| 5 | 测试验证 | docs/reports/tests/{req-id}-test-report.md | pass/fail/skipped | 待填写 |
| 6 | 部署发布 | docs/reports/deploy/{req-id}-deploy-report.md | pass/fail/skipped | 待填写 |

## 3. Must have 验收对照

| Must have | 实施证据 | 测试证据 | 运行/部署证据 | 状态 |
| --- | --- | --- | --- | --- |
| 待填写 | 待填写 | 待填写 | 待填写 | pass/fail/blocked |

## 4. 未完成项与风险

| 项目 | 影响 | 责任方 | 下一步 |
| --- | --- | --- | --- |
| 待填写 | 待填写 | 待填写 | 待填写 |

## 5. 最终交接包

```yaml
handoff:
  reqId: {req-id}
  phase: 7
  phaseName: acceptance
  gateStatus: pass | blocked
  nextRecommendedSkill: none
  userConfirmationRequired: false
  artifacts:
    - docs/reports/acceptance/{req-id}-acceptance.md
```
