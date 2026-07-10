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

## 📋 本 Skill 检测清单（test-runner）

1. `docs/standards/agent-workflow.md`（Phase 5 门禁）
2. `docs/standards/test.md`（测试规范、覆盖率门槛）
3. 测试配置：`.runsettings` / `xunit.runner.json`（.NET）、`vitest.config.*` / `jest.config.*`（Node）
4. `package.json` scripts / `*.csproj` 中声明的 test 命令

**冲突示例**：`test.md` 要求行覆盖率 ≥80%，但项目 `vitest.config` 配的是 60% → 提示差异，建议对齐门槛（更新规范或配置二选一），本次以项目配置实际执行值为准并记录。

**缺失降级**：无测试配置时用项目声明的 test 命令、探测不到则按栈用其惯用命令跑（.NET `dotnet test` / Node `vitest` 等），并记录所用命令。**无覆盖率门槛时不预设任何数值**（不假设 80%）：本次覆盖率仅作建议、不阻塞，同时用 `templates/coverage-thresholds-template.md` 的**提问清单**引导用户约定门槛并沉淀到 `docs/standards/test.md`，之后以其为准。`runsettings`/`vitest` 样例仅在用户要求生成配置时作**同栈参考**，不自动套用其内数值。

---

*最后更新：2026-07-06*
*维护：test-runner Skill*
