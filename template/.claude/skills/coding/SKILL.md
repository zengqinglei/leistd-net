---
name: coding
description: |
  在需要按已确认的 Plan.md 落地代码改动时使用；正式审查交给 code-review，完整测试交给 test-runner。

  使用时机：
  (1) Plan/context 指向 Phase 3 开发自测
  (2) 需要新增、修改或重构代码以满足已确认需求
  (3) code-review 或 test-runner 发现问题需回到开发修复
  (4) 用户自然语言：「实现这个功能」「按 Plan 开发」「把这块代码改一下」

  不适用：还没有已确认 Plan（先用 requirement-plan）；只想审查或跑测试（用 code-review / test-runner）。
metadata:
  openclaw:
    requires: []
    skillKey: "coding"
user-invocable: true
disable-model-invocation: false
---

# 代码开发 (coding)

> 只规定通用开发流程；语言、框架、命令、目录和检查清单以项目级文档与现有代码为准。

## 必读顺序

1. `docs/standards/agent-workflow.md`：阶段、交接包、人工确认点。**先按其 §2.0 定位「项目根」**（含 `docs/`+`backend/`+`frontend/` 的那一层），本 skill 所有 `docs/xxx` 均相对该根解析——monorepo 布局下项目根是 `apps/<name>/` 而非仓库根。
2. 当前 `docs/requirements/{req-id}-plan.md` 和 `docs/requirements/context/{req-id}.md`。
3. 规范（按文件名点名，勿泛读）：
   - **必读** `docs/standards/code-standard/common-develop.md` §5「工程铁律」全节（技术栈无关的强约定）。
   - 含 API/接口变更时**必读** `docs/standards/api-standard.md`（路由/分页/DTO 命名/响应格式的单一权威，勿开发后才在 code-review 才发现违规）。
   - 后端变更读 `docs/standards/code-standard/backend-develop.md` 相关条；前端变更读 `docs/standards/code-standard/frontend-develop.md` 相关条。
4. 项目配置和邻近代码：如 `package.json`、`*.csproj`、`pyproject.toml`、`go.mod`、现有模块代码。

## 第 0 步（动手前强制）：先找参考链路

**写任何新需求代码前，先在现有代码里找 1~2 个同类的、从前端到后端的完整链路作为参考样板**（见 `common-develop.md §5.0`），端到端读一遍（前端组件→service→DTO→mock ↕ 后端 Controller→AppService→DomainService/Entity→持久化），照它的分层/命名/目录/错误处理/mock 风格写新代码，逐层对应——**不凭记忆自创风格**。这是 AI 最易犯的错，故列为动手前的强制前置动作，而非可选建议。在 dev-report 里说明参考了哪条链路。

## 开发边界

- 严格按 Plan 范围实现；范围变化先记录并请求确认。
- 无项目规范时，根据现有配置和邻近代码推断最小规则，并说明假设。
- 不把模板默认 .NET/Angular 规范当作未知项目的事实。
- 跨模块、接口契约、数据结构、权限、安全、生产配置变更必须先给方案并等待确认。

## 工作流程

1. **恢复上下文**：确认 reqId、Phase、Must have、文件清单和最近 handoff。
2. **制定最小实施路径**：复杂变更先输出方案；简单变更直接实施。
3. **修改代码**：保持目录、命名、错误处理、日志、测试风格一致。
4. **内置自查**：对照 Plan 和相关规范检查 P0/P1/P2；正式审查由 `code-review` 执行。
5. **最小验证**：运行项目声明的构建、类型检查或最小相关测试；无法执行时记录原因。
6. **沉淀报告**：写入 `docs/reports/development/{req-id}-dev-report.md`。
7. **输出交接包**：通过时推荐 `code-review`；失败或阻塞时说明回退/所需动作。

## 最小验证原则

- 优先使用项目规范或配置声明的命令。
- 无声明时，根据检测到的技术栈选择最小安全命令。
- 不安装依赖、不访问网络、不修改生产配置，除非用户确认。
- 不删除有效测试或降低断言来制造通过。
- 验证未执行或降级时，在 handoff 的 `verification` 段如实标注（`executed:false` / `degraded:true` + 原因），不得计入纯 pass（见 agent-workflow §4）。

## 输出

- 代码变更摘要和涉及文件。
- 已执行命令、结果、失败原因或未执行原因。
- dev report 路径。
- 最新 handoff，通常为：

```yaml
handoff:
  phase: 3
  phaseName: coding
  gateStatus: pass
  nextRecommendedSkill: code-review
  userConfirmationRequired: false
```

## 按需资源

| 资源 | 路径 | 读取时机 |
| --- | --- | --- |
| 实施工作流参考 | `references/implementation-workflow.md` | 复杂方案、内置审查或最小验证细化时 |
| 对齐与降级策略 | `references/reconcile-strategy.md` | 项目缺少规范/命令，或实际与规范冲突时 |
| 开发自测报告模板 | `templates/dev-report-template.md` | 生成 dev report 时 |
| 技术栈与编码规范 | `docs/standards/tech-stack.md` + `docs/standards/code-standard/{backend,frontend}-develop.md`（单一权威，勿另存副本） | 需要技术栈/编码具体规则时 |

## 禁止事项

- 不跳过 Plan/context 直接开发。
- 不静默扩大需求范围。
- 不硬编码密钥、Token、密码或真实客户数据。
- 不修改 CI、部署、数据库结构或安全边界，除非 Plan 或用户明确要求。
- 不声称执行未执行的验证。
