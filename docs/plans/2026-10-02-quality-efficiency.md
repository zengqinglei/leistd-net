# 质量保障分层与提效实施

核查日期 2026-10-02。基线提交 `a9a11e12`（十场景）；CI 数据取自 PR 运行 `36828497318`、`36815865237`（两片调度后，九场景）及此前十次单矩阵运行。
本机为共享 Mac（同时有其他会话，`uptime` 负载 21–35），本机数字只用于同机成对比较，不作 CI 承诺。
用户 2026-10-02 裁决：接受 PR 档场景缩减，全部实施。全部验收完成后删除本文，长期规则上收到 `docs/framework/quality-assurance.md`、`docs/template/quality-assurance.md` 与 `template/docs/standards/testing.md`。

## 结论

1. **慢的不是用例数量，是每份用例的固定成本和重复执行。** 框架 1,700+ 个用例全量 21 秒；模板单测每场景不到 1 秒。删单元测试几乎省不出时间，本轮不以删测试为手段。
2. **最大的单项浪费是测试宿主里的 PBKDF2 600,000 次迭代。** 每个测试宿主播种管理员、每次登录都要算一遍。在同一生成项目中交替 A/B 测三轮，集成测试中位数从 54.9 秒降到 30.3 秒（−45%），用例 CPU 合计从 397 秒降到 185 秒。生成的业务项目同样受益。
3. **CI 关键路径是"打包 → 模板分片"。** 十个场景每个都跑全套集成测试和浏览器测试；Identity 家族有五个场景，其中三个单特性场景的代码行都能由其他场景生成。按"条件行覆盖"计算，PR 只需 6 个场景；全集放到合入后（develop 推送）、夜间和发布环节。ABP 自己的 PR CI 完全不跑模板，微软的指引也是完整集成测试放在构建服务器上执行。
4. **本地"每次都跑全量"缺一张按改动选检查的分层表。** 一次本地全量矩阵要 24.5 分钟；仓库规范只说"选最小充分集合"，没给出层级和时间预算，于是常常默认跑全量。

## 一、现有保障入口与实测耗时

### 1.1 入口清单

| # | 入口 | 内容 | 现在的触发方式 |
| --- | --- | --- | --- |
| E1 | `scripts/check-all.ps1` | 28 道静态闸门（14 道规则自检 + 14 道仓库扫描）：文档/Skill/API 漂移、退役术语、i18n、模板条件/using 守卫、时间源、DbContext、csproj、日志、测试布局/命名、XML 注释、文档骨架 | 本地每次改动；CI `docs-sync` |
| E2 | `dotnet build` + `dotnet test framework/...slnx` | 框架 28 个测试工程，约 1,700 个用例；CI 带真实 Redis | 本地；CI `test` |
| E3 | `pack-local-feed.ps1` | 打包候选框架包 | CI `framework-pack`；矩阵、PG 和 OIDC 脚本各自内部再打一次 |
| E4 | `test-package-consumption.ps1` | 68 个包隔离消费 | CI `package-consumption` |
| E5 | `test-template-matrix.ps1` | 10 场景，每场景：生成 → 形态断言 → restore/build → 运行时冒烟 → 单测 + 集成测试 → npm ci → spartan 检查 → lint → ng build → spec 发现 → 浏览器单测；按需容器 | 本地；CI 两片 + 汇总 |
| E6 | `test-template-postgresql-e2e.ps1` | 真实库迁移与租户隔离 | 本地按需；CI |
| E7 | `test-template-oidc-e2e.ps1` | 四服务真实 OIDC HTTP 闭环；`-IncludeBrowserScenarios` 加有头浏览器 | 本地按需；CI 只跑 HTTP |
| E8 | 生成项目 `husky` + `lint-staged` | 提交前只对暂存文件跑 eslint/stylelint/prettier | 业务项目本地 |
| E9 | 生成项目 `dotnet test` / `npm test` / `npm run lint` / `npm run build` | 业务项目自测 | 业务项目本地（模板没有自带 CI） |
| E10 | `release.yml` → 复用 `ci.yml` | develop/main 推送、工作日夜间（`develop`）都跑完整质量后才发布 | 合入后 |

### 1.2 本机实测（2026-10-02，冷热缓存注明）

| 入口 | 三次墙钟 / 秒 | 中位 | 备注 |
| --- | --- | --- | --- |
| E1 check-all | 77.1, 85.3, 69.1 | 77.1 | 其中"退役术语扫描"单项 38.7 秒：它先递归遍历 `template/frontend/node_modules`（504 MB），再把这些路径过滤掉 |
| E2 框架 build（增量） | 18.7（一次）；无改动重建 6.5 | — | |
| E2 框架 test `--no-build` | 21.5, 19.3, 22.2 | 21.5 | 本机没有 Redis，锁契约用例跳过 |
| E5 全量矩阵（含打包，不含容器） | 1,468（一次） | — | 24.5 分钟，十场景全绿；首个 `npm audit` 遇网络抖动后重试 |
| E5 中 identity-all-features 的集成测试，600k 迭代 | 55.9, 54.9, 48.3 | 54.9 | 348 个用例；与下一行交替测量，靠 DLL 哈希确认两版确实不同 |
| 同上，测试宿主改 1k 迭代（探针） | 30.3, 22.7, 30.4 | 30.3 | −45%；用例全通过 |

> 测量教训：第一次探针用普通 `dotnet build` 重建，结果覆盖了本 run 的私有 restore，构建失败而旧 DLL 照跑，出现了一组"快了 40%"的假数字。改为成对交替测量，并用 DLL 哈希证明两种变体确实不同之后才采信。

### 1.3 CI 实测（ubuntu-latest，4 vCPU）

两片调度后 PR 质量入口墙钟为 708 秒和 862 秒（此前单矩阵时代的中位约 1,320 秒）。

| 作业 | 运行时长 / 秒（36828497318 / 36815865237） | 是否在关键路径 |
| --- | --- | --- |
| docs-sync（E1） | 33 / 45 | 否 |
| test（E2，restore 17 + build 38 + test 46） | 119 / 111 | 否 |
| framework-pack（E3） | 56 / 46 | **是** |
| package-consumption（E4） | 171 / 179 | 否 |
| template-shards 1 | 462 / 771 | **是**（后一次） |
| template-shards 2 | 633 / 616 | **是**（前一次） |
| template-matrix 汇总 | 10 / 12 | 是 |
| postgresql-e2e（E6，自带打包） | 118 / 134 | 否 |
| oidc-e2e（E7，自带打包） | 249 / 251 | 否 |

每次运行约 30 个 runner 分钟；关键路径 = 打包约 1 分钟 + 最慢一片 8–13 分钟。

**单场景耗时（36828497318 日志时间戳，秒）**

| 场景 | 合计 | 其中集成测试 | 其中前端（npm ci + spartan + lint + build + 浏览器测试） |
| --- | --- | --- | --- |
| identity | 114 | 31 | 57 |
| identity-notifications | 167 | 45 | 90 |
| identity-external-login | 135 | 51 | 67 |
| identity-localization | 126 | 44 | 69 |
| identity-all-features | 138 | 53 | 72 |
| standalone | 85（容器另 42） | 27 | 50 |
| resource / resource-notifications / resource-localization | 各 55 | 2 | 约 45 |

每片还有约 45 秒准备时间：Chromium 安装 32–37 秒，三道源码闸门加两次 `npm audit` 约 10 秒。

### 1.4 重复与浪费

| 编号 | 现象 | 量级 |
| --- | --- | --- |
| W1 | 测试宿主播种和登录都用生产级 PBKDF2（600k 次） | 集成测试 CPU 的约 53% |
| W2 | 10 个场景都跑全套后端测试和浏览器测试；Identity 家族五个场景重复执行 280–350 个集成用例 | 两片合计约 17.5 分钟中的大头 |
| W3 | 每个用例新建一个宿主的测试类：ExternalLoginLink 19 个用例起 17 个宿主，ExternalAuthentication 22 个用例 15 个，Tenancy 30 个用例 13 个，TenantOidcFlow 7 个 | 修掉 W1 后，这几类成为集成测试的关键路径（最长一类 22.8 秒） |
| W4 | 退役术语扫描先遍历 `node_modules` 再过滤 | 本机每次 check-all 约 35 秒 |
| W5 | `spartan info/healthcheck` 每个场景跑一遍，而 `libs/ui` 没有条件块，只随两套 package 变体不同 | 每场景约 5 秒 × 10 |
| W6 | 框架在一次 CI 运行中被打包 3 次（pack、PG、OIDC） | 只多耗 runner 分钟，不在关键路径 |
| W7 | 本地没有分层预算，改动后常常默认跑全量矩阵 + PG + OIDC | 每次 30 分钟以上 |

近 78 个提交中只改文档的只有 3 个，按路径跳过作业收益很低。

## 二、官方实践与耗时范围

| 来源 | 结论 | 对我们的含义 |
| --- | --- | --- |
| [MS 测试金字塔](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/test-asp-net-core-mvc-apps) | 数百个单元测试应在几秒内跑完，每次推送前都跑；大型系统的完整集成测试在开发机上跑往往不现实，可以放到构建服务器上 | 本地分层；全集交给 CI 和合入后 |
| [ASP.NET Core 集成测试](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0) | 集成测试只用于最重要的基础设施场景，能用单测验证的就用单测；`IClassFixture` 共享宿主；推荐 SQLite 内存库 | 支持 W3 归组宿主；SQLite 另立议题 |
| [.NET 单元测试最佳实践](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices) | 单元测试应在毫秒级；过高的覆盖率目标适得其反 | 不设覆盖率门槛（现状一致） |
| [ASP.NET Core Identity `PasswordHasherOptions.IterationCount`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.passwordhasheroptions.iterationcount) | 迭代次数是官方可配置项，哈希里记录迭代次数，校验时按哈希里记录的值计算 | W1 用同样的做法：迭代次数可配置，默认值不变 |
| [EF Core 测试选型](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy) | 不建议用 InMemory，它也并不更快；真实库测试可以快且稳定 | 记为后续议题，不在本轮 |
| [Angular `ng test`](https://angular.dev/cli/test) | 有 `--include`、`--filter`、`--watch`；不传 `browsers` 时默认用 jsdom | 业务项目内循环用 `--include` |
| [Vitest CLI](https://vitest.dev/guide/cli) | `--changed`、`related` 只跑受影响用例 | 经 `ng test` 是否可用需实测，本轮只写 `--include` |
| ABP 自己的 CI（[build-and-test.yml](https://github.com/abpframework/abp/blob/dev/.github/workflows/build-and-test.yml)） | PR 只构建并测框架和核心模块，启动模板只在完整构建（`-f`）里跑；Angular 用 Nx affected | 把模板全集移出 PR 有先例 |
| [GitHub 必需检查排障](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/collaborating-on-repositories-with-code-quality-features/troubleshooting-required-status-checks) | 不要把会被路径过滤跳过的工作流设为必需检查；被 `if` 跳过的作业算成功；汇总作业要 `always()` | 沿用现有 `template-matrix` 汇总名，由它按档位校验 |
| [Playwright CI](https://playwright.dev/docs/ci) | 不建议缓存浏览器：恢复时间与下载相当 | 不做浏览器缓存 |

**各场景的耗时范围**（官方只给出前两行的量级，其余是据此推出的建议）：

| 档位 | 时机 | 目标耗时 | 依据 |
| --- | --- | --- | --- |
| L0 编辑循环 | 每次保存或小改动 | ≤ 1 分钟 | 官方：单元测试毫秒级，数百个几秒内跑完 |
| L1 提交前 / 阶段完成 | 交评审前 | ≤ 5 分钟（框架）/ ≤ 10 分钟（模板） | 官方：每次推送前跑单测；完整集成测试可以交给构建服务器 |
| L2 PR CI | 每次推送 PR | 墙钟 ≤ 8 分钟 | EF Core 3 万个真实库用例"几分钟"、每次提交都跑；ABP 的 50/30 分钟只是超时上限 |
| L3 全集 | develop/main 推送、夜间、发布 | 不设严格预算，完整执行 | 官方：完整集成测试放构建服务器 |

## 三、优化方案

### 3.1 框架仓库视角（leistd-net 自身）

| 项 | 做法 | 接替与证据 | 预估收益 |
| --- | --- | --- | --- |
| R1 分层执行表 | 在 `docs/framework/quality-assurance.md` 写 L0–L3 表，并给出"改动路径 → 必跑入口"映射，两个开发 Skill 只引用它。默认规则：只改框架某家族 → 家族测试 + check-all，契约变化再加打包/消费；只改模板某特性 → 一个"含该特性"的场景 + identity-all-features；参数模型、条件块或公共生成逻辑改动 → PR 档六场景；PG/OIDC/容器只在触及数据库、认证、部署时本地跑，其余交给 CI | 不删任何检查，只决定在哪一档跑 | 典型模板改动本地从 30 分钟以上降到 5–8 分钟（−75% 以上，按单场景约 2.5 分钟估算）|
| R2 PR 档场景覆盖集 | 场景定义加 `Pr` 标记，PR 档 6 个场景：identity（默认形态）、identity-all-features、identity-notifications、resource-notifications、resource-localization、standalone-external-login。push develop/main、夜间、发布跑全部 10 个。容器检查改挂 standalone-external-login | **接替者**：新增静态闸门 `check-template-scenario-coverage`，对全部模板文件求值嵌套条件和文件级 modifiers，要求每个条件行至少由一个 PR 档场景生成，否则失败；配 `--self-test`。现状算出的最小覆盖为 5 个且唯一，另加默认形态 identity。汇总作业按档位核对场景集合 | PR 场景时长从约 1,050 秒降到约 640 秒；两片关键路径从 8–13 分钟降到约 6 分钟，PR 墙钟中位从约 785 秒降到约 470 秒（−40%），runner 分钟从约 30 降到约 23 |
| R3 测试宿主降低哈希成本 | `PasswordHasher` 的迭代次数改为 Options，默认 600k 不变（`PasswordHashingTests` 仍断言默认值）；`ProjectWebApplicationFactory` 设 1k。PG/OIDC 端到端仍走生产默认值 | 不改断言；哈希头里本来就记录迭代次数，校验路径不受影响；成对 A/B 已实测 | 集成测试 −45%（本机），每个 Identity 场景约省 15–25 秒；同时惠及业务项目（B1）|
| R4 宿主归组（**撤销**） | 原拟合并派生宿主 | 实施前核查前提不成立：ExternalLoginLink 等类的用例依赖空库（断言精确用户名 `alice`/`zhang_san`、复用同一邮箱、`.Single(u => u != "alice")` 全库计数、改租户级"强制两步验证"设置）。共享宿主须改写断言，违反"不改有效断言"；宿主启动成本已由 R3 降低 | — |
| R5 退役术语扫描改用 `git ls-files` | 只取已跟踪和未跟踪但未忽略的文件，不再遍历 `node_modules` | 变异验证：新建未跟踪文件写入退役词仍要变红 | 本机 check-all 约 77 → 40 秒 |
| R6 spartan 检查每套变体一次（**撤销**） | 原拟每套 package 变体只跑一次 | 实施前核查前提不成立：healthcheck 会扫描整个项目源码（含 `src/` 的条件代码）找过时 API，不只看 `libs/ui` 与依赖；每变体一次会漏掉只在部分场景生成的组件 | — |

**不采纳**（附原因）：

- 按路径跳过 CI 作业：近 78 个提交里只有 3 个只改文档，收益太小。
- 删单元测试：用例本身很便宜，删掉省不出时间。
- 前端改 jsdom / 关隔离：此前已知隔离不可关；浏览器测试每场景只有 11–20 秒。
- 三片或动态调度：R2 之后两片已足够。
- 缓存浏览器：Playwright 官方不建议。
- TemplateVerifier 快照：十套快照的维护量大，且现有形态断言和条件闸门已覆盖它能抓的问题。
- MTP / xUnit v3：官方没给出性能数据。
- InMemory → SQLite：这是测试真实性议题，要改生产代码里的数据库选择，另立评估。
- 消除 W6 的重复打包：不在关键路径。

### 3.2 业务项目视角（模板生成的项目）

| 项 | 做法 | 预估收益 |
| --- | --- | --- |
| B1 | 同 R3：生成项目自带"测试宿主低成本哈希" | 业务项目 `dotnet test` 中集成测试 −45% |
| B2（**随 R4 撤销**） | — | — |
| B3 | `template/docs/standards/testing.md` 增加分层命令和时间预算：L0 `dotnet test <项目> --filter "FullyQualifiedName~<类>"`、`ng test --include <目录>`；L1 先跑单测项目、受影响的集成类、`npm run lint`（已有 lint-staged）；完整 `dotnet test` / `npm test` 放提交前和 CI | 业务项目日常反馈从分钟级降到秒级 |

模板目前不带 CI 流水线，业务项目的 CI 由各项目自选平台，本轮不新增。

### 3.3 规范调整

- `docs/template/quality-assurance.md` 里"每片保留完整阶段""十场景全集"的要求改为按档位表述：L3 必须是全集；L2 按覆盖闸门选出的场景集执行，但每个入选场景仍跑完整阶段。
- 删除或替换检查仍需变异验收，这条不变。**调度档位的调整**（同一检查改到哪一档执行）以覆盖闸门 + 全集仍然执行作为验收，不要求逐场景注入缺陷。
- 效率证据：调度类改动以真实 CI 运行（至少 3 次）的中位数验收；本地同机成对交替测量只用于单项变更。

## 四、验收标准

| 编号 | 标准 | 证据 |
| --- | --- | --- |
| A1 | PR 质量入口墙钟（`created_at` → 最后一个必要作业 `completed_at`）连续 3 次真实 PR 运行中位 ≤ 8 分钟；runner 分钟 ≤ 24 | `gh api` 作业时间表 |
| A2 | 全集档（develop 推送或 `workflow_dispatch full`）仍跑 10 个场景，五类阶段全部通过；汇总作业在缺场景、多场景、档位与集合不符时失败 | 一次全集运行 + 汇总夹具变异（删一个场景、伪造档位，各须变红） |
| A3 | 覆盖闸门：注入一行只由非 PR 档场景生成的条件代码（如 `#if (IncludeLocalization && !IncludeNotifications)`）→ check-all 失败并指出文件和行；撤回后通过；自检正反例齐全 | check-all 输出 |
| A4 | 哈希：生产默认仍为 600k（`PasswordHashingTests` 不改断言）；测试宿主为低成本；把 Options 默认值改成 1k 时，那条断言须变红；生成项目的 Api 启动配置里不出现迭代次数（只有测试宿主覆盖） | 变异记录 + 测试输出 |
| A5 | 集成测试：identity-all-features 同机成对交替三轮，中位降幅 ≥ 30%；用例数不变 | 三轮数据 + DLL 哈希 |
| A6 | 宿主归组后用例数、名称不变；用 `--filter` 单独跑和整组跑都通过（证明用例间不互相依赖） | 测试输出 |
| A7 | check-all 本机中位降幅 ≥ 30%；退役术语在新建未跟踪文件里仍被发现 | 三轮数据 + 变异 |
| A8 | 端到端：本地十场景全量矩阵绿；PG 与 OIDC 端到端绿；一次真实 PR 档 CI 和一次全集档 CI 全绿 | 日志与 run 链接 |
| A9 | 文档：L0–L3 表、路径映射、业务项目分层命令落位，check-all 通过 | diff |

## 五、实施结果

本地验收全部完成；A1、A2 及 CI 中的 OIDC 端到端待提交推送后用真实 CI 验收。

| 项 | 状态 | 证据 |
| --- | --- | --- |
| R5 | 完成 | 枚举 18.7 → 0.2 秒；扫描 1,723 → 1,743 个文件（补上原写法漏掉的 `.agents` 隐藏目录）；新建未跟踪文件、隐藏目录写入退役词均变红，撤回变绿；`core.quotepath=off` 防非 ASCII 路径静默跳过 |
| R3 / B1 | 完成 | 默认 600k 不变；新增"旧工作因子密文仍可校验"用例；默认值改 1k 时断言变红、恢复变绿；部署文档补 `PasswordHash__IterationCount` |
| R2 | 完成（本地） | 覆盖闸门自检 11 例；三类变异均红、撤回均绿；收据夹具 9 例符合预期；实施中修复 dot-source 变量覆盖 `-Tier` 的缺陷；容器场景简化为两档共用的 `standalone-external-login` |
| R1 / B3 / §3.3 | 完成 | 分层表、路径映射、维护规范、两份开发 Skill、生成项目 `testing.md`；`npm test -- --include src/app/core` 实测只跑 19/59 个文件，`--filter` 只跑 8 个用例 |
| R4、R6、B2 | 撤销 | 见 §3.1：实施前核查发现前提不成立 |

| 验收 | 结果 |
| --- | --- |
| A3 覆盖闸门 | 达成（见 R2） |
| A4 哈希默认值 | 达成（见 R3） |
| A5 集成测试 | 部分达成。同一生成项目成对交替三轮（负载 30 左右）：CPU（user+sys）中位 124.3 → 57.6 秒（−54%）；墙钟中位 39.4 → 27.9 秒（−29%，略低于 30% 门槛）。上午探针一轮为 54.9 → 30.3 秒（−45%）。4 核 CI 受 CPU 限制，以 A1 的真实 CI 数据为准 |
| A6 宿主归组 | 随 R4 撤销 |
| A7 check-all | 达成：30 道（新增 2 道）三轮 43.1 / 43.1 / 39.4 秒，中位 43.1，基线 77.1，−44% |
| A8 本地端到端 | 达成：PR 档两片 492 / 523 秒全绿（第 2 片含容器），真实收据经汇总校验通过；全集独有 4 场景 398 秒全绿；PostgreSQL 端到端 94.8 秒通过。OIDC 端到端脚本当时正被另一会话修改，交由 CI 验证 |
| A9 文档 | 达成：check-all 30 道全绿 |
| A1 / A2 | 待提交推送后：3 次真实 PR 运行取中位；一次 `workflow_dispatch tier=full` |

评审（独立会话）无阻断问题，处理如下：M1 写明"行覆盖、组合交互由 full 档兜底"（不改为成对覆盖：那要把两个场景加回 PR 档，抵消缩减）；M2 本地指引补关闭侧场景并恢复"开、关两侧"规则；L1–L5、L7、L8 已修；L6 分片均衡以实测为准（492 / 523 秒），不调整。

## 六、实施顺序

1. R5、R3/B1、R6：改动小，收益独立。
2. R2：覆盖闸门 + 场景档位 + CI 档位输入 + 汇总校验；容器归属迁移。
3. R4/B2：宿主归组。
4. R1、B3 与 §3.3：文档、Skill 引用、维护规范。
5. A1–A9 验收；有效结论上收到稳定文档，删除本文。
