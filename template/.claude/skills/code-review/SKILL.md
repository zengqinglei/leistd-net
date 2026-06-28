---
name: code-review
description: |
  对代码变更进行需求对齐、规范检查、自动化检查和 P0/P1/P2 分级审查。

  使用时机：
  (1) Plan/context 指向 Phase 4 代码审查
  (2) 开发完成后需要正式审查
  (3) 用户要求检查代码、规范、diff、PR 或提交前质量
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

1. `docs/standards/agent-workflow.md`：Phase 4 门禁和交接包。
2. Plan、context、dev report。
3. 项目级技术栈、编码规范、API 规范、测试规范中与变更相关的检查清单。
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

## 工作流程

1. 确认审查基线：diff、文件列表、commit range 或用户指定范围。
2. 读取相关规范和 Plan 验收项。
3. 运行或记录项目声明的自动化检查。
4. 逐项输出发现，包含文件路径、行号、影响和修复建议。
5. 写入 `docs/reports/code-review/{req-id}-code-review.md`。
6. 输出 handoff：无 P0 推荐 `test-runner`；有 P0 推荐 `coding`。

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
| 降级策略 | `references/fallback-strategy.md` | 项目缺少规范或检查命令时 |
| 后端规范示例 | `examples/code-standard-backend.md` | 用户要求模板示例时 |
| 前端规范示例 | `examples/code-standard-frontend.md` | 用户要求模板示例时 |
| EditorConfig 模板 | `templates/editorconfig-template` | 用户要求创建规范配置时 |
| ESLint 模板 | `templates/eslintrc-template.js` | 用户要求创建规范配置时 |

## 禁止事项

- 不忽略 P0。
- 不把 P1/P2 伪装为通过且不记录。
- 不审查未读取的规范或未查看的 diff。
- 不声称运行了未运行的自动化检查。
- 不自动提交有 P0 的代码。
