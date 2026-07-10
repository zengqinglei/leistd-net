# docs 与 skills 信息架构重规设计

> 日期：2026-07-08
> 范围：`template/docs/` 与 `template/.claude/skills/` 的信息架构（精简 / 可靠 / 不冗余 / 层次少 / 人与 AI 易查）
> 参考模型：daisyUI skill（渐进式披露——小而单一职责的文件，薄索引按需加载）+ Anthropic skill authoring
> 性质：设计文档，等用户确认后实施。基于代码实测（行数/结构/引用耦合/逐行冗余核对）。
> 状态：定稿待审。不含代码改动。

---

## 0. 一句话结论

**目录形状已达标（≤2 层，与 `project-structure.md` 一致），不动目录。** 真正的问题在**文件内部**（`backend-develop.md` 747 行含 22% 纯重述）和**可靠性缺陷**（悬空引用、空孤儿目录、手写目录漂移）。本轮 = 删冗余 + 修缺陷 + 去噪音 + 补索引，零目录层级变化。

---

## 1. 设计原则(本轮遵循)

对照 daisyUI / Anthropic 的 AI 阅读-执行机制,三条判据:
- **加载粒度 = 使用粒度**:AI 用某条规则时,不应被迫把无关内容读进上下文。
- **可检索定位**:靠标题层级 + 索引 + grep,不靠手写目录。
- **规则是可执行指令,不是叙述**:rule / procedure / template / index 分清,不混文档类型。

并继承既有原则:单一来源不复制、层次尽量少、高风险先确认、最小改动。

---

## 2. 现状实测(证据)

| 文档 | 行数 | 结构问题 |
| --- | --- | --- |
| `code-standard/backend-develop.md` | **747** | §7 重构指南 162 行(22%)纯重述 §3/§4/§5;§6.1 异常表与 api-standard §4 重复且**少一行**(缺 ConflictException/409);手写目录;同一"App 层禁引 EF"规则重述 5 次 |
| `guides/create-app/frontend-create.md` | 545 | 主要是可粘贴脚手架片段,无内部冗余,**健康**(不拆) |
| `api-standard.md` | 242 | §8 API 文档模板与 `modules/_template/api.md` 部分重叠,且分页约定不一致(§8 用 offset/limit,模块模板用 page/pageSize) |
| `code-standard/frontend-develop.md` | 240 | **健康**,无 mis-mix、无内部冗余 —— 作为"多大算健康"的基准 |
| 其余 ≤173 | — | 健康,不动(拆 47 行文档是过度工程) |

目录深度最深 2 层,与 `project-structure.md` 声明一致 —— **最小合理深度,拍平会把 ~25 文件堆平反而更难找**。

---

## 3. 确证的问题(按严重度)

1. **悬空引用 `docs/deploy/release-policy.md`**:被 `requirement-plan/templates/agent-workflow-template.md`(L75/L260)引用,但文件不存在 —— 每个生成项目从第一天带坏指针。
2. **`docs/reviews/` 空孤儿目录**:零文件、零引用。
3. **`backend-develop.md` §7(162 行)= procedure 混进 rule 文档 + 纯重述**:全库最大单点冗余。
4. **`backend-develop.md` §6.1 异常表 = api-standard §4 的发散部分副本**(4 行 vs 5 行,缺 409)—— 不只冗余,是**不一致风险**:只读 backend 文档的 AI 会漏掉 409。
5. **手写目录锚点**(backend/frontend-develop.md):随标题编辑静默失同步,AI 不需要。
6. **两处 API 文档模板部分重叠 + 分页约定不一致**(api-standard §8 vs modules/_template/api.md)。
7. **`docs/README.md` 阅读路径表漏 3 个 meta 文档**(classification/naming/skill-authoring)—— 二跳才能发现。
8. **`guides/` 下 create-app/ vs frontend/ 无索引** —— 轻微查找税。

---

## 4. 已知冗余,刻意接受(不在本轮处理)

- **6 份 `reconcile-strategy.md` 公理段逐字重复(~264 行)**:用户已明确决定**保持每 skill 自包含副本**(理由:skill 可独立分发、零跨 skill 引用;`skill-authoring.md` §2 据此立约)。本设计**尊重该决定,不抽取**。一致性靠"共享段逐字一致"约束 + 评审 diff 保证。
  > 记录理由,避免将来误判为遗漏:抽到 `docs/standards/` 虽能消重且不违反 §2(docs 绝对路径允许),但会让 skill 依赖 docs 树、削弱"脱离仓库独立分发"能力;当前取自包含优先。

---

## 5. 目标 IA(目录形状不变,仅内容级增删)

```
docs/
├── README.md                          # 补:阅读路径表加 classification/naming/skill-authoring 三行
├── quick-start/
│   ├── quick-start.md                 # step-4 人工确认清单改为链接 agent-workflow.md §8,不重述
│   └── ai-native-model.md             # "必须人工确认的事项"改为指针
├── guides/
│   ├── README.md                      # 新增薄索引(~10 行):create-app/ vs frontend/ 分流说明
│   ├── create-app/frontend-create.md  # 不动
│   └── frontend/{global-error-handling,mock-development}.md  # 不动
├── standards/
│   ├── README.md                      # 不动
│   ├── agent-workflow.md              # 不动(reconcile 公理不抽取,见 §4)
│   ├── tech-stack / project-structure / document-naming / document-classification / skill-authoring / ui-design-strategy / test.md  # 不动
│   ├── api-standard.md                # §8 与 modules/_template/api.md 去重(见下),统一分页约定
│   └── code-standard/
│       ├── common-develop.md          # 不动
│       ├── backend-develop.md         # 减肥:删 §7(162行)、删 §6.1 改指针、删手写目录 → ~560 行
│       └── frontend-develop.md        # 减肥:仅删手写目录,其余不动(健康基准)
├── modules/ … requirements/ … deploy/ … reports/ … reference/   # 目录不变
└── deploy/release-policy.md           # 新增:从 deploy/README.md §5-7 拆出,修悬空引用
```

**删除**:`docs/reviews/`(空孤儿)。

## 6. 逐文件动作

| 文件 | 动作 | 信息是否丢失 |
| --- | --- | --- |
| `docs/reviews/` | 删(空) | 否 |
| `backend-develop.md` §7 | 删(纯重述 §3/§4/§5) | 否(内容已在 §3-§5 可引用形式存在) |
| `backend-develop.md` §6.1 | 删,改一行指针 → `api-standard.md §4` | 否(且修正 409 不一致) |
| `backend-develop.md` / `frontend-develop.md` 手写目录 | 删 | 否(标题层级仍在) |
| `backend-develop.md` 附录检查清单 | 保留,删掉与已删 §7 重复的 3 条 | 否 |
| `deploy/release-policy.md` | 新增(从 deploy/README §5-7 拆) | 否(内容搬家) |
| `deploy/README.md` §5-7 | 缩为指向 release-policy.md 的指针,保留 §1-4/§8-9 | 否 |
| `api-standard.md` §8 | 与 `modules/_template/api.md` 择一为准:模块模板更完整,§8 缩为"单端点文档块规范"并统一分页约定为 offset/limit(实测更正:api-standard §6.1 明确"与 PagedRequestDto 一致,不使用 page/pageSize";是 modules/_template/api.md 用错了 page/pageSize,已改为 offset/limit + PagedResultDto 响应形状) | 需人工确认哪个为准 |
| `quick-start/*` 人工确认清单 | 改指针 | 否 |
| `docs/README.md` 阅读路径表 | 补 3 行 | 否(新增) |
| `guides/README.md` | 新增薄索引 | 否(新增) |

## 7. skill 引用变更(路径级)

- `agent-workflow-template.md` L75/L260 的 `releasePolicy: "docs/deploy/release-policy.md"` —— **路径不变**,只需该文件真实存在(问题 1 修复)。
- `backend-develop.md` 被 coding/code-review/task-manager 与 `agent-workflow.md §9` 引用 —— **文件名不变,原地减肥,零引用改动**。
- `api-standard.md §8` 若改造 —— 先 grep 各 skill 的 examples/按需资源是否按锚点 `#8-api-文档模板` 引用(本次未发现,实施前再核)。
- reconcile-strategy 引用 —— **不变**(§4 决定不抽取)。

## 8. 风险与应对

| 风险 | 应对 |
| --- | --- |
| 删 §7 误删唯一规则 | 已逐条核对 §7 内容全部在 §3-§5 存在;删前再 diff |
| api-standard §8 去重改动分页约定,影响既有生成项目 | 统一为 page/pageSize(与框架 PagedResult 一致);实施前 grep 引用与生成项目样例 |
| 拆 release-policy 改动 deploy/README 结构,影响 deploy skill | deploy skill 引用的是 `docs/deploy/`(目录)与 `server-config.md`,不锚定 README 章节号;核验后再改 |
| 手写目录删除后人类读者不便 | GitHub 自动渲染 TOC;标题层级保留 |

## 9. 验收闭环

### Must have
| # | Must have | 映射 |
| --- | --- | --- |
| M1 | 目录层级零变化,`reviews/` 删除 | §5 |
| M2 | backend-develop.md 减至 ~560 行,§7/§6.1/手写目录去除,零规则丢失 | §6 |
| M3 | 悬空 `release-policy.md` 修复为真实文件 | 问题1/§6 |
| M4 | 异常映射单一来源(api-standard §4),409 不一致消除 | 问题4 |
| M5 | reconcile 公理**不动**(尊重自包含决定),并在文档记录"刻意接受" | §4 |
| M6 | README/guides 索引补齐,meta 文档一跳可达 | 问题7/8 |
| M7 | 所有 skill→docs 引用实施后仍解析(grep 核验 + 生成探针) | §7 |

### 完成定义
- 经用户确认;实施后生成探针 app 验证 docs 结构与 skill 引用完好。
- 纯 IA 重规,不改框架源码。

---

## 附录:实测事实

- backend-develop.md 9 个 H2 / 27 个 H3 / 50 代码块;§7 跨 L544-705(162 行);§6.1 L500-509(4 行,缺 ConflictException)vs api-standard §4 L109-133(5 行)。
- frontend-develop.md 240 行,无 mis-mix —— 健康基准。
- docs 最深 2 层。`docs/reviews/` 空,零引用。`docs/deploy/release-policy.md` 不存在但被 skill 模板引用 2 处。
- api-standard §8 用 offset/limit;modules/_template/api.md 用 page/pageSize —— 分页约定不一致。
- 6 份 reconcile-strategy 公理段逐字重复 ~44 行 × 6(刻意接受,见 §4)。
