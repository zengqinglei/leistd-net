# 质量保障效率优化实施与验收

用户已裁决完成剩余 B/C，两组连续实施后一次汇报。A 已提交为 `00e3ed6f` 并推送；本轮此前仅获授权 pull，后续用户裁决方案 3：撤回联系方式两行登记，排除其脚本，以显式路径提交本组；不带入其他会话改动。不删除缺少接替者及变异证据的检查/测试，保护邮件测试和其他会话改动。长期规则见[质量检查与验证分工](../framework/quality-assurance.md)与[模板质量验证](../template/quality-assurance.md)。

## 选定范围与执行顺序

| 组 | 范围 | 验收 |
| --- | --- | --- |
| A，已提交 | 默认及本地化前端官方 content cache、依赖安装清缓存 | 原有 A 证据保留；跟进 CI `36752179167` 的首次前端运行 |
| B | API 自检与正文共用本次索引，删除独立自检进程/参数，保留 8 例及完整正文扫描 | 旧/新逐项变异和恢复，隔离相同输入前后各三轮；不删单测 |
| C | 同一候选只打包一次，三消费者下载不可变包；消费独立必过，九场景两片，原矩阵检查名汇总 | 保留 OIDC 作业；逐包/逐场景失败、缺片/取消变异；相同输入本地前后各三轮；提交后同规格完整 GitHub CI 实测 |

用户另请登记联系方式日志闸门及自检：这两行归本任务的 `check-all.ps1` 变更，脚本/邮件修复归 DEF-04 会话。它补当前调用点零覆盖的独立责任，不属于 B 删除范围。必须分别记录它的新增成本与 B 合并收益。

所有测量和变异在 `.tmp/qa-efficiency-bc/` 的已提交 `52d541ac` 隔离副本进行，避免读取服务客户端等其他会话的未完成实现。触碰共享文件先读 diff，升级说明追加前重读；不覆盖其他会话。新调度直接替代旧串行作业，不保留并行旧链或迁移开关。

本轮不引入分析器迁移、MTP/v3、浏览器隔离关闭、三片、动态调度、路径裁剪或组合跳过机制。新增场景定义脚本由生成/汇总两个真实入口共享，不引入新的索引格式；结果文件仅承载实际执行摘要与完整性核对。

GitHub 无法运行未提交的 workflow。本轮完成本地实现、同机对比与变异，远端改后数字必须在用户协调提交/推送后补验；不能把本机 M2 结果或模型标成 ubuntu runner 的实际收益。此项尚未完成前保留本计划。

## 基线与测量方法

2026-09-30 阶段一：本机 M2/8 核/16 GiB、Node 24.18/npm 11.16/.NET 10.0.301；静态 49.811/49.810/48.031 秒，框架全量 33.012 秒（1,678 通过、19 Redis Skip），九场景矩阵 859.897 秒。当前已提交 HEAD `e9178ad5` 的 CI 四作业实际秒：静态 35、框架 116、PostgreSQL 126、矩阵 1,266，队列分别 4/4/4/3，run 墙钟 1,271 秒。历史五次矩阵中位 1,319 秒。

C 模型原先九场景区间 1,052.688 秒、外围 213.312 秒；包消费 118 秒。两片场景和 528.254 / 524.434 秒（identity/resource/standalone/resource-notifications/resource-localization；其余四场景）；C1+两片模型 623.566 秒。但新 OIDC 作业尚未纳入此基线，50.75% 仅为模型，下一组必须更新关键路径，不能承诺为当前 CI 已兑现收益。

原评估完整测量与官方对照的临时证据保留 `.tmp/qa-efficiency-20260930/`，原文备份 `.tmp/qa-efficiency-a/phase1-evidence.md`；原评估未提交，不能假称 Git 已归档。有效结论上收后移除原 assessment，不将比较/状态移入分发载荷。

效率对比同输入、同机器/runner、相同命令、明确缓存条件，各至少三轮；分别报告首次/冷/热缓存，不把方法求和视为墙钟。其他会话可能有负载，本地结果不当 CI SLA；C 在有可比远端执行后再判断完整关键路径目标是否达到至少 30% 缩短。

## A 组接替与变异验收

删除的是未变输入的重复分析，接替者仍为相同 ESLint/Stylelint/Prettier 规则；所有匹配文件仍被调用。安装生命周期负责依赖变化失效，没有自写缓存键实现。无单测删除、无生产业务逻辑改变。

- 建立合法缓存后分别修改已有 TS（any）、HTML（缺 alt）、CSS（非法颜色）、格式；原无缓存入口和新缓存入口都必须有对应非零诊断，撤回变异后绿。
- 新增违规 TS 文件必须由完整 globs 发现；规则/格式配置收紧后，同一未改源文件须重新检查并红。
- 用实际本地 Prettier 插件做 v1→v2 行为变化，经实际 npm install 及 lock 更新，证明 postinstall 清掉旧缓存，未变源文件在新入口红。
- 在模板源缓存目录放非源码哨兵，真实 dotnet new 不得输出缓存；生成项目格式检查不得检查缓存文件。
- 不改规则、max-warnings、单测隔离与用例；默认和本地化生成项目 lint/build/浏览器测试全绿，最后 check-all 全绿。

## 执行结果

A 组已提交；以下是其历史实测。B/C 实施与本地验收继续进行，远端改后验收待用户提交后执行。全部验收完成后上收有效事实并删除本计划。

### 同入口耗时（秒）

| 场景 / 模式 | 三次墙钟 | 中位 |
| --- | --- | --- |
| identity / 原无缓存 | 10.233, 7.275, 7.030 | 7.275 |
| identity / 新冷缓存 | 7.071, 7.314, 7.400 | 7.314 |
| identity / 新热缓存 | 2.648, 2.487, 2.347 | 2.487 |
| resource-localization / 原无缓存 | 9.103, 6.952, 6.765 | 6.952 |
| resource-localization / 新冷缓存 | 9.118, 11.185, 7.815 | 9.118 |
| resource-localization / 新热缓存 | 2.432, 2.614, 4.280 | 2.614 |

另做前后顺序交替的三轮复测：

| 场景 / 模式 | 三次墙钟 | 中位 |
| --- | --- | --- |
| identity / 原无缓存 | 17.152, 11.845, 7.796 | 11.845 |
| identity / 新冷缓存 | 10.211, 11.263, 10.214 | 10.214 |
| identity / 新热缓存 | 2.969, 2.747, 2.624 | 2.747 |
| resource-localization / 原无缓存 | 5.832, 4.287, 4.402 | 4.402 |
| resource-localization / 新冷缓存 | 4.322, 5.023, 4.584 | 4.584 |
| resource-localization / 新热缓存 | 2.069, 2.099, 2.065 | 2.069 |

首轮 Identity 热缓存中位 7.275→2.487 秒（少 4.789 秒，65.82%），ResourceLocalization 6.952→2.614 秒（少 4.338 秒，62.39%）；交替轮分别 11.845→2.747、4.402→2.069 秒。全部重复值保留，不用较高负载下的 76.81% 作为承诺。冷缓存首轮与交替轮各有波动：ResourceLocalization 交替轮约多 0.181 秒（4.12%），不声称首次或 CI 新生成目录提速。热重跑省重复工作才是 A 的已验证收益；未改 CI，不将节省乘九。共享机器上其他会话同时运行，数字为本机观察结果。

### 变异与恢复结果

| 缺陷 / 变化 | 原无缓存入口 | 接替的实际缓存入口 | 恢复 / 正例 |
| --- | --- | --- | --- |
| 已有 TS 加 any | exit 1 | exit 1，no-explicit-any | exit 0 |
| 已有 img 删除 alt | exit 1 | exit 1，alt-text | exit 0 |
| 已有 CSS 非法颜色 | exit 2 | exit 2，declaration-property-value-no-unknown | exit 0 |
| 已有 TS 格式破坏 | exit 1 | exit 1，qa-cache-probe.ts | exit 0 |
| 新增违规 TS | 正例已建缓存 | exit 1，no-explicit-any | 配置撤回与最终 lint exit 0 |
| ESLint 规则收紧，源文件不变 | 正例已建缓存 | exit 1，no-restricted-syntax | 配置撤回与最终 lint exit 0 |
| Stylelint 规则收紧，源文件不变 | 正例已建缓存 | exit 2，declaration-property-value-disallowed-list | 配置撤回与最终 lint exit 0 |
| Prettier singleQuote 改值，源文件不变 | 正例已建缓存 | exit 1，qa-cache-probe.ts | 配置撤回与最终 lint exit 0 |
| Prettier 插件 v1→v2，源文件不变 | 正例已建缓存 | exit 1，qa-cache-plugin.qa | 配置撤回与最终 lint exit 0 |
| 模板源缓存哨兵 | 非源码文件 | dotnet new exit 0，未输出缓存目录 | 缓存哨兵删除 |

共 9 类缺陷/失效变异（13 条匹配诊断的非零入口结果），另有缓存载荷排除验证。插件变更用了实际 npm install 与 lock 更新；两次安装后都确认全部 lint 缓存目录被删除。辅助测量命令曾有参数/PATH 与诊断名称错误，已修正重跑，不以单纯非零退出作为通过，只有诊断匹配的 validated 记录用于验收。没有将测量脚本变成新增常驻闸门。

### 实际生成验证与边界

| 入口 | Identity | ResourceLocalization |
| --- | --- | --- |
| npm run build | exit 0 / 21.189s | exit 0 / 34.120s |
| npm test（真实 Chromium） | exit 0 / 25.238s | exit 0 / 35.062s |

浏览器结果：Identity 59 文件 / 381 用例、ResourceLocalization 47 文件 / 300 用例，681 全通过，无用例删除。两场景的 lint:ts、lint:style、format 分别在没有缓存父目录时独立运行均 exit 0，不依赖执行顺序。最终静态入口 27 项全绿、56.941 秒；git diff --check 与新增文档相对链接校验通过。

当前组只改检查配置、安装生命周期与缓存载荷排除，未改变参数语义、业务逻辑或 API；未重复执行框架/后端/全九场景/Redis/PostgreSQL/OIDC/远端 CI。这些不是本组已执行结果，B/C 按其风险另验，最终 C 须完整九场景与真实服务闭环。

证据：`.tmp/qa-efficiency-a/verification.json`、`paired.json`、`verify.py`、`paired.py` 与各入口日志。共享 CI/check-all 相对本组开始时的内容哈希保持一致；其他会话在邮件、后端和依赖文件的新增改动保留。升级说明只在重读后追加 lint 替换关系，未触碰已有 OIDC 行或 DEF-04 内容。原 assessment 已删除，完整原证据保留临时目录，不写入对外分发目录。

## B/C 验收进行中的事实

- B 唯一删除项是 API 独立自检进程/参数；原 8 例仍运行。7 类缺陷在旧/新链均有对应非零诊断（14 条红结果），合法新源码与恢复完整入口均绿。辅助合并曾错误保留提前退出，被正文变异当场抓住，已纠正；纠正前数字不用作验收。
- 同机无 bin/obj 的相同源码副本三轮 B 测量：API 旧 5.301/14.474/8.336 秒，新 7.408/3.382/3.000 秒；全入口旧 53.938/80.070/49.475 秒，新 57.648/44.198/30.757 秒。共享负载波动大，不把入口中位差全部归因于合并；末次应在本任务矩阵结束后复测。新增联系方式自检/扫描合计三次 0.443/0.239/0.229 秒，独立记录，不记为 B 收益。
- C 汇总 62 条夹具/作业状态结果通过；4 类真实 nupkg 缺陷在原消费、新独立消费和实际汇总 guard 均红，缺失源码包的全量消费也红。PR 真实多提交差异变异证明旧 HEAD^ needed=false，而完整 PR needed=true；合法无关范围 false，基础范围不可读 true。
- 框架冻结输入 52d541ac 全量：28 工程 / 1,723 用例全通过，真实 Redis，无跳过，88.692 秒；工作树 check-all 28 项全绿。其他会话的运行时改动未混入冻结输入。
- CI 36752179167 全绿，A 第一次远端缓存入口通过；静态 49 秒、框架 133 秒、PostgreSQL 125 秒、OIDC 284 秒、矩阵 1,936 秒；job 队列 3/4/3/3/4 秒，workflow 到 job 建立 39 秒，质量终点墙钟 1,979 秒。Ubuntu24.04.5 / image20260927.320.1，公共 runner 4 vCPU/16GB。当前模型为共享打包 42 + 分片准备 307.222 + 最大片场景 721.276 = 1,070.498 秒，44.71% 仅模型；新 artifact、作业准备、汇总/队列及修复范围后的容器增量未测。
- 串行本地首轮（同机、冻结源码、完整九场景、不含容器）：pack 14.476 + 全68包消费153.816 + 矩阵944.624 = 1,112.920 秒。试过每消费者重复 pack 的调度，完整九场景两片实际全绿、847.668 秒，但因重复构建不作为终局；原始试验留 .tmp，终局为单次共享不可变包源。
- 终局调度首次运行：包源18.885秒、完整68包消费148.315秒，通过；两个矩阵在 audit 阶段22.274/23.366秒失败。npm 此时报告 GHSA-ff3f-86qr-9cv3，@angular/router <22.2.0 high；官方只影响 Node SSR，本模板纯 SPA 不受该利用路径影响，现有版本门禁仍阻断。未降低阈值或跳过完整入口来假称通过。已询问依赖升级归属，当时仅在 .tmp 准备两套22.2.0升级，仓库依赖文件尚未动；随后同步与当前验收边界见下节。

证据：.tmp/qa-efficiency-bc/{b-mutations,b-measurements,c-receipt-mutations,c-package-mutations,c-scope-mutations,c-missing-package,framework-result,ci-model,c-measurements}.json 及入口日志。需要继续完成逐场景真实测试变异、解决审计后终局同输入前后三轮、用户提交后的远端完整 CI；在这些验收结束前保留计划，不宣称 C 完成。

逐场景实际验证已完成：九种生成形态分别注回“Windows 时区 ID 被接受”的可编译运行时缺陷，旧/新共18次真实 xUnit 测试失败，编译均通过；恢复后18次测试绿，9次新汇总 guard 红。此验证只准备后端生成/运行时/单测/集成测试，未冒称前端完整矩阵绿。22.2 隔离升级的Identity与ResourceLocalization全阶段绿，前端59/47文件、381/300用例全通过，Unit65/27、Integration275/66均通过；两套audit退出0，仅moderate残留，原high阈值保持。当时仓库四份依赖文件仍未改；用户随后授权先在隔离副本验证升级，再同步依赖并继续，详见下节。补丁与依赖版本差异保存 .tmp/qa-efficiency-bc/angular-upgrade.patch 和 angular-upgrade/，升级复现不能把 npm --force/--legacy-peer-deps 当作可接受解法。

前后计时现在以两边完全相同的“52d541ac源码 + .tmp内22.2依赖”重新开始；原22.1阻断结果与重复打包试验仅为历史数据，不作为终局完成或收益。新源条件与脚本哈希见 c-final-input.json/c-candidate-hashes.json；两个原入口仍运行相同全部阶段，没有关闭审计或浏览器测试。

## 当前交付边界与提交协调

用户已授权 Angular 升级；默认、本地化隔离生成场景的完整阶段与 681 个浏览器用例绿后，已重读四份依赖文件 diff（均为空）并同步 22.2.0。未使用强制 peer 安装，保留 A 的缓存配置。此前九场景轮次在 external-login 浏览器阶段长时间挂起，该轮不作为测量；针对同一生成前端的单文件复测 9 例通过，但不能据此代替完整发现。

2026-10-01 会话权限从不受限变为 workspace-write / network restricted / approval never。完整 external-login 浏览器入口在启动测试服务时出现 `listen EPERM ::1:63315`，退出 1、用例执行数 0（`angular-external-full.log` / `angular-external-full-result.json`）。这是系统禁止本地监听，不是有效变异红，也不是依赖升级回归；不尝试绕过权限。完整九场景终局前后三轮、容器及新远端 CI 尚未验收，不能承诺已取得模型中的收益。需要恢复本地服务运行能力后续跑；远端改后仍需用户协调提交/推送。

本会话只执行过此前明确授权的首次 pull；没有 add/commit/push/stash。提交路径共 15 个（新增两脚本亦在其中）：

- `.github/workflows/ci.yml`
- `framework/build/check-docs-api-drift.ps1`
- `framework/build/test-package-consumption.ps1`
- `scripts/check-all.ps1`
- `scripts/test-template-matrix.ps1`
- `scripts/template-matrix-scenarios.ps1`（新增）
- `scripts/check-template-matrix-results.ps1`（新增）
- `docs/framework/quality-assurance.md`
- `docs/template/quality-assurance.md`
- `docs/plans/2026-10-01-qa-efficiency.md`
- `docs/framework/upgrade-0.13.0.md`（仅 §22 属于本组，§21 仍有另一会话在途内容，按整路径提交前须协调）
- `template/frontend/package.json`
- `template/frontend/package-lock.json`
- `template/.template.config/localization/frontend/package.json`
- `template/.template.config/localization/frontend/package-lock.json`

`check-all.ps1` 新增联系方式两行依赖用户会话的 `scripts/check-contact-info-logging.py`，该脚本及邮件实现不归本组；提交/推送须协调同时包含它，否则静态入口会因缺脚本失败。`oidc-e2e`、框架测试和 PostgreSQL 作业定义与冻结基线完全相同（YAML 对象比较），无删测试。最终有效事实保留在本计划直至上述验收完成，规则已上收到维护文档。

### B 终轮复测

在本任务矩阵不运行时，对相同无 bin/obj 冻结源码、副本同入口顺序三轮复测；不包含新联系方式两行（其成本单列），不是 runner CI 承诺。

| 入口 | 改前三次（秒） | 改后三次（秒） | 中位 | 中位减少 |
| --- | --- | --- | --- | --- |
| api | 4.556, 4.718, 4.301 | 2.329, 2.284, 2.370 | 4.556 → 2.329 | 48.88% |
| all | 28.027, 28.447, 28.599 | 28.570, 26.920, 26.857 | 28.447 → 26.920 | 5.37% |

各静态入口均 exit 0；原规则八例和完整扫描仍执行。原高负载轮另存 `b-measurements-shared-load.json`，终轮 `b-measurements.json` 与各逐入口日志保留，不删除不利首轮：首轮完整入口 28.027→28.570 秒，未改善。

最终可执行校验：工作树 `check-all.ps1` 全 28 项绿，实际墙钟 60.125 秒；actionlint、六份 PowerShell AST 解析、文档相对链接与 `git diff --check` 均通过。工作树包含其他会话在途内容与 bin/obj，不能拿这次 60 秒入口与 B 的无 bin/obj 冻结副本直接比较。汇总夹具及实际 workflow guard 62 条复验通过；联系方式规则 9 例与 741 文件扫描绿。C 的完整计时/容器/远端验收仍未完成，不以这些静态绿代替。

### 2026-10-01 再次开始验收

用户要求立即开始后已重新探测环境：IPv4/IPv6 监听均 EPERM，Docker socket permission denied；诊断保留 `c-environment-preflight.json`。实际执行 `python3 .tmp/qa-efficiency-bc/run-c.py` 在前置检查退出 2，没有启动矩阵，没有改写既有时长。

已验证五份冻结副本的四个依赖文件逐字节一致，最终矩阵/场景/汇总/包消费脚本仍匹配变异验收版本，见 `c-final-input-verification.json`。复测脚本增加仅用于 .tmp 测量的前置监听检查、40 分钟命令上限、180 秒无输出超时、时钟异常判无效；超时结束自己的进程组，失败轮次不进入成功关键路径。每次测量写入新证据目录，保留旧日志；消费者还会核对候选包 SHA256，汇总只接纳本轮新产出的结果，避免残留结果影响验收。未改正式 CI 或测试入口来绕开权限，也未添加生产开关。

下一步仍为恢复监听及 Docker 访问后运行这份三轮复测，再补容器和提交后的同候选完整远端 CI。当前环境没有能够完成这些验收的执行能力，不能称 C 已验收。

## 最新裁决：联系方式检查交回 DEF-04

用户选择方案 3：本批撤掉 `check-all.ps1` 的联系方式规则自检与扫描两行，脚本仍留工作区，脚本/登记/调用点修复与用例归用户后续批次。此前暂存快照验证确实命中 `NullEmailSender.cs:21` 原文 `message.To`，不是误报；本批不带该新增检查，避免提交依赖另一会话未完成的修复。API 合并及原 8 例继续保留。

按实际路径本批为 15 个文件：原暂存 16 个只排除联系方式脚本；`check-all.ps1` 仍有 API 合并修改。升级说明只提交已隔离的 §22，§20 修改和 §21 留在工作区。用户已认可 B 的全部变异/三轮测量和 CI 36752179167 的改前基准；不重复基线，提交后补新 CI 实际关键路径。1,070.498 秒 / 44.71% 继续标为模型。提交前须验证本批完整快照的全部静态入口；C 的完整实测与远端 CI 仍不记为已完成。
