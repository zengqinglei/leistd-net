# 模板规范读取成本闸门按生成内容计量

状态：已完成 · 风险档：中

## 1. 需求

**问题**：`scripts/measure-template-read-cost.py`（check-all 最后一道）有三处口径放大了成本。每加一条规范或一行路由，都要在别处删掉等量文字，文案被反复压缩，可能失准：

1. 按模板源码计：条件块两个分支都算、全部功能开启，比任何真实生成项目里 AI 实际读到的都多；
2. 上限是棘轮（等于当前读数，只降不升）；
3. 路由索引 `template/docs/README.md` 计入每个任务。

**依据**（一手来源）：

- Anthropic [Skill 最佳实践](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)：参考文件被读到之前不占上下文；SKILL.md 控制在 500 行内；引用只深一层；内容要 minimal 且 fully outlines；以评测驱动改写。
- Anthropic [Effective context engineering](https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents)：just-in-time 加载，常驻上下文只放轻量标识。
- Claude Code [记忆文档](https://code.claude.com/docs/en/memory)：文件过长会降低遵循度，内容按需加载。
- GitHub Copilot [自定义指令](https://docs.github.com/en/copilot/tutorials/customize-code-review)：短文件更可能被完整处理。
- [mdtoken](https://pypi.org/project/mdtoken/)：按路径与总量设上限，可以只警告。

结论：要约束的是真实生成项目里每次任务实际进入上下文的量；常驻索引另行从严约束；上限需要留出余量，不必做成棘轮。准确性靠真实请求回归，字数不能作为唯一判据。

**决策**：用户于 2026-11-03 确认以下方案全部实施。

**验收**：

| 编号 | Given / When / Then | 覆盖 |
| --- | --- | --- |
| V1 | Given 一个 `#if/#else` 块，When 计量，Then 只计该组合生成的那一支，条件行不计 | `--self-test`（分支相加的变异被拒绝） |
| V2 | Given 两个文件的大块分别在 X 开和 X 关时生成，When 计量，Then 读数取同一组合总量的最大值，不是各文件最大值相加 | `--self-test`（按文件取最大的变异被拒绝） |
| V3 | Given 读数超过上限，When `--check`，Then 以非 0 退出并报超限任务；合法输入退出 0；等于上限时通过 | `--self-test`（入口与 `--check` 共用 `run_gate`；去掉最终拒绝分支的变异被拒绝） |
| V4 | Given 任务读取集合里又列出常驻文件，When 检查，Then 报错；常驻部分单独一行读数与上限 | `--self-test` |
| V5 | Given 读取表里出现 `<br>` 合并的路由、表内插入段落，或同一形态下任务名重复，When 检查，Then 报错；互斥分支下同名的行通过 | `--self-test` |
| V6 | Given 现行模板，When `--check`，Then 六行读数全部不超限 | check-all |
| V7 | Given 读取表按形态切换必读文件（新增界面文案：本地化开读 `frontend-i18n.md`，关读 `frontend-ui.md`），When 计量，Then 同一形态内选读取集合求总量，再对全部组合取最大，互斥文件不相加；非本地化替代文档增长后该任务超限 | `--self-test`（把条件项无条件计入的变异被拒绝） |

**方案**：脚本加载 `check-template-scenario-coverage.py` 的条件求值器（来源条件、文件级排除、嵌套 `#if/#elif/#else`、computed 符号），不另写一套。它对 `template.json` 的全部参数组合（含 Ci，共 4,608 种）逐文件裁剪，得到生成后的字符数，每个任务取最重组合的读数。离线运行，约 1 秒。读取表里随形态切换的必读文件登记为（文件，条件），只在条件成立的组合里计入；按读取表核对，现有任务中只有新增界面文案随本地化切换。常驻部分是项目 Skill 的 `SKILL.md` 与 `template/docs/README.md`，单独一行计量，任务读数不再包含它们。读取表要求一行一条路由，且同一形态下任务名唯一。上限等于登记目标值加 10% 余量，向上取整到百位：这样一条规则或一行路由（几百字符）不必在别处删去等量文字，体量要涨到翻倍之前，必须先有人在规格里写明理由。超限时先压缩去重；确需更多内容时，在对应规格里写理由后上调 `TARGETS`；读数下降时不必跟着下调。压缩或改写规范时，按 [Skill 验收](../architecture/collaboration-scenarios.md#6-skill-验收) 用真实请求回归，并把关键规则放在文件前部。规则写入 `docs/template/development-guide.md` §4 与脚本头注释。

## 4. 结果

读数对照（改造前为源码口径、含常驻部分、棘轮上限；改造后为生成内容最大值、不含常驻部分，上限为目标值加 10%）：

| 任务 | 改造前读数 / 上限 | 改造后读数 / 上限 |
| --- | --- | --- |
| 常驻（SKILL.md + docs/README.md） | 各任务各计 4,537 | 4,230 / 4,700 |
| backend-crud | 37,106 / 37,106 | 31,519 / 34,700 |
| fullstack-crud | 70,255 / 70,640 | 64,182 / 70,700 |
| ui-change | 52,919 / 52,932 | 47,059 / 51,800 |
| new-text | 19,099 / 19,233 | 14,673 / 16,200 |
| review-only | 41,053 / 41,198 | 35,419 / 39,000 |

- 改动：`scripts/measure-template-read-cost.py`（计量模型、`--self-test`、读取表结构检查）；`scripts/check-all.ps1` 注释；`docs/template/development-guide.md` §4；`docs/framework/quality-assurance.md` 的“被改脚本的自检”表增加一行。
- 验收：`--self-test` 通过；把裁剪条件改为恒真、把取最大值改为按文件各取最大值、把条件项无条件计入、去掉 `--check` 的最终拒绝分支，这四个变异都被自检拒绝。在 `frontend-ui.md` 的非本地化分支加入 2,200 字符后，`--check` 以 new-text 16,874 > 16,200 退出 1，撤销后恢复通过。`--check` 六行全部通过；check-all 的 37 道 Python 闸门全部通过，`docs/` 章节锚点通过。pwsh 闸门（Skill 校验、退役符号等）由 CI 静态作业验收。
- 未做：模板引擎的源名替换（如 `MyProject` 换成项目名）不计入，差异只有几个字符。Skill 验收的真实请求回归在下次压缩或改写规范时执行，本次没有改动规范正文。
