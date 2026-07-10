# 文档归档策略 (Archiving Policy)

> 需求方案、任务上下文、阶段报告是**一次性、随时间累积**的产物。本文件说明何时、如何把历史产物物理归档。规则源以 `docs/standards/agent-workflow.md` §12 为准，本文件只给 task-manager 的可执行操作细节，不重复规则。

---

## 🎯 核心原则

- **默认扁平**：文件名内嵌 `req-YYYYMMDD-NNN`，已自带时间序。日常**不**按迭代/年月建目录层级，**不**预建 `archive/` 空目录。
- **归档是到期动作，不是初始结构**：只有历史文件才可能进 `archive/`；**新产物始终写扁平目录**（`requirements/`、`requirements/context/`、`reports/{type}/`）。
- **归档是移动，不是删除**：除非用户显式要求，不删除任何归档文件。
- **不改命名、不改 Skill 路径逻辑**：归档只增加一层 `archive/` 前缀，其余 Skill 的产出路径不受影响。

---

## ⏱️ 何时触发

满足任一即可执行一次归档（低频，人工或收口时触发，**不在日常阶段流程里自动做**）：

1. 用户**显式要求**归档某需求 / 某批历史需求。
2. 需求在 registry 置为 `done`/`archived` 后**超过保留期**（默认 3 个月，项目可在 `agent-workflow.md` §12 调整）。
3. 某报告目录（如 `reports/code-review/`）**单目录文件数过多**（默认超过约 50 个）影响检索。

---

## 🔧 归档动作

对每个待归档需求 `{req-id}`：

| 产物 | 从 | 到 |
| --- | --- | --- |
| 需求方案 | `requirements/{req-id}-plan.md` | `requirements/archive/{req-id}-plan.md` |
| 任务上下文 | `requirements/context/{req-id}.md` | `requirements/context/archive/{req-id}.md` |
| 阶段报告 | `reports/{type}/{req-id}-*.md` | `reports/{type}/archive/{req-id}-*.md` |

步骤：

1. `archive/` 目录**按需创建**（第一次归档才建）。
2. **移动文件，保持原文件名不变**（ID 内日期即归档批次依据）。
3. 更新 `registry.md`：该需求行状态置 `archived`；若仍在「活跃需求」表则移入「已完成需求」表；路径列（Plan / Context / 报告）更新为 `archive/` 后的新路径。
4. 输出归档摘要：归档了哪些需求、涉及哪些文件、registry 变更。

---

## 🧭 迭代 / 批次聚合视角

若项目需要「某迭代（如 2026-07-sp1）交付了哪些需求」的聚合视角：

- ✅ **优先在 `registry.md` 增加「迭代/批次」列**做逻辑聚合，保持物理目录扁平、Skill 路径稳定。
- ❌ **不要**为聚合而改物理目录层级（如 `reports/2026-07-sp1/…`）——那会迫使所有 Skill 感知「当前迭代」上下文，得不偿失。

---

## 🚫 禁止事项

- 不预建 `archive/` 或迭代空目录。
- 不因归档而重命名文件或改变命名规范。
- 不删除归档文件（除非用户显式要求）。
- 不把归档动作塞进日常阶段推进（它是独立的低频收尾操作）。
