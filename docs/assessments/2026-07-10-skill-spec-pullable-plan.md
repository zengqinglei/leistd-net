# 项目级 skill / 规范「可拉取化」实施方案（治断点 I）

> 目标：把 `dotnet new` 一次性快照的 6 个项目级 skill + `docs/standards/` 规范，改造为**可版本化拉取、可回流**的分发，让上游改进能同步到已生成项目。对应端到端审视的断点 I（[`2026-07-10-end-to-end-update-loop.md`](2026-07-10-end-to-end-update-loop.md)）。本文只做方案（现状 → 目标 → 迁移 → 风险），不含实现代码；采纳与否、何时做由维护者决定。
>
> **前置依赖**：本方案与「框架级 skill 的 `npx skills` 发布验证」强绑定——那条链路（[[记忆 leistd-framework-skill-publish-todo]]）尚未验证通。**应先验证 `npx skills add` 能正确发布框架级 skill，再启动本方案**，因为二者复用同一分发通道。

---

## 1. 现状（断点 I 复述）

- 6 个项目级 skill（`template/.claude/skills/`）+ 11 项规范（`template/docs/standards/`）在 `dotnet new` 时作为源文件**拷贝为快照**（`template.json` 无 `postActions`）。
- 生成后与上游**永久脱钩**：不是 NuGet 包、无 postAction、无 npx 订阅。
- 除 `task-manager`（`version: 5.1.0`，与框架版本无关）外**零版本标记**——用户无法判断是否过期。
- `release.yml` 明确排除模板/根 `*.md` 发版，流程上坐实解耦。

## 2. 目标形态

让「项目级 skill + 规范」具备与「组件代码 + 文档」同等的**拉动式**更新能力：

- 用户在已生成项目里跑一条命令，即可把上游改进的 skill/规范拉取/更新到本地，并能看到版本差异。
- 版本可追溯：每套 skill/规范带版本印记，能回答「我这套是哪个上游版本、是否过期」。
- 与 monorepo 布局兼容：拉取落点遵循 §2.0 项目根定义。

## 3. 方案（分两层，先易后难）

### 层一：版本印记 + 拉取命令（本方案的最小可用形态）

即便不立刻做成"包"，也先让快照**可感知、可拉取**：

1. **生成时打版本印记**：`dotnet new` 生成物里写入 `.claude/LEISTD_SKILLS_VERSION`（或并入某个已生成文件），值 = 生成时的框架 `VERSION`。这是"我这套 AI 协作层是哪个上游版本"的锚。
   - 实现挂点：`template.json` 的参数替换（把 `VERSION` 作为模板变量注入），或一个 `postAction` 写文件。
2. **提供拉取/对比命令**：在生成项目 `docs/standards/README.md` 或根 README 写明同步路径。最务实的两种：
   - **npx 拉取**（若与框架级 skill 同通道）：`npx skills add zengqinglei/leistd-net --skill <项目级 skill 名>` 直接更新 `.claude/skills/`；规范类似（需确认 `npx skills` 是否支持非 skill 的普通 md 分发——很可能不支持，规范可能仍需其它通道）。
   - **diff 兜底**：`dotnet new` 到临时目录 → diff `.claude/skills/` 与 `docs/standards/` → 手工合并。作为 npx 不覆盖场景的保底。

### 层二：真正的可拉取包（根治）

把 skill 与规范分别做成可版本化拉取的制品：

1. **项目级 skill → `npx skills` 可拉取**：`npx skills` 已能从 GitHub 仓库发现 skill（实测它当前正是发现了 `template/.claude/skills/` 那 6 个）。因此**天然可拉取**——关键是：
   - 明确"哪些 skill 该被 `npx skills add` 拉"（项目级 6 个 **应该**可拉，框架级 1 个也应可拉，但两者要能区分，避免用户误装全部）。
   - 版本对齐：`npx skills` 按 git ref 拉取，可用 tag（框架已有 `vX.Y.Z` tag）锁定版本。
2. **规范（standards）→ 需独立通道**：`docs/standards/*.md` 不是 skill，`npx skills` 不分发它。选项：
   - **随框架级"元 skill"携带**：做一个 `leistd-project-standards` skill，把规范作为它的 reference 文件带上，用户 `npx skills add` 时一并拉到。
   - **或 NuGet 化**：做一个 `Leistd.ProjectTemplate.Standards` 内容包，规范随包分发、随框架版本流动（与组件文档同范式）。成本更高但与现有 NuGet 闭环一致。
3. **CI 校验**：skill/规范发布后，CI 冒烟 `npx skills add --list` 确认可发现（呼应框架级 skill 发布待办）。

## 4. 关键设计决策（需在实现前定）

| 决策点 | 选项 | 倾向 |
| --- | --- | --- |
| skill 与规范同通道否 | (a) 都走 npx skills；(b) skill 走 npx、规范走 NuGet 内容包 | 待定——取决于 `npx skills` 能否分发非 skill md |
| 项目级 vs 框架级 skill 如何区分拉取 | 靠 `--skill <name>` 精确指定 / 靠目录约定 / 靠 registry 清单 | 待 `npx skills` 发布验证后据实测定 |
| 版本锁定 | git tag（已有 `vX.Y.Z`）/ 分支 / commit | git tag（与框架版本对齐） |
| 回流是"覆盖"还是"合并" | 用户可能已改本地 skill/规范 → 覆盖会丢改动 | 需提供 diff 视图、默认不静默覆盖 |

## 5. 风险

- **`npx skills` 通道能力未知**：能否分发规范类普通 md、能否精确区分项目级/框架级 skill——**本方案强依赖发布验证的结果**，未验证前无法定层二细节。
- **用户本地改动冲突**：已生成项目里用户很可能改过 skill/规范；拉取更新不能静默覆盖，需 diff/合并策略。
- **版本对齐复杂度**：skill/规范版本、框架 VERSION、npx 拉取的 git ref 三者要对齐，需清晰的版本策略避免"拉到不匹配的组合"。
- **成本**：层二（尤其规范 NuGet 化 + CI）工作量不小；层一（版本印记 + 命令说明）成本低、先交付。

## 6. 建议推进顺序

1. **先解锁前置**：验证框架级 skill 的 `npx skills` 发布（push 后实测，见发布待办）。这一步的结果直接决定层二的可行路径。
2. **做层一**（低成本即时收益）：版本印记 + 回流说明——让快照"可感知过期 + 有手工回流路径"，先把断点 I 从"完全无路"降到"有明确路径"。
3. **据发布验证结果做层二**：若 `npx skills` 通道够用，项目级 skill 直接可拉；规范按 §4 决策选通道。
4. CI 冒烟校验收口。

---

## 一句话结论

断点 I 走"重"方案（可拉取包）方向正确，但**它的层二细节被"框架级 skill 发布是否验证通"这个前置卡住**。务实路径是：**先做层一（版本印记 + 回流说明，即时低成本）把"无感知快照"变成"可感知、有手工回流路径"；待 `npx skills` 发布验证后，再据实测能力落地层二的真正可拉取化。**
