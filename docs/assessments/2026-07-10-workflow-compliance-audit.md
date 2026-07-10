# AI 工作流闭环与规范可遵守性审视

> 端到端审视：AI 按项目工作流交付一个需求，链路是否闭环、开发/需求/测试报告三类规范是否能被遵守。方法：①机制走查（门禁是否强制）+ ②对抗性压测（派 subagent 扮演"实现需求的 AI"，看它有/无规范时行为是否真变）。基于 2026-07-10 双 agent 实测。

---

## 一句话结论

**链路在文档层面完整闭环，规范对"愿意遵守的 AI"高度可遵守；但强制性 100% 依赖 AI 自觉执行 prompt 指令——没有任何机器级门禁（无 hook / settings.json / 校验器），所有 gate 都是 AI 自报字段。** 换言之：**引导做得极好，强制几乎为零。**

---

## 一、闭环完整性 —— ✅ 无结构断口

7 阶段状态机（requirement-plan→task-manager→coding→code-review→test-runner→deploy→task-manager 收口），每阶段产出物恰好是下一阶段输入：

- **路径表**（`agent-workflow.md:22-34`）给每类报告固定唯一文件名 `{req-id}-*.md`，下一阶段 SKILL 的「必读顺序」精确引用同名文件——产出→输入映射无断口。
- **统一交接包 YAML**（`agent-workflow.md:57-84`）作跨阶段粘合层，字段完整（gateStatus / nextRecommendedSkill / verification / blockers / retryCount），支持跨会话无损接续。
- **Must have 可全程追踪**：从 Plan 五段 → dev/test/acceptance 报告模板都强制 Must have 表，一路追到验收。

这是设计上最扎实的部分。

## 二、三类规范可遵守性 —— 压测视角（愿意遵守的 AI）

派 subagent 扮演"要实现『分页查询 + 订单创建时间 + 测试情况』的 AI"，结果：**前 4 项全部靠查规范拿到被明文约束的正确答案，而非常识猜测**。

| 规范点 | AI 靠什么得出 | 依据 |
| --- | --- | --- |
| 复杂需求先出 Plan + 人工确认才开发 | 查规范 | `agent-workflow.md:44,107,189` |
| 分页方法名 `GetPagedListAsync`（**否决** `GetListAsync`） | 查规范 | `api-standard.md:176-183` + `backend-develop.md:454` |
| 路由 `/api/v1/users` | 查规范 | `api-standard.md:174` |
| 禁 `DateTime.UtcNow`、用 `IClock` | 查规范 | `backend-develop.md:111-140` |
| 审计 `CreationTime` 不手写、由拦截器填充 | 查规范 | `backend-develop.md:176` + `CLAUDE.md` |
| 测试报告落 `docs/reports/tests/{req-id}-test-report.md` + Must have 映射 | 查 skill+模板 | `test-runner/SKILL.md:45` + 报告模板 |

**最强证据**：规范**显式写出了要否决的 AI 常识错误**（`而非 GetListAsync`、`❌ DateTime.UtcNow`）——它预判并拦住了 AI 最容易犯的错。这是"可遵守性"的最高形态。

**压测中"只能猜"的三处，均非规范缺陷**：①需求歧义（"创建时间"语义）→ 规范正确要求追问而非拍板；②项目实例配置（测试命令/覆盖率门槛）→ 规范明文"允许缺失+降级标注"；③框架 API 签名 → 规范主动推向随包文档。

## 三、强制性 —— 走查视角（不自觉的 AI 能否绕过）

压测的 AI 是"愿意遵守"的。走查回答互补的另一面：**门禁拦不拦得住不自觉的 AI？答案是拦不住。**

| 环节 | 必读强制 | 模板结构强制 | 门禁拒绝推进 | 真强制? |
| --- | --- | --- | --- | --- |
| 读 workflow 事实源 | 文字约定 | — | 无 | 软 |
| Plan 五段 + Must have 映射 | — | ✅ plan 模板强制五段 | needs-confirmation（自评） | 半强制 |
| 开发硬规则(命名/IClock/api-v1) | "只读相关部分" | ⚠️ dev-report 仅一行"规范对齐" | 无 | **弱** |
| code-review 规范审查 | "读相关清单" | 🔴 **无 review 报告模板** | 规范违规未映射 P0 | **最弱** |
| 测试执行/覆盖率 | 必读含 test 配置 | ✅ 报告模板强 | 🔴 默认无覆盖率门槛 | 半强制 |
| 验收收口 Must have 证据 | 收口前检查 | ✅ acceptance 模板强 | 证据齐才 done（自评） | 半强制 |

**强制性来源排序**：主要靠 **(b) 报告模板结构强制**（Plan/dev/test/acceptance 四份模板把关键字段列成表，逼 AI 填），其次 **(a) 必读顺序文字约定**；**(c) 机器门禁在本仓库不存在**（无 settings.json/hook/校验脚本，实测确认）；归根结底 = **AI 自觉 + 模板半推着走**。

---

## 四、断点与软肋（按严重度）

### 🔴 高
1. **所有 gate 是纸面门，无机器准入控制**（`agent-workflow.md:40,86-96`；无 hook/settings.json）。AI 可伪造 `gateStatus`、跳阶段、不读规范，系统无从拦截。
2. **开发硬规则不被报告逐项强制**：`backend-develop.md:539-563` 有详尽 checklist，但 `dev-report-template.md:29-34` 只用一行"规范对齐 pass/fail"概括，checklist 未被任何模板引用为必填项；coding SKILL "只读相关部分"进一步弱化。
3. **code-review 无报告模板 + 规范违规未映射 P0**：`code-review/templates/` 仅含 editorconfig/eslintrc 配置模板，无 review-report 结构模板；用 `GetListAsync`/`DateTime.UtcNow` 这类硬违规在 P0/P1/P2 分级表里无对应，易被降级为 P2 放行。**三类规范中约束最弱的一环。**

### ⚠️ 中
4. retryCount 防循环靠 AI 自维护（不累加即绕过）。
5. Plan 确认门是 AI 自评（无 registry 状态转换校验）。
6. 默认无覆盖率硬门槛（低覆盖率默认 pass + 记录）。
7. Must have "3-7 个"下限与 `{{占位符}}` 残留无校验。

### 低
8. 两份测试报告模板并存（`test.md:135-162` vs `test-report-template.md`），字段不一致，AI 选哪份有歧义。

---

## 五、修复方向（把"引导"升级为"强制"）

强制性缺口的本质：规范靠 prompt 引导、没有机器执行层。按性价比：

1. ✅ **已实施** — **code-review 补报告模板 + 规范违规映射分级**（治软肋 3，最高价值）：新增 `code-review/references/review-report-template.md`，把硬规则 checklist 作为**逐项勾选**纳入「规范硬规则核对」表；SKILL 分级表新增硬性条款「规范硬规则违反 = P1，不得降级为 P2」并点名 `GetListAsync`/缺 `/api/v1/`/`DateTime.UtcNow`/手写审计字段等；工作流程第 5 步 + 按需资源表引用模板，强调「逐条勾选，不得整体填一个 pass」。
2. ✅ **已实施** — **dev-report 展开为逐项自查**（治软肋 2）：`dev-report-template.md` 第 5 节的「项目规范对齐」从一行改为 §5.1「规范硬规则自查表」（与 code-review 核对表同源），开发阶段先自查、审查阶段再复核——同一套硬规则两道关。
3. ✅ **已实施** — **消除双测试报告模板**（治软肋 8）：`test.md §8` 的内联报告格式改为指向 test-runner 的 `test-report-template.md`（单一权威来源），不再维护第二份。
4. ⬜ **未实施（长期方向）** — **加机器门禁**（治软肋 1，成本最高）：若要真"硬门"，需引入 hook/校验脚本（如提交前校验 Plan 有 3-7 个 Must have、报告文件存在、无占位符残留）。这是从"prompt 强制"到"harness 强制"的跃迁——本体系刻意选择"模型自觉 + 模板引导"的轻量路线，加硬门禁会改变其形态，是否值得取决于对"不自觉 AI"的防范需求。**留待维护者单独决策。**

> 实施说明（2026-07-10）：第 1–3 项已落地，把强制力从「AI 自觉 + 一行 pass」提升到「模板逐项勾选」——AI 要跳过某条硬规则须显式写「违反/不适用」，绕过成本明显提高，且人类/后续 AI 一眼可见哪条未核对。软肋 4/5/6/7 与第 4 项（机器门禁）未动。

---

## 六、总评

- **对"配合的 AI"**：规范可遵守性 **优秀**——命名/路由/时间/响应/报告落点/流程门禁这些 AI 最易凭常识犯错的点，规范不仅给了正确答案，还显式否决了错误常识。压测证明 AI 能仅凭项目内 skill+规范正确干活。
- **对"不配合/图省事的 AI"**：强制性 **偏轻**——全链路无机器门禁，靠模板结构半推着走。经本轮补强（§五 1–3），code-review 已有报告模板 + 硬规则逐项核对 + 违规映射 P1，dev-report 亦展开逐项自查——从三类规范中最弱的一环提升为「模板结构强制」；仍未上机器门禁（软肋 1）。
- **闭环本身**：完整，无结构断口。
- **结论**：这是一套**"引导优先、强制轻量"**的工作流——设计理念自洽（信任模型自觉 + 用模板兜底）。本轮已把最高优先的补强（code-review 模板化、开发 checklist 逐项化、测试报告单一来源）落地；剩余的「机器硬门」属改变体系形态的长期方向，留待维护者决策。
