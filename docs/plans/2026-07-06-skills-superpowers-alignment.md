# Skills 对齐 Superpowers 最佳实践 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 吸收 superpowers 的三项长处到模板的 skill 体系——description 只写"何时用"、加 session-hook 强制发现、给 skill 引入压力测试验收——同时与 superpowers 共存不冲突。

**Architecture:** 三块独立改动。(A) 6 个 SKILL.md 的 description 瘦身+边界澄清（workflow 移出 description，只留 when+触发词+与 superpowers 划界；body 不动）。(B) 仓库根 `.claude/` 加 SessionStart hook + 入口 skill `using-leistd-workflow`，服务**模板仓自身**开发（不进 `template/`，不受 template.json 的 `.claude/**` 排除影响，与 superpowers 的 hook 并存）。(C) 新增一份 skill 压力测试验收指南，纳入 skill 维护流程。

**Tech Stack:** Markdown（SKILL.md frontmatter YAML + body）、Claude Code SessionStart hook（settings JSON + 提示文件）、Windows（Bash/Git Bash）。无编译；验证=grep/结构核验 + 一次真实 subagent 基线跑（Task C）。

## Global Constraints

- 改动范围：`template/.claude/skills/*/SKILL.md`（block A）、仓库根 `.claude/`（block B）、`template/docs/`（block C）。不改业务代码、不改 8 阶段划分、不新增开发阶段 skill。
- **不改 `template/.template.config/template.json` 的生成规则**（`.claude/**` 排除保持，skills 仍是模板仓自身工具，不进子项目）。
- block B 放**仓库根 `.claude/`**；**不放** `template/.claude/`（放那里会被排除、无处生效）。
- 与 superpowers 共存：新增 hook 是**追加**一个 SessionStart hook，不覆盖 superpowers 的；入口 skill 名 `using-leistd-workflow` 与 superpowers 14 个名零碰撞。
- description 遵循 superpowers/Anthropic authoring 规范：**第三人称、只描述"何时用"、不概述 workflow**；保留 使用时机 + 用户自然语言触发短语 + 与相邻 skill（含 superpowers）边界。
- 保留每个 SKILL.md 的 `metadata`（openclaw/requires/skillKey、task-manager 的 `version`）、`user-invocable`、`disable-model-invocation` 不变；body 不动。
- 零跨 skill 文件引用（沿用 §2.5 硬约束）。不写真实域名/密钥/客户名。
- 每任务独立提交，前缀 `chore(skills)`/`chore(repo)`/`docs(skills)`，结尾附 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`。分支 `develop`。
- 六个 skill：coding、code-review、deploy、test-runner、task-manager、requirement-plan。

---

## 设计说明：description 瘦身的确切含义

现状 description 以 "该 Skill 用于[做什么 X]……" 开头——superpowers 明确禁止在 description 概述 what/workflow。瘦身 = 把开头"做什么"改写为纯 "何时用" 触发语；workflow 细节本就在 body（必读顺序/工作流程等），只需从 description 删除，不搬运。保留：使用时机、用户自然语言触发短语、边界澄清（补一句与 superpowers 对应 skill 的分工）。每个新 description 均**不再出现"该 Skill 用于"**，改为"在<场景>时使用"。

---

## Task A1: coding + code-review description 瘦身

**Files:**
- Modify: `template/.claude/skills/coding/SKILL.md`（仅 frontmatter description）
- Modify: `template/.claude/skills/code-review/SKILL.md`（仅 frontmatter description）

**Interfaces:**
- Produces: 瘦身 description 风格（A2/A3 沿用）。body 不动。

- [ ] **Step 1: 定义验证（先失败）**

Run: `grep -q "该 Skill 用于" template/.claude/skills/coding/SKILL.md && echo OLD_PRESENT || echo ALREADY`
Expected: `OLD_PRESENT`

- [ ] **Step 2: 替换 coding 的 `description: |` 块为：**

```yaml
description: |
  在需要按已确认的 Plan.md 落地代码改动时使用；正式审查交给 code-review，完整测试交给 test-runner，通用 TDD 循环可配合 superpowers:test-driven-development。

  使用时机：
  (1) Plan/context 指向 Phase 3 开发自测
  (2) 需要新增、修改或重构代码以满足已确认需求
  (3) code-review 或 test-runner 发现问题需回到开发修复
  (4) 用户自然语言：「实现这个功能」「按 Plan 开发」「把这块代码改一下」

  不适用：还没有已确认 Plan（先用 requirement-plan）；只想审查或跑测试（用 code-review / test-runner）。
```
（保留其后 `metadata:` 起所有内容不变。）

- [ ] **Step 3: 替换 code-review 的 `description: |` 块为：**

```yaml
description: |
  在需要对代码变更做需求对齐、规范与 P0/P1/P2 分级审查时使用；不跑完整测试（test-runner），不做需求分析（requirement-plan）；与 superpowers:requesting-code-review 的区别是本 Skill 按项目 docs 规范就地审查并出 review report。

  使用时机：
  (1) Plan/context 指向 Phase 4 代码审查
  (2) 开发完成后需要正式审查
  (3) 用户自然语言：「帮我 review 一下」「提交前检查」「看看这个 diff / PR」「检查代码质量」

  不适用：需要运行测试验证（用 test-runner）；需要澄清需求范围（用 requirement-plan）。
```

- [ ] **Step 4: 验证**

Run:
```bash
for s in coding code-review; do
  grep -q "该 Skill 用于" template/.claude/skills/$s/SKILL.md && echo "$s STILL_WHAT" || echo "$s when_only_OK"
  grep -q "用户自然语言" template/.claude/skills/$s/SKILL.md && echo "$s trigger_OK" || echo "$s trigger_FAIL"
  grep -q "不适用：" template/.claude/skills/$s/SKILL.md && echo "$s notwhen_OK" || echo "$s notwhen_FAIL"
  grep -q "## 工作流程\|## 必读顺序\|## 审查范围\|## 最小验证原则" template/.claude/skills/$s/SKILL.md && echo "$s body_intact_OK" || echo "$s body_CHECK"
done
grep -q "superpowers:test-driven-development" template/.claude/skills/coding/SKILL.md && echo coding_sp_OK
grep -q "superpowers:requesting-code-review" template/.claude/skills/code-review/SKILL.md && echo review_sp_OK
```
Expected: 两 skill 均 `when_only_OK`/`trigger_OK`/`notwhen_OK`/`body_intact_OK`；`coding_sp_OK`；`review_sp_OK`。

- [ ] **Step 5: Commit**

```bash
git add template/.claude/skills/coding/SKILL.md template/.claude/skills/code-review/SKILL.md
git commit -m "chore(skills): slim coding/code-review descriptions to when-only with superpowers boundaries

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task A2: deploy + test-runner description 瘦身

**Files:**
- Modify: `template/.claude/skills/deploy/SKILL.md`、`template/.claude/skills/test-runner/SKILL.md`（仅 description）

- [ ] **Step 1: 定义验证**

Run: `grep -q "该 Skill 用于" template/.claude/skills/deploy/SKILL.md && echo OLD_PRESENT || echo ALREADY`
Expected: `OLD_PRESENT`

- [ ] **Step 2: 替换 deploy 的 `description: |` 块为：**

```yaml
description: |
  在需要准备/执行部署、健康检查、日志/状态检查或回滚时使用；验收收口交给 task-manager，生产变更须先获用户显式确认。

  使用时机：
  (1) Plan/context 指向 Phase 6 部署发布
  (2) 测试和代码审查通过后需要发布、重启或健康检查
  (3) 用户自然语言：「部署上线」「发布一下」「回滚」「看服务日志 / 状态」「重启服务」

  不适用：仅需汇总验收（用 task-manager）；测试/审查尚未通过（先用 test-runner / code-review）。
```

- [ ] **Step 3: 替换 test-runner 的 `description: |` 块为：**

```yaml
description: |
  在需要运行项目声明的单元、集成、业务场景或 E2E 测试并产出测试报告时使用；静态规范审查交给 code-review，改被测代码交给 coding；通用 TDD 循环可配合 superpowers:test-driven-development。

  使用时机：
  (1) Plan/context 指向 Phase 5 测试验证
  (2) 代码审查通过后需要验证 Must have 覆盖
  (3) 用户自然语言：「跑一下测试」「看覆盖率」「跑集成 / 场景测试」「验证功能是否正常」

  不适用：需要静态规范/缺陷审查（用 code-review）；需要修改实现（用 coding）。
```

- [ ] **Step 4: 验证**

Run:
```bash
for s in deploy test-runner; do
  grep -q "该 Skill 用于" template/.claude/skills/$s/SKILL.md && echo "$s STILL_WHAT" || echo "$s when_only_OK"
  grep -q "用户自然语言" template/.claude/skills/$s/SKILL.md && echo "$s trigger_OK" || echo "$s trigger_FAIL"
  grep -q "不适用：" template/.claude/skills/$s/SKILL.md && echo "$s notwhen_OK" || echo "$s notwhen_FAIL"
  grep -q "## 工作流程\|## 动作类型\|## 测试范围\|## 决策规则" template/.claude/skills/$s/SKILL.md && echo "$s body_intact_OK" || echo "$s body_CHECK"
done
```
Expected: 两 skill 均 `when_only_OK`/`trigger_OK`/`notwhen_OK`/`body_intact_OK`。

- [ ] **Step 5: Commit**

```bash
git add template/.claude/skills/deploy/SKILL.md template/.claude/skills/test-runner/SKILL.md
git commit -m "chore(skills): slim deploy/test-runner descriptions to when-only with boundaries

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task A3: task-manager + requirement-plan description 瘦身（含与 superpowers brainstorming/writing-plans 划界）

**Files:**
- Modify: `template/.claude/skills/task-manager/SKILL.md`、`template/.claude/skills/requirement-plan/SKILL.md`（仅 description）

**说明:** requirement-plan 与 superpowers 的 brainstorming/writing-plans 语义最易混淆——这里显式划界：requirement-plan 产出**项目 docs 规范的五段闭环 Plan 并登记到 registry**，与 superpowers 的通用 brainstorm/plan 不同。

- [ ] **Step 1: 定义验证**

Run: `grep -q "该 Skill 用于" template/.claude/skills/requirement-plan/SKILL.md && echo OLD_PRESENT || echo ALREADY`
Expected: `OLD_PRESENT`

- [ ] **Step 2: 替换 task-manager 的 `description: |` 块为：**

```yaml
description: |
  在 Plan 确认后需要任务登记、上下文恢复、阶段推进、进度跟踪或验收收口时使用；不直接替代开发/审查/测试/部署，只维护可追溯状态与阶段推进。

  使用时机：
  (1) Plan.md 已确认，需要初始化 registry/context 或开始实施
  (2) 需要查询、恢复、更新任务状态或保存阶段交接包
  (3) 需要拆解子任务、维护 Must have 映射或处理阻塞
  (4) 测试/部署完成后需要生成验收报告并收口
  (5) 用户自然语言：「这个需求进行到哪了」「继续上次的任务」「登记 / 验收这个需求」「拆一下子任务」

  不适用：还没有已确认 Plan（先用 requirement-plan）；具体阶段执行（用 coding/code-review/test-runner/deploy）。
```

- [ ] **Step 3: 替换 requirement-plan 的 `description: |` 块为：**

```yaml
description: |
  在需要把想法/背景/自然语言需求整理为项目规范的五段闭环 Plan.md（并可登记到 registry）时使用；只产出 Plan 与验收标准，不写实现代码（coding），不登记推进（task-manager）。与 superpowers:brainstorming / writing-plans 的区别：本 Skill 输出的是本项目 docs/requirements 下的规范化 Plan，而非通用设计/实现计划。

  使用时机：
  (1) 用户提出新想法、需求、目标或业务背景
  (2) 需要澄清范围、目标、风险和验收标准
  (3) 需要生成或修订 docs/requirements/{req-id}-plan.md
  (4) 用户自然语言：「我想做一个…」「新需求」「帮我理一下方案 / Plan」「这个功能怎么拆」

  不适用：只想要通用设计头脑风暴（用 superpowers:brainstorming）；Plan 已确认要推进（用 task-manager）。
```

- [ ] **Step 4: 验证**

Run:
```bash
for s in task-manager requirement-plan; do
  grep -q "该 Skill 用于" template/.claude/skills/$s/SKILL.md && echo "$s STILL_WHAT" || echo "$s when_only_OK"
  grep -q "用户自然语言" template/.claude/skills/$s/SKILL.md && echo "$s trigger_OK" || echo "$s trigger_FAIL"
  grep -q "不适用：" template/.claude/skills/$s/SKILL.md && echo "$s notwhen_OK" || echo "$s notwhen_FAIL"
done
grep -q "superpowers:brainstorming" template/.claude/skills/requirement-plan/SKILL.md && echo rp_sp_OK
grep -q "## 核心职责\|## 工作流程\|## 阶段门禁" template/.claude/skills/task-manager/SKILL.md && echo tm_body_OK
# 全量：6 个 description 都不再有"该 Skill 用于"
grep -rl "该 Skill 用于" template/.claude/skills/ && echo WHAT_REMAINS || echo ALL_WHEN_ONLY
```
Expected: 两 skill `when_only_OK`/`trigger_OK`/`notwhen_OK`；`rp_sp_OK`；`tm_body_OK`；`ALL_WHEN_ONLY`。

- [ ] **Step 5: Commit**

```bash
git add template/.claude/skills/task-manager/SKILL.md template/.claude/skills/requirement-plan/SKILL.md
git commit -m "chore(skills): slim task-manager/requirement-plan descriptions, disambiguate vs superpowers

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task B: 仓库根 session-hook + 入口 skill `using-leistd-workflow`

**Files:**
- Create: `.claude/skills/using-leistd-workflow/SKILL.md`（仓库根，服务模板仓开发）
- Create: `.claude/hooks/session-start.md`（hook 注入的提示文本）
- Modify: `.claude/settings.local.json`（追加 SessionStart hook）

**说明:** Claude Code 支持多个 SessionStart hook，本 hook 与 superpowers 的并存（各自独立触发，不覆盖）。hook 输出一段简短提示，指引"先读入口 skill / agent-workflow.md 再动手"。入口 skill 本身是发现锚点，列出 6 个开发 skill 及其触发场景，等价于 superpowers 的 `using-superpowers`。

**Interfaces:**
- Produces: 入口 skill `using-leistd-workflow`；SessionStart hook。均在仓库根 `.claude/`，不进 `template/`。

- [ ] **Step 1: 定义验证**

Run: `test -f .claude/skills/using-leistd-workflow/SKILL.md && echo EXIST || echo MISSING`
Expected: `MISSING`

- [ ] **Step 2: 创建入口 skill `.claude/skills/using-leistd-workflow/SKILL.md`**

```markdown
---
name: using-leistd-workflow
description: |
  在本模板仓库（leistd-net）内开始任何需求/开发/审查/测试/部署工作前使用，作为项目工作流的发现入口。

  使用时机：
  (1) 会话开始、清空或压缩后的第一步
  (2) 不确定该用哪个项目 skill 时
  (3) 用户自然语言：「怎么开始」「用哪个 skill」「项目工作流是什么」
metadata:
  openclaw:
    requires: []
    skillKey: "using-leistd-workflow"
user-invocable: true
disable-model-invocation: false
---

# 项目工作流入口 (using-leistd-workflow)

> 本 skill 是 leistd-net 模板仓自身开发的工作流发现入口。动手前先读它，再按意图选择下面的阶段 skill。

## 唯一事实源

所有阶段 skill 启动后**优先读取** `template/docs/standards/agent-workflow.md`（8 阶段状态机、handoff 交接包、人工确认门禁）。本仓库的开发 skill 位于 `template/.claude/skills/`。

## 按意图选择 skill

| 你的意图 | 用哪个 skill |
| --- | --- |
| 有想法/需求，要理成方案 | `requirement-plan` |
| Plan 已确认，要登记/推进/验收 | `task-manager` |
| 按 Plan 写/改代码 | `coding` |
| 审查代码变更（需求对齐+规范+分级） | `code-review` |
| 跑测试、看覆盖率 | `test-runner` |
| 部署/健康检查/回滚 | `deploy` |

## 与 superpowers 的关系

superpowers 提供通用工程方法（brainstorming/writing-plans/TDD/code-review 等），本项目 skill 提供**特定项目治理闭环**（需求登记→开发→审查→测试→部署→验收，含报告沉淀与人工确认）。二者可共存：通用能力用 superpowers，项目治理用本套 skill。相邻能力的边界见各 skill description 的"不适用"段。

## 高风险动作必须人工确认

生产部署/回滚、数据迁移/删除、修改认证授权/密钥、引入外部费用——见 `agent-workflow.md` §8。
```

- [ ] **Step 3: 创建 hook 提示文本 `.claude/hooks/session-start.md`**

```markdown
本仓库（leistd-net 模板）有项目级开发工作流。动手实现需求前，请先读技能 `using-leistd-workflow`（位于 .claude/skills/），它是工作流发现入口，会指引你按意图选择 requirement-plan / task-manager / coding / code-review / test-runner / deploy，并要求先读 template/docs/standards/agent-workflow.md。superpowers 通用技能与本套项目技能可共存。
```

- [ ] **Step 4: 追加 SessionStart hook 到 `.claude/settings.local.json`**

把现有内容：
```json
{
  "permissions": {
    "allow": [
      "Bash(dotnet new *)"
    ]
  }
}
```
改为（追加 hooks 段，permissions 保持）：
```json
{
  "permissions": {
    "allow": [
      "Bash(dotnet new *)"
    ]
  },
  "hooks": {
    "SessionStart": [
      {
        "matcher": "startup|clear|compact",
        "hooks": [
          {
            "type": "command",
            "command": "cat .claude/hooks/session-start.md"
          }
        ]
      }
    ]
  }
}
```

- [ ] **Step 5: 验证**

Run:
```bash
test -f .claude/skills/using-leistd-workflow/SKILL.md && echo skill_OK
test -f .claude/hooks/session-start.md && echo hook_msg_OK
grep -q "SessionStart" .claude/settings.local.json && echo hook_cfg_OK
grep -q "Bash(dotnet new \*)" .claude/settings.local.json && echo perms_preserved_OK
# JSON 合法性
python -c "import json,sys; json.load(open('.claude/settings.local.json')); print('json_valid_OK')" 2>/dev/null || node -e "JSON.parse(require('fs').readFileSync('.claude/settings.local.json','utf8'));console.log('json_valid_OK')"
# 名字不与 superpowers 碰撞
grep -q "using-leistd-workflow" .claude/skills/using-leistd-workflow/SKILL.md && echo name_OK
# 未放进 template/
test -d template/.claude/skills/using-leistd-workflow && echo WRONG_PLACE || echo placement_OK
```
Expected: `skill_OK`、`hook_msg_OK`、`hook_cfg_OK`、`perms_preserved_OK`、`json_valid_OK`、`name_OK`、`placement_OK`。

- [ ] **Step 6: Commit**

```bash
git add .claude/skills/using-leistd-workflow/ .claude/hooks/session-start.md .claude/settings.local.json
git commit -m "chore(repo): add workflow discovery entry skill + session-start hook (repo tooling)

- 仓库根入口 skill + SessionStart hook，服务模板仓自身开发
- 与 superpowers hook 并存；不进 template/，不改生成规则

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task C: skill 压力测试验收指南 + 一次真实基线验证

**Files:**
- Create: `template/docs/standards/skill-authoring.md`（skill 编写/验收规范，含压力测试流程）
- Modify: `template/docs/standards/README.md`（若存在索引则加一行引用；否则跳过并在报告说明）

**说明:** 吸收 superpowers 的 writing-skills TDD：改/建 skill 时先跑"不给 skill 看 agent 会不会违规"的基线（RED），再验证 skill 存在时 agent 遵守（GREEN）。本任务产出规范文档，并**实际跑一次基线**证明流程可用（用 requirement-plan 的一条硬规则"不生成没有验收闭环的 Plan"作样例）。

- [ ] **Step 1: 定义验证**

Run: `test -f template/docs/standards/skill-authoring.md && echo EXIST || echo MISSING`
Expected: `MISSING`

- [ ] **Step 2: 创建 `template/docs/standards/skill-authoring.md`**

```markdown
# Skill 编写与验收规范

> 本项目 skill 的编写、description 规范与"压力测试"验收流程。改动或新增 skill 时遵循本文件。

## 1. description 规范（对齐 Anthropic / superpowers）

- 第三人称，**只描述"何时用"**，不概述 workflow/流程（流程写在 body）。
- 首句为"在<触发场景>时使用；<与相邻 skill 边界>"，不用"该 Skill 用于…"式的能力概述。
- 保留：使用时机（含阶段触发）、用户自然语言触发短语、"不适用"边界（指向正确 skill，含 superpowers 对应能力）。

## 2. 自包含约束

- skill 只引用自身目录相对路径或 `docs/` 项目文档；禁止引用其他 skill 目录（`../<other-skill>/`）。
- 公共元规则用"每 skill 自包含副本 + 单一事实源（agent-workflow.md）"，不跨目录引用。

## 3. 压力测试验收（Skill TDD）

改动或新增 skill 的行为规则时，按 RED→GREEN 验证规则真的改变了 agent 行为：

1. **RED（基线）**：派一个 subagent，**不提供该 skill**，给它一个应触发该规则的场景，记录它是否违规、用什么理由绕过。
2. **GREEN**：把 skill 规则提供给 subagent，同一场景，验证它现在遵守。
3. **REFACTOR**：若 GREEN 未通过或出现新的绕过理由，补强 skill 措辞，重跑。

只有 description 文案/引用等**非行为**改动可豁免压力测试，但需在提交说明标注"文案改动，行为不变"。

## 4. 验收清单

- [ ] description 符合 §1（when-only、有触发短语、有不适用边界）。
- [ ] 满足 §2 自包含。
- [ ] 行为规则改动已过 §3 压力测试（或已标注豁免理由）。
- [ ] 未泄漏真实域名/密钥/客户名。
```

- [ ] **Step 3: 若 standards 有 README 索引则加引用**

Run: `test -f template/docs/standards/README.md && echo HAS_README || echo NO_README`
若 `HAS_README`：在其文档列表处追加一行 `- skill-authoring.md：skill 编写、description 规范与压力测试验收`。若 `NO_README`：跳过，在报告注明。

- [ ] **Step 4: 实际跑一次压力测试基线（证明流程可用）**

派一个 subagent（不给它 requirement-plan skill），提示：
「用户说：帮我做个导出 CSV 的功能，直接给我一个 Plan。请直接产出 Plan。」
观察它产出的 Plan **是否包含验收闭环（Must have + 验证映射）**。记录结果（大概率 RED：缺验收闭环）。
然后把 requirement-plan 的"Plan 最低要求：必须含验收闭环、Must have 映射验证方式"规则提供给同类 subagent，同一提示，验证它现在**包含**验收闭环（GREEN）。
把 RED/GREEN 两次的关键输出摘要写进报告，作为流程可用性证据。（这是一次性验证证据，不入库为长期文档。）

- [ ] **Step 5: 验证**

Run:
```bash
test -f template/docs/standards/skill-authoring.md && echo authoring_OK
grep -q "压力测试" template/docs/standards/skill-authoring.md && echo pressure_OK
grep -q "只描述.*何时用\|只描述“何时用”\|只描述.何时用" template/docs/standards/skill-authoring.md && echo whenonly_rule_OK
grep -q "../" template/docs/standards/skill-authoring.md && echo HAS_DOTDOT || echo no_dotdot_ok
```
Expected: `authoring_OK`、`pressure_OK`、`whenonly_rule_OK`、`no_dotdot_ok`。Step 4 的 RED/GREEN 摘要在报告中呈现。

- [ ] **Step 6: Commit**

```bash
git add template/docs/standards/skill-authoring.md
# 若改了 README 也一并 add
git add template/docs/standards/README.md 2>/dev/null || true
git commit -m "docs(skills): add skill authoring + pressure-test acceptance guide

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task D: 端到端核验

**Files:** 无新增，仅核验。

- [ ] **Step 1: description 全量对齐**

Run:
```bash
grep -rl "该 Skill 用于" template/.claude/skills/ && echo WHAT_REMAINS || echo ALL_WHEN_ONLY
for s in coding code-review deploy test-runner task-manager requirement-plan; do
  grep -q "不适用：" template/.claude/skills/$s/SKILL.md && grep -q "用户自然语言" template/.claude/skills/$s/SKILL.md && echo "$s OK" || echo "$s FAIL"
done
```
Expected: `ALL_WHEN_ONLY`；6 行 `OK`。

- [ ] **Step 2: 自包含仍成立（未因本次改动引入跨引用）**

Run:
```bash
grep -rn "\.\./\(coding\|code-review\|deploy\|test-runner\|task-manager\|requirement-plan\)/" template/.claude/skills/ .claude/skills/ && echo CROSS_REF || echo NO_CROSS_REF
```
Expected: `NO_CROSS_REF`。

- [ ] **Step 3: hook/入口 skill 就位且与 superpowers 无碰撞**

Run:
```bash
test -f .claude/skills/using-leistd-workflow/SKILL.md && echo entry_OK
grep -q "SessionStart" .claude/settings.local.json && echo hook_OK
# 与 superpowers 14 名零碰撞（using-leistd-workflow 不在其中）
echo "brainstorming dispatching-parallel-agents executing-plans finishing-a-development-branch receiving-code-review requesting-code-review subagent-driven-development systematic-debugging test-driven-development using-git-worktrees using-superpowers verification-before-completion writing-plans writing-skills" | grep -qw "using-leistd-workflow" && echo COLLISION || echo NO_COLLISION
```
Expected: `entry_OK`、`hook_OK`、`NO_COLLISION`。

- [ ] **Step 4: 生成规则未变（skills 仍不进子项目）**

Run: `grep -q '"\*\*/.claude/\*\*"' template/.template.config/template.json && echo exclude_intact_OK || echo exclude_CHANGED`
Expected: `exclude_intact_OK`。

- [ ] **Step 5: Commit（若无改动可跳过）**

本任务通常无文件改动；若 Step 1–4 全绿则无需提交。如需记录核验证据，可写入本 plan 同目录的 report 并提交。

---

## 任务→目标映射

| 目标（评估第四节借鉴项） | 覆盖任务 |
| --- | --- |
| description 瘦身（when-only） | A1, A2, A3, D1 |
| 相邻 skill 边界澄清（含 vs superpowers） | A1, A2, A3 |
| session-hook + 入口 skill（强制发现） | B, D3 |
| skill 压力测试验收流程 | C |
| 与 superpowers 共存不冲突 | B（hook 并存、命名零碰撞）, D3 |
| 不破坏自包含 / 生成规则 | D2, D4 |
