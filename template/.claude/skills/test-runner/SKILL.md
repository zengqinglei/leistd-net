---
name: test-runner
description: |
  运行测试套件、收集覆盖率并生成测试报告的通用流程。

  **当以下情况时使用此 Skill**:
  (1) 用户要求运行测试
  (2) 开发完成后需要验证
  (3) PR、部署前或验收前需要测试报告
  (4) 需要检查 Plan.md 的 Must have 测试覆盖

  **调用 Agent**:架构师助手(architect)
metadata:
  openclaw:
    requires: []
    skillKey: "test-runner"
user-invocable: true
disable-model-invocation: false
---

# 运行测试 (test-runner)

> 优先使用项目声明的测试命令；缺少配置时记录降级，不把通用建议当项目门槛。

## 执行前必读

- **先读配置**：优先读取 `docs/standards/agent-workflow.md` 的测试索引，再读取项目测试配置。
- **按项目命令执行**：优先使用 package/script、`.runsettings`、`pytest.ini`、`pyproject.toml` 等声明。
- **覆盖率以项目为准**：无项目门槛时使用通用建议，并在报告中标记为建议项。
- **报告验收覆盖**：若存在 Plan.md，报告必须包含 Must have 与测试证据的映射。
- **沉淀报告**：测试完成后写入 `docs/reports/tests/{req-id}-test-report.md`。
- **失败不掩盖**：失败测试、超时、缺失依赖、未覆盖验收项都要明确记录。

## 工作流程

1. **检测项目类型**：根据 `package.json`、`*.csproj`、`*.sln`、`pyproject.toml`、`go.mod` 等识别。
2. **读取测试配置**：项目级索引 → 测试配置文件 → 项目脚本命令。
3. **运行测试**：优先执行项目声明命令；按需运行覆盖率。
4. **收集结果**：通过/失败/跳过数量、耗时、失败详情、覆盖率。
5. **验收映射**：对照 Plan.md Must have 标记已覆盖/未覆盖。
6. **生成报告**：总结、失败分析、覆盖率、决策建议。

## 常见配置

| 技术栈 | 常见配置 | 常见命令 |
|--------|----------|----------|
| .NET | `.runsettings`、`xunit.runner.json`、`*.csproj` | `dotnet test` |
| Node.js | `package.json`、`vitest.config.*`、`jest.config.*` | `npm test` / `npx vitest run` |
| Python | `pytest.ini`、`pyproject.toml`、`setup.cfg` | `pytest` |
| Go | `go.mod` | `go test ./...` |

示例命令只能在检测到对应技术栈且项目无更明确命令时使用。

## 降级处理

如无测试配置：
- 使用项目现有 test 命令或测试框架默认配置。
- 提示用户创建项目级测试规范或配置。
- 在报告中记录降级行为和假设。

完整降级提示见 `references/fallback-strategy.md`。

## 决策规则

| 情况 | 决策 |
|------|------|
| 所有测试通过且覆盖率满足项目门槛 | 通过 |
| 有失败测试、测试命令失败或关键依赖缺失 | 打回 |
| Must have 未覆盖测试 | 标记风险，通常打回或等待用户决策 |
| 无项目覆盖率门槛但低于通用建议 | 标记建议项，不自动阻塞 |

## 参考资源

| 资源 | 路径 | 用途 |
|------|------|------|
| runsettings 模板 | `templates/runsettings-template` | .NET 测试配置参考 |
| Vitest 配置模板 | `templates/vitest-config-template.ts` | Vitest 配置参考 |
| 覆盖率门槛模板 | `templates/coverage-thresholds-template.md` | 项目级覆盖率规范参考 |
| 测试报告模板 | `references/test-report-template.md` | 生成详细报告 |
| 降级策略 | `references/fallback-strategy.md` | 无配置时处理 |

## 完成衔接

| 测试结果 | 下一动作 |
|----------|----------|
| 全部通过 | 进入部署或验收 |
| 测试失败 | 返回开发修复后重测 |
| 部分通过/环境问题 | 报告原因，等待用户决策 |

---

*最后更新:2026-06-25 项目配置优先版 v3.0.0*
*维护:通用质量助手*

