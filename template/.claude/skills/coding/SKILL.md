---
name: coding
description: |
  按已确认的 Plan.md 实施代码开发，并按项目规范完成最小验证。

  **当以下情况时使用此 Skill**:
  (1) Plan.md 已确认，需要实施代码开发
  (2) 需要按项目规范生成或修改代码
  (3) 需要在开发阶段完成构建、类型检查或最小相关测试

  **调用 Agent**：架构师助手（architect）
metadata:
  openclaw:
    requires: []
    skillKey: "coding"
user-invocable: true
disable-model-invocation: false
---

# 代码开发 (coding)

> Skill 只规定通用开发流程；技术栈、命令、目录和检查清单以项目级文档为准。

## 执行前必读

- **先读 Plan**：确认范围、策略、步骤、风险和 Must have 验收项。
- **先读规范**：优先读取 `docs/standards/agent-workflow.md`，再按索引读取技术栈和编码规范。
- **无规范不套模板**：缺少规范时，根据现有代码和配置推断最小规则，并提示补齐项目规范。
- **复杂先出方案**：跨模块、接口契约、数据结构、权限、安全、生产配置变更必须先给方案并等待确认。
- **最小验证**：开发完成后运行项目声明的构建、类型检查或最小相关测试；完整测试由后续上下文触发对应测试能力。
- **沉淀报告**：开发完成后写入 `docs/reports/development/{req-id}-dev-report.md`，供后续审查和测试读取。

## 安全边界

- 不生成硬编码密码、Token、API Key；使用环境变量或安全配置。
- 删除/迁移代码必须有明确依据；超出 Plan 范围或影响外部接口时先确认。
- 不修改 `.gitignore`、CI、部署配置、数据库结构，除非 Plan 或用户明确要求。
- 不向生产库写测试数据；测试数据放在项目约定的测试或 mock 位置。

## 工作流程

1. **读取 Plan.md**：提取范围、策略、文件清单、风险、Must have。
2. **读取项目规范**：`agent-workflow.md` → 技术栈 → 编码规范 → 检查清单。
3. **必要时输出方案**：复杂变更先说明契约、文件清单、实施顺序和风险。
4. **实施代码**：保持与现有目录、命名、错误处理、日志、测试风格一致。
5. **内置规范审查**：对照项目检查清单处理 P0/P1/P2；正式审查由后续上下文触发对应审查能力。
6. **最小验证**：运行构建、类型检查或最小相关测试，并记录结果。
7. **沉淀报告**：写入 dev report，说明变更、验证、风险和下一步测试建议。

详细方案模板、内置审查和最小验证规则见 `references/implementation-workflow.md`。

## 项目规范读取

优先顺序：
1. `docs/standards/agent-workflow.md`
2. 索引指定的 `tech-stack`、`code-standard`、`test` 文档
3. 常见路径：`docs/standards/tech-stack.md`、`docs/standards/code-standard/`
4. 项目配置和邻近代码：`package.json`、`*.csproj`、`pyproject.toml`、`go.mod` 等

提取：技术栈、构建命令、目录结构、命名规则、编码规范、检查清单。

## 规范审查边界

| 检查项 | coding 内置审查 | 独立 code-review |
|--------|----------------|------------------|
| 项目规范清单 | 是 | 是 |
| 自动化检查 | 最小必要 | 完整审查 |
| Must have 覆盖 | 简要自查 | 必须检查 |
| PR/提交前审查 | 否 | 是 |

## 参考资源

| 资源 | 路径 | 用途 |
|------|------|------|
| 实施工作流参考 | `references/implementation-workflow.md` | 复杂方案、审查、验证细节 |
| 降级策略 | `references/fallback-strategy.md` | 无规范时处理 |
| 开发自测报告模板 | `templates/dev-report-template.md` | 生成 dev report |
| .NET+Angular 示例 | `examples/techstack-dotnet-angular.md` | 项目级规范示例，不是通用默认 |
| 后端规范示例 | `examples/code-standard-backend.md` | 项目级后端规范示例 |
| 前端规范示例 | `examples/code-standard-frontend.md` | 项目级前端规范示例 |

## 完成衔接

| 执行结果 | 下一动作 |
|----------|----------|
| 开发 + 内置审查 + 最小验证通过 | 写入 dev report，并在输出中标记“需要正式代码审查” |
| 规范审查有 P0 | 修复后重新验证 |
| 构建或类型检查失败 | 修复后重新验证 |
| 发现需求/方案变化 | 暂停并向用户确认 |

---

*最后更新：2026-06-25 项目规范优先版 v2.0.0*
*维护：通用开发助手*



