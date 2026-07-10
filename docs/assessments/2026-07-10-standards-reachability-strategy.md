# 规范可达性审视 + 简洁性推荐策略

> 审视 AI 在协作流程各入口能否被引导到对应规范，并结合"信息别太多以致 AI 忽略"的诉求，给出推荐策略。基于可达性走查（2026-07-10）。可达性与简洁性是同一问题的两面：够不到 → 堆再全也没用；够得到但太长 → 抓不住重点。解法是**分层**。

---

## 一、可达性诊断

### 可达性矩阵（入口 × 规范类别）

| 入口 \ 规范 | 工作流 | API | 后端 | 前端 | 通用铁律 §5 |
| --- | --- | --- | --- | --- | --- |
| 完整流程 Phase0→7 | 必达 | 可能达 | 可能达 | 可能达 | 必达 |
| 切入 coding | 必达 | 易漏 | 可能达 | 可能达 | 必达 |
| 切入 code-review | 必达 | 可能达 | 可能达 | 可能达 | 可能达 |
| 切入 test-runner/deploy/task-manager | 必达 | 不可达 | 不可达 | 不可达 | 不可达 |
| **裸自然语言（不触发 skill）** | **不可达** | **不可达** | **不可达** | **不可达** | **不可达** |
| 自驱连续改（不重触发 skill） | 易漏 | 易漏 | 易漏 | 易漏 | 易漏 |

### 关键事实
- **强项**：一旦进任一 skill，可达性高——`agent-workflow.md` 是 6 个 skill 的硬前置第 1 步；**按节点切入** coding/code-review 也会就地引导读对应规范（不必从 Phase 0 完整走）；循环中 code-review 每轮强制勾核对表。
- **根本断点**：整套可靠性**全押在「模型选中 skill」这一步**，而这步 (1) 无确定性触发（纯靠 description 短语匹配自然语言）；(2) 无会话级兜底——**无 root `CLAUDE.md`、无 `.claude/settings.json`、无 SessionStart/PreToolUse hook**。措辞没命中触发短语（"加个字段""这段有 bug 改一下"）或自驱不重起 skill → 规范从"必达"直接掉到"不可达"，中间无降级台阶。
- **最大盲区**：**裸自然语言 × 通用铁律 §5** = 不可达。common §5 那些"踩坑写死、联调期才炸"的铁律（枚举大小写边界、删除幂等、依赖倒置），一旦走裸自然语言就对 AI 完全不可见。
- **次要断点**：即便进了 coding，`api-standard.md` / `backend-develop.md` / `frontend-develop.md` 在必读顺序里**未按文件名点名**（只泛指"编码规范/相关条"），弱于 common §5 的"至少读全节"；api-standard 的强约束只在 code-review 的事后 P1 门禁，缺开发前的前置硬读——即"先违反、审查时再抓"而非"开发前读到"。

---

## 二、推荐策略：三层「极简指路 → 按需展开 → 逐项强制」

可达性与简洁性合一：**入口只放最少的字把 AI 导向规范（治可达性），规范正文按需展开（不塞进入口，治简洁性），skill 核对点逐项勾选（治强制）。**

### 第 1 层：会话级兜底 —— 极简"指路牌"（治最大盲区，最高优先）

给生成项目补一个**极短的 root 入口**，让**任何入口（含裸自然语言）**在动手前都被导向规范。两种载体，二选一或都用：

- **root `CLAUDE.md`**（AI 默认自动读，最可靠）：**极简**（20~30 行封顶），只做"指路"不复述规范。内容框架：
  ```
  # 本项目的 AI 协作约定（先读我）
  动手前：① 走工作流——按意图用 .claude/skills/ 的阶段 skill（改代码=coding、审查=code-review…）；
          ② 无论走不走 skill，改代码前必读 docs/standards/code-standard/common-develop.md §5「工程铁律」（踩坑写死的强约定）。
  规范索引（按需展开，勿全量堆读）：
    - 工作流/阶段/门禁 → docs/standards/agent-workflow.md
    - API 契约/路由/分页 → docs/standards/api-standard.md
    - 后端 .NET/DDD → code-standard/backend-develop.md
    - 前端 Angular → code-standard/frontend-develop.md
    - 通用工程铁律 → code-standard/common-develop.md §5
  框架 Leistd.* API 用法 → 见 backend/CLAUDE.md（勿臆造 API）。
  ```
  关键：它是**指针目录**，不是规范本身——AI 读它只花几十 token，就知道"该去哪读什么"。
- **可选叠加 `.claude/settings.json` 的 hook**（UserPromptSubmit / SessionStart）：在每次用户提问时注入一句"改代码前先看 root CLAUDE.md 的约定"。这是机器级兜底，比纯文档更硬——但引入 harness 依赖，属可选增强。

> 为什么是 root 而非 backend：AI 默认读**项目根**的 CLAUDE.md；`backend/CLAUDE.md` 只在进 backend 上下文才可能被读，且只讲框架 API、不指向铁律/工作流。root CLAUDE.md 补上"裸自然语言入口"这个盲区。

### 第 2 层：规范正文 —— 按需展开、单一权威、瘦身（治简洁性，即前一轮的瘦身方案）

- **每条硬规则只在一处权威展开**，其余处降为一句 `§x.x` 指针（消除 backend 单文件内"示例+专节+检查清单"三处重复，净减约 100 行）。
- **路由/DTO/分页命名收归 `api-standard.md` 单一权威**，backend 只留示例 + 指针。
- 让 AI 读规范时"一条只撞见一次权威版本"，不被重复稀释、不在 626 行里迷路。

### 第 3 层：skill 核对点 —— 逐项强制（已建，微调）

- coding 的 `dev-report §5.1` 自查表 + code-review 的 `review §3` 核对表已把 Top 铁律做成逐项勾选。**微调**：把 `api-standard.md`、`backend-develop.md`、`frontend-develop.md` 在 **coding 必读顺序里按文件名点名**（而非泛指"编码规范"），把 api 的强约束从"仅 code-review 事后 P1"提前到"coding 开发前也点名读"。
- 两个核对表已现漂移（review 多 common §5.4/§5.8），手工对齐 + 删判定点里复述规则的括号。

---

## 三、为什么这样最优（可达性 × 简洁性的统一）

- **治可达性**：root CLAUDE.md 让"裸自然语言"这个最大盲区有了必达指路牌；coding 必读顺序点名文件名，让 API/后端规范从"易漏"升到"可能达+"。
- **治简洁性**：入口只放指针（几十 token，AI 不会忽略）；规范正文瘦身去重（一条一处权威）；核对表只放判定点。**信息按"进入深度"分层供给**——AI 先读极简指路，需要某类规范时才展开那一份，而非一上来面对 1252 行全量。
- **一句话**：不是"把规范写全塞给 AI"，而是"**入口一张极简地图 + 规范按图索骥 + 关键路口设卡逐项核对**"。地图小到不会被忽略，规范深到够用，卡口硬到绕不过。

---

## 四、推荐执行顺序

| 优先级 | 动作 | 治什么 |
| --- | --- | --- |
| **P0** | 补 root `CLAUDE.md`（极简指路牌，20~30 行） | 最大盲区：裸自然语言不可达 |
| **P0** | coding 必读顺序把 api-standard/backend/frontend **按文件名点名** | 次要断点：API/后端易漏、缺开发前前置读 |
| P1 | 规范瘦身去重（前一轮瘦身方案：删 backend §5.4/§5.5/§4.3 专节等，一条一处权威） | 简洁性 + 防漂移 |
| P1 | 两个核对表对齐 + 删括号复述 | 已现漂移 |
| P2（可选） | `.claude/settings.json` 的 UserPromptSubmit hook 注入"先读 root CLAUDE.md" | 机器级兜底（比纯文档更硬，但引入 harness 依赖） |

> 注：这套"三层"策略要随 `dotnet new` 进生成项目才有效——root CLAUDE.md 需加入 template 生成物。这也回连到端到端更新闭环里"快照回流"的议题（[`2026-07-10-end-to-end-update-loop.md`](2026-07-10-end-to-end-update-loop.md)）。

---

## 五、一句话结论

**规范"够不到"比"不够简洁"更致命**——当前最大盲区是裸自然语言入口 + 通用铁律不可达，根因是无 root CLAUDE.md / 无会话级兜底，整套押在"模型选中 skill"一步上。推荐用**三层策略**同时治可达性与简洁性：**极简 root 指路牌（必达、几十 token）+ 规范正文瘦身按需展开（单一权威）+ skill 必读顺序点名文件 + 核对表逐项强制**。P0 是补 root CLAUDE.md 与 coding 必读点名，其余按序跟进。
