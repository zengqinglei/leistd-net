# 评估报告：docs 规范 skill 化 / 组件知识交付 / framework 单元测试

> 日期：2026-07-08
> 范围：`template/docs/` 规范体系、`framework/` 组件与文档、`framework/` 单元测试
> 性质：调研 + 方案评估（对本仓库自身的 meta-work，放 repo-root `docs/`，不进 template 的 registry —— 后者是生成给下游 app 的占位）
> 状态：评估定稿。作为后续三份独立实现 Plan 的共享 spec，等待用户确认推进范围。

---

## 0. 方法与依据

四路并行调研（均基于代码实测，非臆测）：
1. `template/docs/` 全部 standards/reports 文档 —— 逐份分类判定"该做 skill 还是留 doc"。
2. `framework/docs/components/` 与组件源码 —— 文档质量与交付形态（doc / skill / MCP）。
3. `framework/` 全部 33 源项目 + 4 测试项目 —— 单测缺口、优先级、覆盖率、性能、共享基类。
4. 业界参考 —— Anthropic Agent Skills、ABP Framework、PrimeNG/Angular Material、context7/MCP、Microsoft EF Core testing。来源见附录 B。

三项彼此独立、体量差异极大（第 1 项分钟级、第 3 项约 2 周/人）。**结论：不合并为一份 Plan；本报告是共享评估，后续拆三份独立 Plan 各自 spec→plan→执行。**

---

## 1. docs 规范是否该沉淀为 skill

### 结论：不新增 skill；仅补一处引用。

现有 6 个 workflow skill（requirement-plan → task-manager → coding → code-review → test-runner → deploy → task-manager）已把每份 standards 文档作为**参考知识**挂进对应阶段的"必读顺序"或 `reconcile-strategy.md`；五类报告（dev / code-review / test / deploy / acceptance）**已是强制工作流步骤且各有模板**——"自主沉淀"已经成立，非缺失。

### 判定表（节选，完整见调研）

| 文档 | procedure/reference | 已被 skill 覆盖 | 建议 |
| --- | --- | --- | --- |
| code-standard/{backend,frontend,common} | reference | coding/code-review 必读 | keep-doc |
| api-standard.md | reference | 6 skill reconcile 引用 | keep-doc |
| test.md（含报告模板 §8） | mixed | test-runner 模板已内化 | keep-doc |
| deploy/README.md（含流程 §6-8） | mixed | deploy skill workflow 已内化 | keep-doc |
| req-template-plan.md | 产物模板 | requirement-plan 模板 | keep-doc |
| tech-stack / project-structure | reference | 多 skill 引用 | keep-doc |
| **ui-design-strategy.md** | reference | **未被任何 skill 引用** | **补引用** |
| reports/*/README.md（5） | 占位 | 各对应一个 skill 的产物 | keep-doc |

### 业界印证

Anthropic：标准值不值得做成 skill = 是否是"反复调用的可执行流程"（→skill）vs"参考知识/少用/需判断"（→doc）；ABP 把约定留作 reference docs，用**模板生成器**去"执行"约定。你们的分工与之一致。

### 唯一动作（→ 第 1 项 Plan，分钟级）

在 `requirement-plan`（需求涉及 UI 时）和 `code-review`（前端 diff 审查时）的按需资源表补一行对 `ui-design-strategy.md` 的引用。不新建 skill。

---

## 2. framework 组件是否要 skill / MCP，如何更新

### 结论：加一个轻量"发现" skill + 补齐缺失文档 + 加 CI 同步闸门；MCP 暂缓。

组件文档**质量高**（严格套 `_doc-template.md`：API 表 / DI 注册 / 用法示例 / 注意事项，AI 可直接照用）。但有三个真问题：

1. **4/14 家族零文档**：`auditing`、`authorization`、`notifications`、`realtime` 是真实项目却无 doc、不在 README 索引。
2. **无发现索引**：AI 不知道去 `docs/components/` 查，遇缺文档家族会静默臆答。
3. **无 CI 同步闸门**：README 的"改组件同步文档"仅是约定，`release.yml` 不触发任何 docs 检查，文档会静默漂移。

### 交付形态对比（人 + AI 双受众）

| 形态 | 人 | AI | 维护 | 随代码同步 |
| --- | --- | --- | --- | --- |
| 纯 Markdown（现状） | 好 | 好但无发现入口，4 家族返回空 | 低但**无强制** | 仅手工 |
| **+ 发现 skill** | 中性偏正 | 高（解决发现问题，镜像已有 using-leistd-workflow） | 低（只索引不复述） | 索引表小、可 CI 校验 |
| MCP server | 低 | 结构化大表查询才高 | 高（进程+生成管线+版本） | 生成式最佳但重 |

### 业界印证

PrimeNG/Angular Material 的 API 表**从源码生成**（decorator/JSDoc）以防漂移；MCP（context7 式）只在"库多、变化快、跨库查询"时才划算。你们自有框架、~29 项目、12 文档 —— **docs（真源）+ 薄 skill（发现）+ CI 闸门**足够；MCP 是当前的过度工程。

### 关键修正：两类受众，发现 skill 只服务其一

> 后续实测补充（对本节初版结论的重要修正）：必须区分**两类受众**，`using-leistd-components` 发现 skill **只解决其中第一类**。

- **受众① 框架仓库自身开发**（我们在 `leistd-net` 里改框架）：skill + `framework/docs/components/*.md` 有效。发现 skill 服务的是这一类。
- **受众② 下游包消费者**（`dotnet add package Leistd.*` 的用户项目）：**skill 与 `framework/docs/` 都不会进入其项目**。实测确认交付缺口：
  1. NuGet 包除 dll 外只打**一份共用的 `framework/NuGet.md`**（`common.props:61`），无组件级 API 指引。
  2. `framework/docs/components/*.md`（质量高）**不进包**，且被 release CI 显式排除（`release.yml:58` 过滤 `framework/docs/` 与 `*.md`）。
  3. 每包带 XML doc（`GenerateDocumentationFile=true`），但编译器/IDE 能读、**AI 不会主动去 `~/.nuget/packages/**/*.xml` 翻**。
  4. `template/backend/` 脚手架**无 CLAUDE.md / AGENTS.md**，下游 AI 无"用了哪些 Leistd 组件、去哪查用法"的入口。

  → 结论：下游 AI 想知道 `[UnitOfWork]` / `BusinessException.WithCode` 用法**当前无路可走**。这是初版 Q2 的盲点，需用**独立于发现 skill 的下游交付链路**补上。

### 动作（→ 第 2 项 Plan）

**受众①（框架仓库开发）**
1. 新增 `using-leistd-components` 发现 skill（repo-root `.claude/skills/`，镜像 `using-leistd-workflow`）：一张"家族→doc 路径→一句话定位→状态(有/无文档)"表 + "缺文档先读源码 XML 注释再照 `_doc-template.md` 补，不臆造"的自愈规则。**明确限定其适用边界为框架仓库自身开发，非下游交付方案。**
2. 补齐 4 个缺失家族文档（auditing / authorization / notifications / realtime）。
3. 加 CI 检查：`framework/components/<家族>/` 无对应 doc 或 skill 表无该行 → 失败。

**受众②（下游包消费者）—— 治本必做**
4. **docs 随包分发（治本）**：把 `framework/docs/components/*.md` 作为内容文件打进对应 NuGet 包（如 `PackagePath="docs\"`），并改掉 release CI 对 `framework/docs/` 的排除。用户 restore 后 `~/.nuget/packages/<pkg>/<ver>/docs/` 即有权威用法文档。
5. **脚手架 AI 入口（治本必做）**：给 `template/backend/` 预置 `CLAUDE.md`（或 `AGENTS.md` 跨工具通吃）—— 一张"本项目引用的 `Leistd.*` 组件清单 + 一句话定位 + 用法指引定位（指向动作 4 的 nuget 缓存 docs 或线上 URL）"。形成"入口→定位→精确 API"的完整识别链路。
6. **XML doc 补齐 + 引导（低成本增益）**：按开发规范补全公共 API 的 `///` 注释（现 CS1591 被 NoWarn 容忍，多有缺失）；在动作 5 的 CLAUDE.md 里指示 AI"精确签名看同名 `.xml`"。
7. **线上文档兜底（补充）**：`common.props` 已有 `PackageProjectUrl`；在 NuGet.md / CLAUDE.md 放稳定组件文档 URL 供联网 AI 兜底（不作唯一手段，离线/内网失效）。

**其余**
8. **MCP 暂缓**（backlog）。触发条件：框架规模显著增长，或需跨全组件 API 结构化查询（如"哪些组件注册 Redis 依赖"）到 grep/Read 力不从心时；届时 skill 索引表即 MCP 资源目录种子。
9. 文档仍**手写对齐源码**（`_doc-template.md` 的"AI 友好但不臆造"），不自动从 XML 注释生成 prose；XML 注释仅作交叉核对。

> 最小可行首步（受众②）：**动作 5（脚手架 CLAUDE.md 入口）+ 动作 4（docs 随包分发）** 配合，才能让下游 AI 形成完整识别链路。

---

## 3. framework 单元测试补齐

### 结论：系统性补测，分层推进；先搭骨架，且**先修 CI**。

实测：**33 源项目仅 4 测试项目**。**最严重发现：CI 无 `dotnet test`** —— `.github/workflows/release.yml` 直接 checkout→pack→push，现有测试与未来所有测试只靠本地手动跑，零强制。

### 关键设计决策（结合业界）

| 议题 | 方案 | 依据 |
| --- | --- | --- |
| **先修 CI** | 加 PR 触发 `dotnet test` 步骤，优先级高于补测本身 | 实测 CI 缺口 |
| **DB 测试选型** | 弃用 EF InMemory（现状）；纯逻辑用手写 fake；EF 逻辑用 InMemory 仅限非事务分支；**事务分支用 SQLite in-memory** | Microsoft + ABP 均明确 discourage InMemory provider |
| **覆盖率门槛** | 分层：Tier A 95%/90%（UnitOfWork.Core、Exception.*、Security.Core、Auditing.EfCore、DI.DynamicProxy、Tracing.Core）；Tier B 80%/70%；Tier C 不设（接口/DTO/marker）。coverlet + `.runsettings` + CI fail-under | 模板 coverage-thresholds-template + coverlet 惯例 |
| **共享测试基类** | 要但小：`Leistd.TestBase` 只放已被重复发明的 fake `IClock`/`ICurrentUser`、InMemory DbContext 工厂、`ILocalEventBus`/`IUnitOfWork` fake；不做通用 mega-fixture | ABP 分层 TestBase，按你们轻量风格裁剪 |
| **Mock 库** | 不引入 NSubstitute/Moq；续用手写 fake（接口 2-6 成员，`RealTime.Tests/TestDoubles.cs` 已证明够用） | 现状一致性 |
| **性能** | 全程内存态，全套应 &lt;1min；注意两处静态缓存污染（LocalEventBus `_wrapperCache`、SignalRPresenceService 静态字典），新测试用 per-test 唯一 key | 实测风险点 |

### 优先级（价值最高优先）

UnitOfWork.Core → Exception.*（`ConvertToBusinessException` 分支顺序风险影响所有 API 错误响应）→ DI.DynamicProxy（AOP 静默失效）→ Auditing.EfCore / Ddd.Infrastructure 持久化（`EfCoreRepository.SaveChangesIfNeededAsync` UOW 分支，回归会双写或静默不持久化）→ Security.Core / Response.* → Tracing.Core（AsyncLocal）→ 其余 P0 批量（Core/DI/object-mapping/Ddd.Domain DataFilter）→ 补已测家族缺口（authorization 内部、event-bus 多 handler、realtime presence 多连接计数）→ Lock → Notifications / UnitOfWork.EfCore 事务分支。

### 动作（→ 第 3 项 Plan，本轮仅"骨架"）

**骨架（本轮）**：
1. 加 PR 触发的 CI `dotnet test` 步骤。
2. 加 `framework/.runsettings` + `coverlet.collector`（CPM）+ 分层覆盖率门槛与 fail-under 校验。
3. 建 `framework/tests/Leistd.TestBase`（fake `IClock`/`ICurrentUser`、InMemory DbContext 工厂、`ILocalEventBus`/`IUnitOfWork` fake；可直接复用/迁移 `RealTime.Tests/TestDoubles.cs`）。

**后续（独立分批，非本轮）**：按优先级批量补测；首轮"每家族有意义的 P0 覆盖"约 2 周/人。

---

## 4. 风险及应对策略

| 风险 | 概率 | 影响 | 应对 | 触发后处理 |
| --- | --- | --- | --- | --- |
| 三项混在一份 Plan 导致大项拖住小项 | 高 | 中 | 拆三份独立 Plan，第 1/2 项可立即做，第 3 项独立 | 已在本报告确立拆分 |
| CI 加 `dotnet test` 拖慢发布流水线 | 中 | 低 | 拆独立 PR 触发的 ci.yml，与 release.yml 分离；全内存态套件 &lt;1min | 慢测试打 `Category=Integration` 隔离 |
| 分层覆盖率门槛设置过严挡住合并 | 中 | 中 | Tier C 不设门槛；A/B 阈值可先观察再收紧 | 项目级 `.runsettings` 覆盖 |
| 补 4 家族文档时臆造 API | 中 | 中 | 强制照源码 XML 注释 + `_doc-template.md` 骨架；skill 自愈规则明确"缺则读源码不臆造" | code-review 拦截 |
| 弃用 EF InMemory 改 SQLite 增加改造量 | 中 | 低 | 仅事务分支用 SQLite，其余仍 InMemory；骨架期不铺开 | 分批推进 |
| 静态缓存导致测试并行污染 | 中 | 中 | per-test 唯一 key（现有约定）；文件内注明约束 | 必要时按 collection 串行 |
| MCP 过早引入徒增维护面 | 低 | 中 | 本轮明确暂缓 + 写清触发条件 | 达到触发条件再评估 |

---

## 5. 验收闭环

### Must have

| # | Must have | 映射 | 验证方式 |
| --- | --- | --- | --- |
| M1 | 三项分析均有代码实测依据与业界参考，结论可复核 | §1/§2/§3 + 附录 B | 本报告评审 |
| M2 | 第 1 项：明确"不新建 skill，仅补 ui-design 引用"及落点 | §1 动作 | 后续 Plan-1 |
| M3 | 第 2 项：区分两类受众——发现 skill 只服务框架仓开发；下游消费者需 docs 随包分发 + 脚手架 CLAUDE.md 入口 + XML doc 引导；MCP 暂缓及触发条件明确 | §2 动作 + 关键修正 | 后续 Plan-2 |
| M4 | 第 3 项：明确"先修 CI + .runsettings 分层门槛 + TestBase 骨架"，及后续补测优先级 | §3 动作 | 后续 Plan-3 |
| M5 | 三项拆为独立 Plan，互不阻塞 | §0/§4 | 本报告确立 |

### Nice to have

| # | Nice to have |
| --- | --- |
| N1 | 组件文档 API 表长期可考虑半自动从 XML 注释交叉校验 |
| N2 | 覆盖率趋势用 ReportGenerator 可视化（非门槛，仅观测） |

### 完成定义（本评估）

- 本报告经用户确认；三项推进范围与先后已定。
- 不在本报告内实施任何代码/文档改动（纯评估）。
- 确认后各项转独立 spec/plan。

---

## 附录 A：关键事实（实测）

- framework：29 组件项目 + 4 ddd-struct = 33 源项目；仅 4 测试项目（Authorization / Ddd.Infrastructure / EventBus / RealTime）。
- 测试栈：xUnit + EF InMemory + MS.DI，无 mock 库，手写 fake（`RealTime.Tests/TestDoubles.cs`）。已有 `Directory.Build.props` + `Directory.Packages.props`（CPM）。
- **CI 无 `dotnet test`**（`.github/workflows/release.yml` 仅 pack/push）。无 `.runsettings`、无 coverlet 配置。
- 组件文档 12 份，质量高但 4 家族（auditing/authorization/notifications/realtime）零文档、无机读元数据、无 CI 同步闸门。
- `ui-design-strategy.md` 是唯一未被任何 skill 引用的 standards 文档。
- Microsoft + ABP 均 discourage EF Core InMemory provider，推荐 SQLite in-memory / Testcontainers。

## 附录 B：来源

- Anthropic：Equipping agents with Agent Skills；Effective context engineering；Code execution with MCP；Complete Guide to Building Skills。
- Red Hat Developer：MCP servers vs. skills。
- ABP.IO：Module Best Practices & Conventions；Automated Testing / Integration Tests / Unit Tests。
- PrimeNG（primeng.dev/table）；Angular Material（material.angular.dev；angular/components）。
- Context7（upstash/context7）。
- Microsoft Learn：Choosing a testing strategy (EF Core)；Use code coverage for unit testing (.NET)。
- coverlet（coverlet-coverage/coverlet）；Code Maze / genezini .NET coverage。
