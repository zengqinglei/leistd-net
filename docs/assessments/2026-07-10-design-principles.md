# leistd-net 文档与 skill 设计原则（权威清单）

> 本文是 leistd-net 仓库**文档与 skill 设计原则的单一出处**。三层框架（components / ddd-struct / template）、三套 skill（框架使用 / 开发框架 / 项目级）、以及文档撰写与演进的所有决策，都应以本清单为准。
>
> 编号约定：**D**=贯穿性方法论/取向，**A**=框架组件分层，**B**=模板规范，**C**=skill。D0 是所有原则的第一约束。

---

## D0 — 一切文档/索引以「AI 可直接读」为第一设计约束

我们的文档、索引、导航，首要读者是 **AI（以及人）**。AI 的索引方式是**读自然语言 markdown + 顺链接跳转 + 理解语义**，不是解析结构化数据。所有文档决策的起点是「这份东西 AI 会怎么读到、读懂」，而非事后考虑。

- **不引入描述性/元数据文件**（`index.json`、`manifest.yaml`、自定义 schema…）。它们对 AI 不友好：
  1. **多一层间接**——AI 要先解析、再据此找内容，比直接读一篇讲清楚的 markdown 更易读漏读错；
  2. **制造新漂移面**——元数据与正文是两处需保持一致的事实源，又多一处会漂移；
  3. **不是 AI 的母语**——AI 对 markdown 叙述的理解远好于对自定义 json/yaml。
- **让文档本身成为好索引**：用 markdown 表格承载「能力→组件」映射、用稳定入口页 + 相对链接承载导航。人能读、AI 能读、无解析层。
- **机器只做两件事**：生成**给 AI 读的 markdown**、以及 **CI 校验**；机器**不产出给 AI 解析的结构化元数据**。

> 地位：D0 是第一约束。下面的 C（skill）、A3（示例自包含）、B1（SSOT）本质都是「为 AI 的读取方式服务」的具体化。

---

## A. 框架组件分层原则（components + ddd-struct）

### A1 依赖方向（不可违反）
- `*.Core` / `Leistd.Ddd.Domain` 是底层，不得反向依赖上层或具体实现。
- **`components` 不得依赖 `ddd-struct`**——单向 `ddd-struct → components`。
- ddd-struct 四层单向：`Domain ← Application(.Contracts) ← Infrastructure`。
- `*.Core` 必须平台无关；Web / EF / 重型 AOP 只出现在 `*.AspNetCore*` / `*.EntityFrameworkCore` 实现层。

### A2 组件独立闭环
- 每个家族职责单一、自成闭环；跨组件通过显式 `Add*/Map*` 组合，**不替他人代做注册/映射**（notifications 不代映射 realtime 的 Hub 是正面范例）。

### A3 文档示例自包含（A1 在文档层的延伸）
- 组件文档示例**只能用该组件 csproj 真实引用的类型 + 原生 .NET/EF Core**，禁止出现 ddd-struct 专属类型（`IRepository`/`BaseAppService`/`IAppService`/`Entity<>`/`PagedResultDto` 等）。
- EF 集成组件（auditing/unit-of-work）用原生 `DbContext` 合理；无关持久化的组件用中性类名（`OrderNotifier`，不用 `OrderAppService` 强套分层）。
- **判据：csproj 引用了什么，示例就只能用什么（+ 原生 .NET）。** DDD 分层的完整示范由 `ddd-struct.md` 唯一承载，组件文档只用一句叙述性 cross-link 指过去、不在示例代码里引入其类型。

### A4 NuGet 化
- 版本单点（`framework/common.props`）、CPM 统一第三方版本、docs 精确按家族名打包、元文档（`_doc-template.md` 等）不入包。

---

## B. 模板规范原则（template/docs）

### B1 单一事实源（SSOT）
- 同一规则只维护一处；重复则保留权威源、其余改 cross-link。三层各有权威源：组件用法→随包文档；框架开发→`development-guide.md`；项目工作流→`agent-workflow.md`；技术栈→`tech-stack.md`；命名→`document-naming.md`。

### B2 分类清晰
- 规则 vs 证据（`standards/` vs `reports/`）；跨需求 vs 单需求（`standards/` vs `requirements/`）；命名 kebab-case + 需求 `req-YYYYMMDD-NNN`。

### B3 渐进沉淀 + 可持续
- 项目级文档初始可缺失、按需沉淀；归档默认扁平（ID 自带时间序）、到期物理归档到 `archive/`、**不预建迭代目录**；需批次视角用 registry 加「迭代」列做逻辑聚合，不改物理层级。

### B4 可追踪闭环
- `registry ↔ context ↔ reports` 用同一 `req-id` 串起，从 plan 到 acceptance 可完整追溯。

---

## C. 三套 skill 的定位与共性原则

### C0 定位区分

| skill | 受众 | 定位 |
| --- | --- | --- |
| **框架使用 skill**（`leistd-net-framework` 分发版） | 消费 NuGet 的下游 AI | **纯知识索引**，引导定位随包文档，不复述正文、不臆造 API |
| **开发框架 skill**（`using-leistd-net-framework` 内部版） | 在框架仓内改框架的 AI | 指向 `framework/docs/` + `development-guide.md`，查源码补文档 |
| **项目级 skill**（template 内 6 个） | 最终业务项目的 AI | requirement-plan→coding→code-review→test-runner→deploy→task-manager **全流程闭环** |

### C1 description 规范
- 第三人称、只描述「何时用」、含用户自然语言触发短语、含「不适用」边界（指向相邻 skill）。

### C2 自包含
- skill 只引用**自身目录相对路径**或 `docs/` 项目文档；**禁止引用其他 skill 目录**。公共元规则用「每 skill 自包含副本 + 单一事实源」。

### C3 薄 SKILL + 按需 references
- SKILL.md 精炼（职责/门禁/规则指针）；操作细节放 `references/`，用「按需资源」表声明读取时机。

### C4 引用真实
- 所有引用路径必须真实存在；规则源单一，reference 只给操作细节、不复述规则源。

### C5 能力可发现性
- 对**框架不具备的能力**（幂等/防重、限流、缓存抽象、分布式事件）显式声明「未覆盖、勿臆造」。
- 对**跨家族能力**（领域事件 vs 应用事件、软删除字段填充 vs 自动过滤、事务定界）给分工表。
- 相邻易混组件（notifications ↔ realtime）加反向排除句。

### C6 不绑定外部工具
- 我们自己的 skill/文档**不引用外部工具的具体名**（如 superpowers），只描述中性的能力边界（「通用工程方法不在本 skill 范围」）。历史记录文件名/历史评估中的既有字样属事实记录，不事后篡改。

---

## D. 贯穿性方法论（除 D0 外）

### D1 以源码/事实为准，不臆造、不迎合
- 判断文档正确性时**亲自核对源码**（如确认 event-bus 无 `LogError`、tracing 无 `IProxyGenerator`、`BaseDbContext` 不自动挂拦截器）。
- 审查报告也可能误判——对「agent-workflow 章节重号」判定为误报（那些 `##` 在代码块内），不为迎合报告而改正确内容。

### D2 验证而非假设（evidence before assertions）
- 写完校验/修复**必做验证**，尤其**负向验证**。CI 漂移校验脚本正是靠「注入假 API 看能否抓到」这步，暴露了「只校验 206 个就假通过」背后的代码块反引号配对错位 bug，修复后校验数才跃到 1572。只跑正向「通过」就收工会交付空壳。

### D3 文档即索引，不造旁路清单
- （D0 的直接推论）导航与能力映射用 markdown 表格 + 入口页承载；否决 `index.json` 等旁路清单。

### D4 匹配问题规模，不过度设计
- 「清单驱动 + 生成式索引」是理论最优，但对当前 15 组件 + 4 层规模是过度设计；务实最优 = 即时高价值项先行（包归属表 + 漂移校验），其余按真实信号渐进。

### D5 强内聚基座：单份文档 + 文内归属，优于物理拆分
- ddd 四层是「搭一个 DDD 项目」的单一心智模型，物理拆分会割裂学习路径；保持单份 `ddd-struct.md` + 文内包归属标注，拿到绝大部分收益且不伤连贯。

### D6 诚实报告代价与边界
- 如实说明方案的固有成本（如漂移校验对散文有白名单维护成本），不把高误报工具硬塞流程；不确定的外部语义（如 deploy `requires`）不擅改，列为待确认项。

---

## 使用说明

- 新增/修改**组件文档**：查 A3 + B（撰写规范见 `framework/docs/_doc-template.md`）。
- 新增/修改**组件代码**：查 A1/A2/A4 + `framework/docs/development-guide.md`。
- 新增/修改**任何 skill**：查 C0–C6，并对照现有 `.claude/skills/*/SKILL.md` 与 `template/.claude/skills/*/SKILL.md` 的结构和边界。
- 演进**文档分发/索引机制**：查 D0/D3/D4/D5 + `2026-07-10-manifest-driven-doc-discovery.md`。
- 做任何**审查/修复**：查 D1/D2/D6。
