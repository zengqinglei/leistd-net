# 对齐与降级策略 (Reconcile & Fallback Strategy)

> 当项目规范/配置**缺失**，或实际情况与通用/项目级策略**冲突**时，Skill 的提示、沉淀与降级行为。

---

## 🎯 核心原则

### 1. 双层降级（项目级优先，内置兜底）

- ✅ **有项目级覆盖**：读取 `docs/standards/` 对应文档，按项目偏好执行。
- ✅ **无项目级覆盖**：使用本 Skill 内置默认规则继续执行（本文件即内置兜底，永不缺失）。
- ❌ **不因缺失而报错停止**：继续执行，但显式告知用户。

### 2. 区分"缺失"与"冲突"

- **缺失**：应读取的规范/配置不存在 → 用内置默认继续 + 提示可创建项目级文档。
- **冲突**：实际情况（代码 / 配置 / 接口 / 命名 / 门槛）与通用策略或已有项目级文档**不一致** → 提示差异 + 建议将差异**沉淀或更新到对应项目文档** + 降级继续。

### 3. 提示而非阻塞

- ✅ **提示一次**：首次检测到缺失或冲突时提示，避免重复打扰。
- ✅ **继续执行**：用现有模式或内置默认完成当前任务。
- ✅ **提供指引与落点**：告诉用户具体文件路径与沉淀落点。
- ✅ **记录可追溯**：在任务 context / 阶段报告中记录本次降级或冲突处置。

---

## 🧭 冲突沉淀落点矩阵

检测到冲突时，按类型建议沉淀到对应项目文档（存在则更新，不存在则建议创建）：

| 冲突类型 | 沉淀落点 |
| --- | --- |
| 技术栈声明 vs 实际代码 | `docs/standards/tech-stack.md` |
| 编码规范 vs 邻近代码 | `docs/standards/code-standard/{backend|frontend|common}-develop.md` |
| API 规范 vs 现有接口风格 | `docs/standards/api-standard.md` |
| 测试门槛/框架 vs 项目实际 | 项目测试配置（`.runsettings` / `vitest.config.*`）或对应规范 |
| 命名规范 vs 现有目录 | `docs/standards/document-naming.md` 或 `project-structure.md` |
| 部署策略 vs 项目文档 | `docs/deploy/` 相关文档 |

沉淀原则：只记录**通用可复用规则**，不写一次性内容；同一规则不在多处重复维护（见 `document-naming.md` §4）。

---

## 📋 本 Skill 检测清单（code-review）

1. `docs/standards/agent-workflow.md`（Phase 4 门禁、交接包）
2. `docs/standards/code-standard/`（前端/后端/通用规范）
3. lint/format 配置：`.eslintrc.*` / `eslint.config.js` / `.editorconfig`

**冲突示例**：邻近代码普遍用单引号但项目无 ESLint 声明 → 提示差异，建议在 `.editorconfig`/ESLint 配置或 `code-standard/frontend-develop.md` 固化，本次以既有代码风格为准。

**缺失降级**：无 lint 配置时，以**既有代码风格 + 项目 `code-standard/`（若有）**为准做一致性检查，**不自动套用内置 eslint/editorconfig 样例的具体规则**；并提示用户：如需固化 lint 规范，可据本项目实际风格约定后写入 `code-standard/frontend-develop.md` 或生成配置（`eslintrc`/`editorconfig` 样例仅作同栈参考，套用前按项目约定核对）。

---

*最后更新：2026-07-06*
*维护：code-review Skill*
