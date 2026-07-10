---
name: code-review
description: |
  在需要对代码变更做需求对齐、规范与 P0/P1/P2 分级审查时使用；不跑完整测试（test-runner），不做需求分析（requirement-plan）。本 Skill 的特点是按项目 docs 规范就地审查并产出 review report。

  使用时机：
  (1) Plan/context 指向 Phase 4 代码审查
  (2) 开发完成后需要正式审查
  (3) 用户自然语言：「帮我 review 一下」「提交前检查」「看看这个 diff / PR」「检查代码质量」

  不适用：需要运行测试验证（用 test-runner）；需要澄清需求范围（用 requirement-plan）。
metadata:
  openclaw:
    requires: []
    skillKey: "code-review"
user-invocable: true
disable-model-invocation: false
---

# 代码审查 (code-review)

> 以发现缺陷、回归风险、需求遗漏和规范违规为主；不替代测试阶段。

## 必读顺序

1. `docs/standards/agent-workflow.md`：Phase 4 门禁和交接包。**先按其 §2.0 定位「项目根」**（含 `docs/`+`backend/`+`frontend/` 的那一层），本 skill 所有 `docs/xxx` 均相对该根解析——monorepo 布局下项目根是 `apps/<name>/` 而非仓库根。
2. Plan、context、dev report。
3. 项目级技术栈、编码规范、API 规范、测试规范中与变更相关的检查清单；前端/界面 diff 另读 `docs/standards/ui-design-strategy.md`。
4. 本次 diff、相关邻近代码和项目配置。

## 审查范围

- **需求对齐**：Must have 是否实现，是否偏离核心策略和文件变更清单。
- **行为风险**：边界条件、错误处理、权限、安全、数据一致性、兼容性。
- **规范对齐**：目录、命名、分层、组件、接口、日志、测试风格。
- **自动化检查**：优先使用项目声明的 lint/format/build/typecheck 命令。
- **测试充分性**：识别缺失测试，但完整执行由 `test-runner` 负责。

## 分级

| 等级 | 含义 | 门禁 |
| --- | --- | --- |
| P0 | Must have 未实现、安全/数据风险、构建失败、严重回归 | 阻塞，回到 `coding` |
| P1 | 重要规范或可维护性问题，短期应修复 | 可继续但需记录 |
| P2 | 风格、命名、轻微优化建议 | 不阻塞 |

> **规范硬规则违反 = P1，不得降级为 P2 放行。** 明文硬规则（`code-standard/` 与 `api-standard.md` 里带「必须/禁止/而非」字样的条款）违反属 P1，例如：分页方法名用了 `GetListAsync` 而非 `GetPagedListAsync`、路由缺 `/api/v1/` 前缀、实体内直用 `DateTime.Now/UtcNow` 而非 `IClock`、手写审计字段（应由拦截器填充）、组件示例引入错误分层类型等。这些不是「风格」，是被规范明文否决的错误。仅在项目规范确实未声明该条款时才可视为建议（P2）并提示补规范。

## 工作流程

1. 确认审查基线：diff、文件列表、commit range 或用户指定范围。
2. 读取相关规范和 Plan 验收项。
3. 运行或记录项目声明的自动化检查。
4. 逐项输出发现，包含文件路径、行号、影响和修复建议。
5. 按 `references/review-report-template.md` 结构写入 `docs/reports/code-review/{req-id}-code-review.md`——**其中「规范硬规则核对」表须逐条勾选，不得整体填一个 pass**。
6. 输出 handoff：无 P0 推荐 `test-runner`；有 P0 推荐 `coding`。
7. 若本需求已因 P0 从 code-review 回退 coding 累计达 2 次，输出 `gateStatus: needs-confirmation` 交人工决策（见 agent-workflow §4 stop condition）。

## 输出格式

- findings 优先，按 P0 → P1 → P2 排序。
- 每个问题包含：文件位置、问题、影响、建议。
- 若无发现，明确说明“未发现 P0/P1/P2 问题”，并列出残余风险或未执行检查。
- 报告路径和阶段交接包。

```yaml
handoff:
  phase: 4
  phaseName: code-review
  gateStatus: pass | fail
  nextRecommendedSkill: test-runner | coding
```

## 按需资源

| 资源 | 路径 | 读取时机 |
| --- | --- | --- |
| 审查报告模板 | `references/review-report-template.md` | 每次产出 review report 时（含规范硬规则逐项核对表） |
| 对齐与降级策略 | `references/reconcile-strategy.md` | 项目缺少规范/检查命令，或实际与规范冲突时 |
| 后端规范 | `docs/standards/code-standard/backend-develop.md`（单一权威，勿另存副本） | 审查后端变更时 |
| 前端规范 | `docs/standards/code-standard/frontend-develop.md`（单一权威） | 审查前端变更时 |
| EditorConfig 模板 | `templates/editorconfig-template` | 用户要求创建规范配置时 |
| ESLint 模板 | `templates/eslintrc-template.js` | 用户要求创建规范配置时 |

## 禁止事项

- 不忽略 P0。
- 不把 P1/P2 伪装为通过且不记录。
- 不审查未读取的规范或未查看的 diff。
- 不声称运行了未运行的自动化检查。
- 不自动提交有 P0 的代码。
