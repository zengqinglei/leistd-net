# Skills 与文档体系 — 分层架构与引用关系设计方案

> 日期：2026-07-08（重构版）
> 范围：框架三层（通用组件 / DDD 组件 / 模板）、层内 skill、六条引用链路（skill↔文档、nuget↔文档、用户项目↔包/skill）、分发与发现、单一来源、实施计划
> 性质：架构设计文档，含实施计划。基于代码实测（引用链路逐条验证 + 生成探针）+ 业界参考。来源见附录。
> 状态：设计定稿，等待用户确认后按实施计划分阶段执行。

---

## 0. 一句话结论

框架分**三层**（通用组件 → DDD 组件 → 模板），三层都以 `Leistd.*` NuGet 包对外、模板经 `PackageReference` 消费。当前**六条引用链路里有三条是断的**：nuget 包不含组件文档、用户项目无 AI 入口(skill/CLAUDE.md)、`framework/docs` 无 skill 索引。目标架构 = **让文档随包走、让 skill 随生成/随包走、一份文档喂所有入口**，并把 6 个 app 治理 skill 从"被生成排除"改为"随生成进入用户项目"。

---

## 1. 框架分层（实测边界）

```
leistd-net/  (整个仓库 = .NET 版；Java 版对应 leistd-java-*)
│
├── framework/                          ← 独立版本化(VERSION=0.10.3),以 Leistd.* NuGet 发布
│   ├── components/     [第1层 通用组件]  15 家族 / ~29 包  (aop/core/lock/security/tracing/event-bus/uow/…)
│   └── ddd-struct/     [第2层 DDD 组件]   4 包  (Leistd.Ddd.Domain/Application/Application.Contracts/Infrastructure)
│
└── template/           [第3层 模板]      dotnet new fullstack-app → 生成业务应用
    └── backend/  经 PackageReference 引用 Leistd.* (NuGet 模式)  ← 消费第1、2层
```

三层关系:**通用组件**是地基(无业务假设);**DDD 组件**建在通用组件之上(提供 Domain/Application 四层基类型);**模板**消费前两层、生成业务 app。三层都是对外交付面(components + ddd-struct 均 `IsPackable`)。

---

## 2. 六条引用链路（当前事实 + 通/断判定）

| # | 引用链路 | 当前机制 | 状态 |
| --- | --- | --- | --- |
| L1 | 模板 backend → 框架包 | `PackageReference Include="Leistd.*"`,CPM 单点版本 `LeistdFrameworkVersion=0.10.3`;另有本地源码模式(ProjectReference)供框架联调 | ✅ 通 |
| L2 | nuget 包 → 文档 | 每包只打一份共用 `framework/NuGet.md`(`PackageReadmeFile`) + XML doc(`GenerateDocumentationFile=true`) | ⚠️ 半通(无组件级文档) |
| L3 | 框架源码 → 组件文档 | `framework/docs/components/*.md`(11 篇,4 家族缺;ddd-struct 仅 1 README);文档自包含(无相对链接依赖) | ⚠️ 有缺口 |
| L4 | 组件文档 → nuget 包(随包分发) | **无**——组件文档不进包,且被 release CI(`release.yml:58`)排除 | ❌ 断 |
| L5 | 用户项目 → skill | **无**——生成 app 无 `.claude/`(被 `template.json:95` `**/.claude/**` 排除),无 CLAUDE.md/AGENTS.md | ❌ 断 |
| L6 | skill → 框架文档 | **无**——无任何 skill 引用 `framework/docs/`;framework 知识对 AI 无发现入口 | ❌ 断 |

**核心矛盾(实测确认)**:生成探针 app 里 `docs/`(app 治理文档)**在**、`.claude/`(6 个执行 skill)**不在** —— 用户拿到规则书却没拿到执行者(L5 断);同时用户经 L1 装了 `Leistd.*` 包,却因 L4 断而拿不到组件用法文档、因 L6 断而无 AI 发现入口。

---

## 3. skill 拓扑与两个直接裁决

### 3.1 现状(7 skill)

| skill | 位置 | 服务 | 状态 |
| --- | --- | --- | --- |
| using-leistd-workflow | `.claude/skills/`(repo root) | 框架仓开发者 | ✅ 正确(hook 强制发现) |
| coding/code-review/test-runner/deploy/requirement-plan/task-manager | `template/.claude/skills/` | **内容为生成 app 写,却被生成排除** | ❌ L5 断 |

### 3.2 裁决一:6 个 workflow skill 不合并

对照 Anthropic:一 skill 一能力、description 只写"何时用"、渐进披露、阶段门禁契约(`nextRecommendedSkill`/retryCount/P0 弹回)都依赖它们是独立可推荐单元。合并会让"推荐下一个 skill"变"推荐自己",契约失效。粒度正确,保持 6 个。

### 3.3 裁决二:改分发,而非挪位置

这 6 个是**为生成 app 写的 app 治理 skill**(正文引用 `docs/requirements/{req-id}-plan.md`、"不把模板默认栈当作未知项目的事实"),被错当 repo 工具剔除。修法:**随生成进入用户项目**(见 §5 L5 修复),不是合并、不是简单挪到 repo-root。

### 3.4 命名约定(跨语言 + 交付面)

框架有 .NET/Java 两版。对外发布 skill 名 = **`leistd-net-framework`**:`leistd-net`=.NET 版仓库名(Java 版 `leistd-java-*`),`-framework` 标框架交付面(components+ddd-struct,区别于未来可能的 app 治理发布包)。框架仓内发现 skill 对齐为 `using-leistd-net-framework`。

---

## 4. 目标架构:三层各自的 skill / 文档 / 引用

### 4.1 分层职责与产物

| 层 | 文档产物(单一来源) | skill 产物 | 随什么分发 |
| --- | --- | --- | --- |
| **第1层 通用组件** | `framework/docs/components/*.md`(补齐至每家族一篇) | 并入 `leistd-net-framework` 发布 skill 的组件条目 | 随对应 `Leistd.*` 包(L4 修复) + npx/发布(L6 入口) |
| **第2层 DDD 组件** | `framework/docs/ddd-struct/*.md`(补齐至每项目一篇) | 并入同一发布 skill 的 ddd 条目 | 随 `Leistd.Ddd.*` 包 + 同上 |
| **第3层 模板** | `template/docs/`(app 治理,已随生成分发) | 6 个 workflow skill(随生成进用户项目)+ 用户项目 AI 入口 CLAUDE.md | 随 `dotnet new` 生成 |

### 4.2 目标 skill 拓扑(三处,按用途)

```
leistd-net/
├── .claude/skills/                          # 仓库内部工具(框架仓开发者)
│   ├── using-leistd-workflow/               #   已有:工作流发现
│   └── using-leistd-net-framework/          #   新增:框架知识发现(索引 components+ddd-struct 文档,L6 修复)
│
├── template/.claude/skills/                 # app 治理 skill 源(6 个)
│   └── (coding/code-review/…/task-manager)  #   → 目标:随生成进入用户项目的 .claude/skills/(L5 修复)
│
├── skills/leistd-net-framework/             # 对外发布 skill(框架包消费者)
│   ├── SKILL.md                             #   薄索引;npx skills add zengqinglei/leistd-net
│   └── <每家族/每 ddd 项目一条目>.md          #   从 framework/docs 生成/薄封装,不复制正文
│
└── (生成的用户 app)/
    ├── .claude/skills/                      #   ← 6 个 app 治理 skill(随生成落地,L5)
    ├── docs/standards/…                     #   ← app 治理文档(已随生成)
    └── CLAUDE.md                            #   ← 新增:AI 入口,指引"用了哪些 Leistd 包、去 nuget 缓存/skill 查用法"(L5/L6 下游侧)
```

### 4.3 单一来源:一份文档喂三入口(不复制)

`framework/docs/{components,ddd-struct}/*.md` = **唯一权威源**。三个消费入口都指向/薄封装它,禁止手工 fork 正文:
- 入口①(框架仓开发,L6):`using-leistd-net-framework` 发现 skill **只索引**。
- 入口②(对外发布,npx):`skills/leistd-net-framework/` 条目**从同一批 md 生成/薄封装**(daisyUI 式)。
- 入口③(随包,L4):同一批 md 作为内容文件打进对应 `Leistd.*` 包(`PackagePath="docs\"`),AI 从 nuget 缓存读。

### 4.4 分发通道(用对工具)

| 消费者 | 通道 | 依据 |
| --- | --- | --- |
| 框架仓开发者 | repo-root `.claude/skills/` + SessionStart hook | 已验证,与 superpowers 共存 |
| 生成 app 开发者 | 6 skill **随生成**进用户 `.claude/skills/` + 用户项目 CLAUDE.md | 用户拿到完整仓,文件系统直接有;免 registry |
| 框架包消费者(NuGet) | **组件文档随包**(`nuget-skills` 通道,`PackagePath` 打进 .nupkg,版本随包锁定)+ 可选 `npx skills add` | dotnet/skills 官方通道(2026-03),.NET 原生,防版本漂移 |

### 4.5 暂缓项(触发条件)

- **MCP server**:暂缓。触发——组件规模显著增长或需跨全组件结构化查询到 grep/Read 力不从心;届时 skill 索引即 MCP 资源目录种子(daisyUI Blueprint MCP 为参照)。
- **llms.txt**:低成本联网兜底,非主路径。

---

## 5. 六条链路的修复映射

| 链路 | 目标 | 修复动作 |
| --- | --- | --- |
| L1 模板→包 | 保持 | 无(已通) |
| L2 包→文档 | 每包有组件级文档指引 | 由 L4 顺带解决(组件 md 进包) |
| L3 源码→组件文档 | 每家族/每 ddd 项目一篇 | 补齐 4 缺失家族 + 4 个 ddd-struct 项目文档 |
| L4 组件文档→包 | 文档随对应包分发 | 解除 release CI 打包排除;`common.props` 加 `PackagePath="docs\"` 打包对应家族/项目文档 |
| L5 用户项目→skill | 生成 app 带 6 skill + CLAUDE.md | `template.json` 为 `template/.claude/skills/**` 开例外(仅 skills 随生成);新增 `template/backend/CLAUDE.md`(或根)AI 入口 |
| L6 skill→框架文档 | 框架知识有发现入口 | 新增 `using-leistd-net-framework` 发现 skill(索引 §4.3 单一来源) |

---

## 6. 实施计划(分阶段,按依赖排序)

> 每阶段独立 spec/plan,互不阻塞;含验证方式。

| 阶段 | 内容 | 修复链路 | 依赖 | 体量 |
| --- | --- | --- | --- | --- |
| **P0 (已完成)** | 补 ui-design 引用 | — | — | ✅ |
| **P1 文档补齐** | 补 4 缺失家族(auditing/authorization/notifications/realtime)+ 4 个 ddd-struct 项目文档,照 `_doc-template.md`;更新组件/ddd 索引 | L3 | 无 | 中 |
| **P2 框架发现 skill** | 新增 `using-leistd-net-framework`(repo-root,索引 components+ddd-struct 文档,自愈规则"缺则读源码不臆造") | L6 | P1 | 小 |
| **P3 app 治理 skill 随生成** | `template.json` 精确例外 `template/.claude/skills/**` 随生成;探针 app 验证只多 skills、其余 `.claude` 内容仍排除;验证 skill→doc 引用在生成 app 内解析 | L5(skill 侧) | 无 | 小 |
| **P4 用户项目 AI 入口** | 新增 `template/backend/CLAUDE.md`(随生成):列本项目引用的 `Leistd.*` 包 + 一句话定位 + "用法见 nuget 缓存 docs/ 或发布 skill";可用模板条件块按启用组件生成 | L5(下游侧)/L6 | P3 | 小 |
| **P5 文档随包** | 解除 release CI 对 `framework/docs` 打包排除;`common.props` 按项目打包对应家族/ddd 文档(`PackagePath="docs\"`);探针装包验证 nuget 缓存有文档 | L4/L2 | P1 | 中 |
| **P6 对外发布 skill** | `skills/leistd-net-framework/`(从 P1 文档生成/薄封装的薄索引)+ README 发布指引 + `npx skills add` 验证 | L6(对外) | P1 | 中 |
| **P7 CI 同步闸门** | 组件/ddd 无文档或无索引条目→CI 失败;文档不再被打包流程静默排除 | L3/L4 一致性 | P1/P5 | 小 |
| **(并行)P8 单测骨架** | 修 CI dotnet test + 分层覆盖率 + Leistd.TestBase(见 2026-07-08 单测评估) | — | 独立 | 大 |
| **(并行)治理** | root `docs/` 加 README 索引 + 命名/生命周期治理 | — | 无 | 小 |

**建议顺序**:P1(补文档,一切下游的前置)→ P3(解生成排除,收益最大改动最小)→ P2(框架发现 skill)→ P4(用户 AI 入口)→ P5(文档随包)→ P6(对外发布)→ P7(闸门固化);P8/治理并行。

---

## 7. 风险与应对

| 风险 | 概率 | 影响 | 应对 |
| --- | --- | --- | --- |
| L5 改 template.json 例外误带其他 `.claude` 内容进 app | 中 | 中 | 例外精确到 `template/.claude/skills/**`;探针 app 验证只多 skills |
| 6 skill 进 app 后其"单一事实源"路径需在 app 内成立 | 中 | 中 | 已实测 `docs/` 随生成分发,路径成立;P3 探针再验 skill→doc 解析 |
| L4/发布通道版本漂移(skill/文档比装的包新) | 中 | 高 | nuget-skills 随包锁版本;发布 skill frontmatter 标目标框架大版本 |
| 一源多入口退化为多份复制 | 中 | 中 | 发布/随包内容**从 framework/docs 生成或薄封装**,CI 闸门(P7)校验一致 |
| 文档粒度(家族)与包粒度(项目)不一致:一家族 md 打进该家族多个包 | 中 | 低 | 按"同家族每个包都内置该家族 doc"(用户装任一包都能拿到);体积小可接受 |
| 解除 CI 打包排除误触发版本发布 | 中 | 中 | 区分"版本触发排除"(保留)与"打包内容排除"(解除);二者独立 |

---

## 8. 验收闭环

### Must have
| # | Must have | 映射 |
| --- | --- | --- |
| M1 | 框架三层边界 + 六条引用链路事实与通/断判定成文(实测) | §1/§2 |
| M2 | 6 skill 不合并、改分发不挪位置的裁决与依据 | §3.2/3.3 |
| M3 | 三层各自 skill/文档/引用的目标结构 + 单一来源多入口 | §4 |
| M4 | 六条断链的修复动作逐条映射 | §5 |
| M5 | 实施计划分阶段、按依赖排序、互不阻塞,每阶段可验证 | §6 |
| M6 | 命名(leistd-net-framework)覆盖 components+ddd-struct 且区分语言 | §3.4 |
| M7 | MCP/llms.txt 暂缓 + 触发条件 | §4.5 |

### 完成定义(本设计)
- 经用户确认;各阶段拆独立 spec/plan。纯设计,不在本文件实施代码(P0 已单独完成)。

---

## 附录:关键实测事实

- 框架:components 15 家族(~29 包)+ ddd-struct 4 包,均 `IsPackable`,独立版本(VERSION=0.10.3)。
- L1:`template/backend` 经 `PackageReference Include="Leistd.*"` 消费框架,CPM 单点 `LeistdFrameworkVersion`;另有本地源码 ProjectReference 模式(框架联调)。
- L2:每包只打共用 `framework/NuGet.md` + XML doc;无组件级文档。
- L3:组件文档 11 篇(4 家族缺:auditing/authorization/notifications/realtime),ddd-struct 仅 1 README;文档自包含(无相对链接),可直接随包。
- L4:组件文档不进包,`release.yml:58` 排除 `framework/docs/` 与 `*.md`。
- L5:生成探针 app 有 `docs/`、无 `.claude/`(`template.json:95` `**/.claude/**`);`template/backend` 无 CLAUDE.md/AGENTS.md。
- L6:无任何 skill 引用 `framework/docs/`。

## 附录:业界来源(节选)

- Anthropic Agent Skills 开放标准(2025-12-18)、authoring best practices、progressive disclosure。
- Vercel Labs `npx skills`(GitHub 即注册表);daisyUI(`npx skills add` + llms.txt + Blueprint MCP)。
- dotnet/skills(官方,2026-03)+ nuget-skills + managedcode/dotnet-skills:.NET 原生随包分发、git-tag 版本锁定。
- 成熟 .NET 项目通常使用分层 TestBase，并由模板生成器执行约定。Red Hat/Speakeasy:MCP vs skills 选型。
- 完整 URL 见本会话评审 agent 报告。
