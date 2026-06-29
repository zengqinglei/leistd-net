---
name: test-runner
description: |
  运行项目声明的单元、集成、业务场景或 E2E 测试，收集结果并生成测试报告。

  使用时机：
  (1) Plan/context 指向 Phase 5 测试验证
  (2) 代码审查通过后需要验证 Must have 覆盖
  (3) 用户要求运行测试、覆盖率、集成测试或业务场景测试
metadata:
  openclaw:
    requires: []
    skillKey: "test-runner"
user-invocable: true
disable-model-invocation: false
---

# 测试验证 (test-runner)

> 优先使用项目声明的测试命令；缺少配置时记录降级，不把通用建议当项目门槛。

## 必读顺序

1. `docs/standards/agent-workflow.md`：Phase 5 门禁和交接包。
2. Plan、context、code-review report、dev report。
3. 项目测试规范和测试配置：`package.json`、`*.csproj`、`pyproject.toml`、`go.mod`、测试框架配置等。

## 测试范围

- **单元测试**：核心函数、领域逻辑、service、pipe、工具函数。
- **集成测试**：API、数据库、消息队列、外部适配器的项目约定测试。
- **业务场景测试**：对照 Must have 验证关键用户路径。
- **E2E/页面验证**：仅在项目已有配置或用户要求时执行。
- **覆盖率**：以项目门槛为准；无门槛时只作为建议项。

## 工作流程

1. 检测项目类型和测试配置。
2. 读取 Plan 的 Must have 和验收映射。
3. 选择最小但足够覆盖本次变更的测试命令；部署前可运行完整套件。
4. 执行测试并收集通过/失败/跳过、耗时、失败详情、覆盖率。
5. 建立 Must have → 测试/证据映射。
6. 写入 `docs/reports/tests/{req-id}-test-report.md`。
7. 输出 handoff：通过推荐 `deploy` 或验收；失败推荐 `coding`。

## 决策规则

| 情况 | gateStatus | 下一步 |
| --- | --- | --- |
| 测试通过且 Must have 有证据 | pass | `deploy` 或 `task-manager` |
| 测试失败、命令失败、关键依赖缺失 | fail | `coding` |
| 环境缺失但可人工补充 | blocked | 用户或外部动作 |
| Must have 未覆盖 | fail 或 needs-confirmation | 补测试或用户确认风险 |
| 无项目覆盖率门槛但覆盖率偏低 | pass + 风险记录 | 继续但记录建议 |

## 输出

- 执行的命令和结果。
- 失败测试摘要和修复方向。
- Must have 覆盖表和证据路径。
- 测试报告路径。
- 阶段交接包。

```yaml
handoff:
  phase: 5
  phaseName: test-runner
  gateStatus: pass | fail | blocked
  nextRecommendedSkill: deploy | coding | task-manager
```

## 按需资源

| 资源 | 路径 | 读取时机 |
| --- | --- | --- |
| 测试报告模板 | `references/test-report-template.md` | 生成详细报告时 |
| 降级策略 | `references/fallback-strategy.md` | 无测试配置或命令失败时 |
| runsettings 模板 | `templates/runsettings-template` | 用户要求 .NET 测试配置时 |
| Vitest 配置模板 | `templates/vitest-config-template.ts` | 用户要求 Vitest 配置时 |
| 覆盖率门槛模板 | `templates/coverage-thresholds-template.md` | 用户要求创建覆盖率规范时 |

## 禁止事项

- 不删除有效断言或降低测试门槛来制造通过。
- 不把未执行测试写成已通过。
- 不忽略失败、超时、缺失依赖或未覆盖验收项。
- 不把示例命令当作所有项目默认事实。
