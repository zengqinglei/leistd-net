# 质量验证效率优化结果

本轮保留 Framework 公共契约、Template 生成组合与生成业务项目自身业务三类责任。改动减少独立进程和重复打包，不删真实 PostgreSQL/OIDC 验证，不修改数据库隔离与生产安全默认值。

## 实现

- 全部包仍各有独立的 `PackageReference` 项目，用临时 slnx 一次还原、一次构建调度；完整包集合、DLL/XML/家族文档/NuGet.md、源映射与错误退出检查保留。
- PostgreSQL/OIDC CI 下载同候选的包 artifact；独立的 hive、解包目录、数据库和清理保持不变。人工脚本未传 `-SkipPack` 时仍自行打包。
- CI 容器与 main 发布范围以 `git diff --no-renames --name-only` 同时考虑重命名两侧，无法确定基线时保留原来的保守执行/失败行为。
- 密码策略重复断言合入领域单测，实际管理员初始化仍由集成测试验证。六个不使用宿主的规则/哈希类移入单测，保持原断言与条件裁剪；移动文件不计为数据库准备提速。
- 本地验证规范只补缺口：框架阶段完成跑全集覆盖反向依赖；模板探索与最终重新生成分开；生成业务项目继续使用自身规范。修正重跑计时与依赖等待口径。
- 后续按实际耗时交换两个PR场景的分片归属，保留6场景、3分片与全部阶段；最新墙钟预算验收见末节。

## 本机完整包消费对照

源码基线 `df79b494`，同批68包SHA256逐轮核对；Mac14,7、8核/16 GiB、.NET SDK 10.0.301、PowerShell 7.6.4。前后交替三组，两个入口每次清空同一解包/项目目录，保留HTTP下载缓存；不是完全冷启动。计时包含PowerShell入口、元数据/包集检查、项目生成、还原、构建与汇总，构建并行度4。共享机器有其他负载。

| 组 | 优化前秒 | 优化后秒 | 前后独立项目 / DLL |
| --- | ---: | ---: | --- |
| 1 | 154.843 | 12.440 | 68 / 68 |
| 2 | 132.938 | 10.131 | 68 / 68 |
| 3 | 121.910 | 10.108 | 68 / 68 |
| 中位 | **132.938** | **10.131** | 六次均成功 |

完整入口中位少122.807秒，耗时下降92.4%；dotnet启动从136次变2次。此为本机同输入的完整入口结果，不把它当CI墙钟收益；作业不在当前模板关键路径上。

临时slnx是正式验证入口按本次包清单生成的构建产物，不是过渡实现。旧入口为68个独立项目逐一启动restore/build；新入口保留独立项目和各自还原图，用一个slnx批量调度一次restore、一次build（最多4个构建节点），减少重复进程启动及项目评估。它影响包交付验证的运行成本，不改变框架运行时、业务功能、包内容或验证责任；下一轮重建临时目录，`-PackageIds`仍按选定包生成项目。

## 验证与后续测量

两处实际workflow的PowerShell片段由 `python scripts/test-workflow-change-scope.py` 在隔离Git仓库验证：跨边界移动、删除、同目录改名、无关文档、多提交、未知/零基线；空PR基线走merge-base回退，merge-base等于HEAD时保守验证。删除 `--no-renames` 可复现旧漏判。它是范围逻辑修改时的夹具，不加入每次功能开发的静态总入口。

静态30项通过；实际生成的全特性Identity后端通过100条单测、329条集成测试、0跳过。9类消费缺陷的旧/新入口均失败且定位到包：缺包、孤儿、多版本、缺DLL/XML/家族文档/NuGet.md、声明的依赖版本不可解析、源映射错误。恢复后68包合法入口通过。密码策略6种可编译变异（空值、短值、最小边界、最大边界两侧、长口令）共12个新旧变体被原测试集合/接替单测拒绝，恢复后通过；其中长口令原来只由SecureDefaults覆盖，现已明确接替。

原始命令、全部值、包哈希、日志与夹具位于本机 `.tmp/quality-optimization-20261003/`，不随模板/NuGet分发、不上传临时凭据。先前历史CI只用于诊断：PR三次尝试496/479/575秒分别计时；Chromium57样本中位29秒/p90 43秒，345秒仅一次；消费15样本作业中位171秒/步骤164秒。这些多SHA历史分布不充当前后性能验收。

认证/租户/到期实现未改，本轮不执行可选浏览器SSO/MT和实际十分钟等待；组件Chromium及HTTP结果不冒充这些证据。CI范围裁剪、跨作业静态/audit证明、浏览器环境替换与PG共享容器均未实施。

## CI 三轮前后对照

两个版本都由独立分支手动执行 `ci.yml -f tier=pr`，每组3次，固定各自候选：优化前 `df79b494`，优化后 `7ebbf228`。两组在同一天先后运行，均为ubuntu-latest，实际.NET SDK 10.0.401；锁文件、Framework源码、场景集和工具配置相同，改动包括调度/消费实现与明确重复测试整理。GitHub runner与网络没有完全受控，不能把总时间的全部变化归因于代码。每次hive/解包/数据库独立，HTTP/npm及runner预装条件沿用CI默认，未称完全冷启动。

| 版本 / run | 墙钟秒 | runner合计秒 |
| --- | ---: | ---: |
| [前1：37085483004](https://github.com/zengqinglei/leistd-net/actions/runs/37085483004) | 654 | 2118 |
| [前2：37085511675](https://github.com/zengqinglei/leistd-net/actions/runs/37085511675) | 509 | 1907 |
| [前3：37085514140](https://github.com/zengqinglei/leistd-net/actions/runs/37085514140) | 475 | 1924 |
| [后1：37086043602](https://github.com/zengqinglei/leistd-net/actions/runs/37086043602) | 486 | 1492 |
| [后2：37086045946](https://github.com/zengqinglei/leistd-net/actions/runs/37086045946) | 514 | 1526 |
| [后3：37086048853](https://github.com/zengqinglei/leistd-net/actions/runs/37086048853) | 570 | 1703 |
| 前 → 后中位 | **509 → 514** | **1924 → 1526** |

墙钟从8分29秒变为8分34秒，增加5秒（约1%），本轮没有取得CI等待时间提速。runner合计少398秒（20.7%），是这6次运行的观测结果，不承诺稳定比例。

PR墙钟预算保持≤8.5分钟（510秒），首轮复用与批量消费实施后的中位514秒超出4秒，该阶段时间预算尚未达标；后续分片均衡的最新验收见末节。功能与质量验证通过不能替代时间预算验收；不因runner与网络波动放宽目标，也不以这3组样本推断稳定的性能退化。

| 作业运行时间中位 | 前秒 | 后秒 | 变化 |
| --- | ---: | ---: | --- |
| 全包消费（含准备/下载） | 164 | 34 | 少130秒，79.3% |
| PostgreSQL端到端 | 122 | 87 | 少35秒，28.7% |
| OIDC端到端 | 285 | 176 | 少109秒，38.2% |

每轮这3个作业的时间之和中位为565→301秒，少264秒。总量其余变化含矩阵、准备与网络波动，不把398秒都算为消除重复工作的收益。候选打包入口从3处变1处；PG/OIDC增加了等待pack的依赖，但优化后OIDC比最后质量作业早251/279/292秒完成，PG早349/369/418秒完成，均未进入关键路径。

六次CI全部成功，全部6场景完整Backend/Runtime/Lint/Frontend/Test与容器责任回执已下载，用 `check-template-matrix-results.ps1 -Tier pr -ContainerSmoke` 复核通过。每次框架1650条通过；前端2242条执行通过且前后相同；后端跨场景执行由1759→1743（删6个重复执行、单测新增2个对应字符串，每个有本地身份场景净少4个，共4场景），0意外跳过。它们是跨场景执行数，不能当独立设计数量。

## 真实 PostgreSQL 成本分解

只在临时生成的全特性Identity内加入计时，正式模板夹具未新增配置或计时逻辑。三轮集成测试329条通过；入口墙钟30.099/28.930/32.126秒，中位30.099秒，包含dotnet进程启动/收尾。事件起止时间保留，可观察并行重叠。

| 阶段 | 每轮次数 | 三轮累计秒 | 中位累计秒 |
| --- | ---: | --- | ---: |
| 容器启动 | 1 | 3.728 / 4.014 / 3.920 | 3.920 |
| 模板库迁移 | 1 | 0.686 / 0.658 / 0.684 | 0.684 |
| 库克隆 | 92 | 2.688 / 2.980 / 2.688 | 2.688 |
| 宿主创建（含嵌套数据库准备） | 146 | 55.195 / 59.437 / 52.733 | 55.195 |
| 删除调用 | 174 | 4.725 / 5.105 / 4.564 | 4.725 |

宿主并行且包含其他阶段，这些累计时间不能相加或直接从墙钟扣减；删除调用不等于不同数据库数。本机容器/迁移约4.6秒，尚不足以支持给分发夹具新增共享容器配置。CI上的具体分解还没有计时探针，因此不把本机结果当4vCPU runner的归因；本轮保留真实PG与隔离，后续只在有重复、可量化瓶颈时重新评估。

## 生成业务项目的编辑回路

在独立生成的业务项目中演练“finance_角色创建后受保护”的业务规则：先新增测试确认失败，再实现规则。领域单测验证大小写和普通角色对照；真实PG/HTTP验证创建、持久化读取、更新、受保护删除拒绝、普通角色正常删除、匿名401及普通用户403。只消费候选NuGet包，执行业务项目自己的dotnet入口，不调用仓库矩阵。该业务示例仅在临时目录，不写回模板。

| 档位 | 3轮入口秒 | 中位秒 | 责任 |
| --- | --- | ---: | --- |
| L0目标规则（含本次构建） | 3.162 / 2.785 / 2.867 | 2.867 | 3条业务规则，快速反馈 |
| L1后端全集（含构建） | 34.757 / 35.232 / 39.114 | 35.232 | 103条单测 + 330条真实PG集成，0跳过 |

这是已有过滤能力落到实际业务后的分层证据，不是用少跑测试冒充等价全量提速。还原与初次生成单列于原始日志，表内用同一已还原项目的 `dotnet test -c Release --no-restore`，不使用旧二进制；无需每轮编辑承担L1全部责任，交付仍执行L1。未变更UI，本示例没有新增浏览器用户流程；仓库矩阵保留完整Chromium验证。

## 保障入口盘点与原始基线

以下为f39bcdcc源码基线（质量入口与df79b494等价）的盘点；不是优化后的计时。本轮静态总入口仍为19个直接脚本、30项执行，自检与生产扫描各有责任，廉价规则不按数量删除。总入口三轮50.128/55.825/46.168秒，中位50.128秒；各子项用脚本自身0.1秒精度的中位，子项中位之和不等于完整入口中位。新增范围夹具不加入此总入口。

| 脚本（相对仓库根） | 保护对象 / 必要性 | 自检中位秒 | 扫描中位秒 |
| --- | --- | ---: | ---: |
| `framework/build/check-docs-sync.ps1` | 包家族文档、索引与源码对应；框架交付需要 | — | 1.6 |
| `framework/build/check-docs-api-drift.ps1` | 文档 API 引用、自检、源码索引；避免发布失真 | 合并 | 2.8 |
| `scripts/validate-skills.ps1` | Skill 元数据及文档引用 | — | 2.1 |
| `scripts/check-retired-terms.ps1` | 已退役符号、分发层错误表述 | 0.9 | 9.5 |
| `scripts/check-i18n-keys.ps1` | 双语键、错误码与词条、scope | 1.1 | 4.4 |
| `template/scripts/check-operation-action-i18n.py` | 动作码与句子模板集合一致；随业务项目分发 | 0.1 | 0.1 |
| `scripts/check-template-symbols.ps1` | 未登记/退役条件符号 | — | 2.5 |
| `scripts/check-template-conditional-blocks.py` | 嵌套条件结构和预处理标记 | 0.1 | 0.2 |
| `scripts/check-template-scenario-coverage.py` | PR 条件行覆盖、24 参数组合可达性 | 0.1 | 5.9 |
| `scripts/check-using-guards.py` | 全符号组合的 using/import、IVT、XML、空块 | — | 8.2 |
| `scripts/check-async-boundaries.py` | 动态连接路径不走同步解析 | — | 0.2 |
| `scripts/check-clock-access.py` | 时间源可替换及精确豁免 | 0.1 | 0.3 |
| `scripts/check-dbcontext-access.py` | 业务上下文访问、租户/UoW 边界 | — | 0.1 |
| `scripts/check-csproj-conventions.py` | Core 依赖、继承属性、项目/包约定 | — | 0.2 |
| `scripts/check-contact-info-logging.py` | SMTP 等未由运行测试覆盖的联系方式日志调用点 | 0.1 | 0.2 |
| `scripts/check-test-layout.py` | 家族测试项目、目录、slnx 登记 | 0.2 | 0.1 |
| `scripts/check-test-names.py` | 中英文测试名规范 | 0.1 | 1.3 |
| `scripts/check-doc-comment-shape.py` | XML 私有成员/Markdown 形态 | 0.2 | 0.3 |
| `scripts/check-docs-skeleton.py` | 组件文档骨架及空壳 | 0.1 | 0.1 |

辅助实现 `check-i18n-scopes.py` 与 `vendor/skill-creator/quick_validate.py` 由上述入口调用，不重复计为独立阶段。

| 动态入口（历史本机单轮） | 秒 | 保障对象 |
| --- | ---: | --- |
| framework restore | 7.181 | 依赖解析 |
| framework Release build --no-restore | 23.608 | 编译、分析器、XML |
| framework Release test --no-build | 30.328 | 28项目/1650条组件契约，Redis可用 |
| pack-local-feed（构建后热打包） | 19.108 | 68个候选包 |
| test-package-consumption（旧） | 126.899 | 包内容与逐包隔离消费；正式前后比较见本报告三轮表 |
| template-matrix -Tier pr -SkipPack | 737.799 | 6场景完整前后端，本机串行，不与CI三分片墙钟混算 |
| postgresql-e2e -SkipPack | 74.583 | 真实PG多库/迁移/物理隔离/DDL拒绝 |
| oidc-e2e -SkipPack | 185.887 | 4服务HTTP边界，10场景/306断言 |

其他按影响执行的入口：矩阵 `-ContainerSmoke` 验API/Migrator镜像与运行时；`check-template-matrix-results.ps1` 核对回执；OIDC `-IncludeBrowserScenarios`、`-IncludeMultiTenantScenarios`、`-IncludeExpiryWait` 分别验浏览器、MT0–MT5与真实到期；前端npm lint/build/test由生成场景运行；最终版本包在Release推送前打包/隔离消费。发布后等待包源索引的步骤已在df79b494删除，不恢复。

## 官方依据与本轮取舍

外部框架参考调研保留在本机原始计划与两轮评审证据中；长期仓库报告遵循现有文档分发边界，不引入其API或实现约定。

| 官方资料 | 原则 | 对本仓库的应用 |
| --- | --- | --- |
| [Microsoft 单测最佳实践](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices) | 单条规则测试毫秒级，隔离、可重复、自判定；测试成本与价值匹配 | 纯规则移入 UnitTests，避免为转发/DTO/空实现写测试；慢方法先查初始化与错误分类 |
| [Microsoft 测试分层](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/test-asp-net-core-mvc-apps)、[ASP.NET Core integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0) | 能用单测就用单测；不要穷举每个 IO 组合；完整集成可放构建服务器 | 编辑循环跑最小相关集；HTTP 集成保留装配、数据/权限和代表性错误映射 |
| [EF Core 测试选型](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy) | 本地真实库也可很快；fake Provider 不保证生产查询和约束 | 优先优化容器、迁移、播种/克隆成本，不为了快退回 InMemory |
| [Angular Testing](https://angular.dev/guide/testing)、[组件测试](https://angular.dev/guide/testing/components-basics)、[CLI test](https://angular.dev/cli/test) | 默认 Vitest/Node DOM 模拟；DOM/浏览器 API 需要对应环境；支持 include/filter/watch | 纯规则与类测试不用创建 TestBed；Node/浏览器分开仅作候选实验，先验证现有 spec 的真实依赖 |
| [Vitest 性能指南](https://vitest.dev/guide/improving-performance.html) | 先区分环境/导入/初始化/测试成本，再测配置；隔离有成本也有语义 | 测性能时记录完整入口，保留现有隔离；不直接套 `--no-isolate` |
| [Playwright CI](https://playwright.dev/docs/ci)、[浏览器安装](https://playwright.dev/docs/browsers) | CLI 安装或官方镜像；默认不建议缓存浏览器，Linux 系统依赖不能由浏览器缓存替代 | 统计准备时间分布后移出环境替换任务；保持现有Chromium，外部偶发波动不算持续优化收益 |
| [GitHub 缓存](https://docs.github.com/en/actions/reference/workflows-and-actions/dependency-caching) | 依赖缓存按输入键匹配，命中有范围与恢复成本 | 优先官方 npm/NuGet 工具链缓存，不共享可变生成目录，不缓存“已经测试通过”的笼统布尔值 |

上述资料没有规定 .NET/Angular 的 CI 一律必须 N 分钟，也没有规定固定单测/集成比例。分层预算是本仓库的工程反馈目标，实际验收使用本报告数据，不称“官方规定最佳范围”。

## 首轮全集验证

[full运行37087252676](https://github.com/zengqinglei/leistd-net/actions/runs/37087252676) 在候选 `a852ccac` 上全部成功。10场景、2分片的全部阶段与容器回执已下载，本机以 `-Tier full -ContainerSmoke` 复核通过；框架1650条、模板后端跨场景2944条、Chromium跨场景3696条执行通过，后端跳过0条。该候选覆盖批量消费、共享候选包、范围修复与测试迁移的主体实现。后续代码审查补充PG空包源前置检查、范围夹具覆盖/编码、密码测试名称/注释及根文档，以针对性验证收尾，不将旧full运行冒称后续提交的同SHA验证。发布源/版本与正式包推送未执行。

审查修正验证：范围夹具8组全部通过，含空PR基线的merge-base回退与merge-base等于HEAD的保守分支；PG实际脚本在隔离目录拒绝缺失/空目录/非框架包/文件路径4类包源，均在生成和还原前失败，正常包源的真实PG端到端通过。重新生成identity-all-features并完成后端构建、100条单测与329条集成测试，0跳过；此次未改前端与运行时实现，定向入口显式跳过前端及运行时冒烟，不替代前述full证据。30项静态闸门通过，原始日志以`review-`为前缀保存在同一证据目录。

## PR墙钟预算收尾：分片均衡

PR #33已合入develop（`467bd455`），[合入后全集流水线37090781646](https://github.com/zengqinglei/leistd-net/actions/runs/37090781646)成功，full10场景与容器回执已下载复核。独立方案审查后仅调整`scripts/template-matrix-scenarios.ps1`的PR归属及分片说明：

| PR分片 | 场景 |
| --- | --- |
| Identity默认产物与可选特性全开 | identity、identity-all-features |
| Resource通知与Standalone外部登录（含容器） | resource-notifications、standalone-external-login |
| Identity通知与Resource本地化 | identity-notifications、resource-localization |

6个入选场景、3个分片、每个场景的全部阶段保持；full归属与场景次序逐项相同，容器仍挂在standalone-external-login。原重片在3次历史运行都最后完成，容器API构建命令跨度43–52秒。模型只支持约30–50秒的调度收益预期，旧片额外排队不归因于交换。矩阵从脚本动态输出及artifact聚合沿用[GitHub matrix指南](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/run-job-variations)所支持的机制，没有新增动态调度器或日常闸门。

开始前登记3对验收及失败规则，前测固定`467bd455`临时ref，后测固定`b4595a0c`；每对同时触发`workflow_dispatch tier=pr`，3对依次进行。前后Framework、Template及workflow的Git tree相同，场景参数/前端/lint标志相同；实际SDK全部10.0.401，runner/image版本全部20260901.588/20260927.320.1。每run的hive、包缓存、数据库仍独立，沿用默认HTTP/npm缓存，runner与网络未完全受控。68包的ID/版本逐对一致，哈希逐包记录，Git提交来源信息变化不冒称同批二进制哈希相同。没有重跑、替换或剔除CI样本。

| 对 | 前测run / 墙钟秒 / runner秒 | 后测run / 墙钟秒 / runner秒 | 墙钟变化秒 |
| --- | --- | --- | ---: |
| 1 | [37091816307](https://github.com/zengqinglei/leistd-net/actions/runs/37091816307) / 465 / 1600 | [37091815612](https://github.com/zengqinglei/leistd-net/actions/runs/37091815612) / 494 / 1701 | +29 |
| 2 | [37092362596](https://github.com/zengqinglei/leistd-net/actions/runs/37092362596) / 508 / 1589 | [37092362604](https://github.com/zengqinglei/leistd-net/actions/runs/37092362604) / 443 / 1508 | −65 |
| 3 | [37092927304](https://github.com/zengqinglei/leistd-net/actions/runs/37092927304) / 504 / 1495 | [37092927673](https://github.com/zengqinglei/leistd-net/actions/runs/37092927673) / 469 / 1733 | −35 |
| 前→后中位 | **504 / 1589** | **469 / 1701** | **−35** |

墙钟按各次attempt的run_started_at至template-matrix完成计，runner为各质量作业运行时间之和。中位减少35秒（6.9%）；三次后测494/443/469秒均低于510秒，本轮预先登记的时间验收达标，不称稳定SLA。第一对反而慢29秒，没有每次都提速：其前后均由未改动的Identity默认片决定结束，后测该片运行时间增加24秒，依赖/开始偏移与聚合尾部共同多5秒。后测3次最后完成的分片依次是Identity默认产物与可选特性全开、Identity通知与Resource本地化、Identity默认产物与可选特性全开；新的Resource通知与Standalone外部登录分片（含容器）三次均未最后完成，原重片的负载集中得到缓解，不代表每对关键路径都改变。runner中位反而增加112秒（7.0%），保留该观测，不宣称本轮节省计算消耗，也不把所有波动归因于分片交换。

第二对后测的聚合尾部为51秒，其余5次为7–14秒：最后一个场景分片在392秒完成，聚合作业396秒开始、首个步骤433秒开始，约37秒为runner准备延迟。这段波动使后测更慢，保留在443秒实测值中，不扣除或计为分片优化收益，也说明聚合准备时间尚未完全受控。

6次全部质量作业成功，均核对6场景完整阶段及实际容器回执。每次Framework1650、后端1743、Chromium2242条通过，后端跳过0条；本轮没有减少测试执行数。两个移动场景的backend restore跨度变化−0.801至+1.407秒，npm ci变化−3.862至+4.259秒，未观察到明显的预热损失，不能把命令跨度当CPU时间。所有job的runner名称/ID、开始偏移（含依赖等待）、准备/场景/收尾及聚合信息均保存。

本地条件覆盖自检/完整扫描与30项静态闸门通过；PR成员每场景一次、full成员和次序不变的对照通过。使用真实后测回执运行聚合通过；在其副本中移走Standalone所在片的回执，检查按缺片拒绝。原始登记、命令、日志、候选包哈希、回执及完整值位于本机`.tmp/quality-wallclock-20261003/`。

[最终full运行37093592615](https://github.com/zengqinglei/leistd-net/actions/runs/37093592615)在同一代码候选`b4595a0c`上10项作业全部成功，包含真实PG和OIDC端到端。下载回执以`-Tier full -ContainerSmoke`复核10场景、2分片全部阶段及容器通过；原始日志核对Framework1650、后端2944、Chromium3696条通过，后端跳过0条。后续提交只修订根报告及计划，`.github/`、`framework/`、`template/`、`scripts/`与该验证候选相同。独立只读复算6次运行与代码审查确认无阻塞、时间验收符合预先登记规则；关键分片与聚合准备延迟两处报告意见已采纳，最终full证据也已独立复核。本轮临时benchmark远端ref在验收后清理，完成计划按仓库生命周期移除，历史保留在Git中。
