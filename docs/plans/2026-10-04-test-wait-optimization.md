# 测试等待时间优化实施计划

状态：a9c94b19 基线实现、24 样本与独立复审完成，已提交 b2a38bb6 并创建 MR #53；develop 随后合入 #52（07ac3cb6），已正常合并为 c16413fe，正在验证最新集成候选。原证据保留并标明基线，不能代替最新候选 CI。

## 1. 背景、基线与目标

Framework 提供通用组件与 DDD 分层；Template 组合这些能力生成业务项目。前轮已完成范围选测、包消费复用、Job 假时钟和质量闭环。本轮解决仍留在测试中的固定休眠及过长故障等待，保持原质量责任；另按新增授权修复 TokenCache 迟到 miss 的重复抓取，保留原生产安全与到期契约。

已审计源码基线：`a9c94b19d7177326cb849e587e47fe4cc072e40d`；当前分支 `zengqinglei/CI性能优化`。实施开始时先核对工作区和 upstream；若 develop 有新变更，按现有授权正常合并，记录新基线并重新建立受影响基准，不混用旧样本。

| 已核实对象 | 当前单次观测 | 本轮策略 |
| --- | --- | --- |
| JWKS 不可用用例 | 10.653 秒 | 仅该宿主覆盖为约 300 毫秒传输超时 |
| 一个等待者取消，共享抓取继续 | 3.296 秒 | 开始／释放信号与显式取消，不等固定 3／1 秒 |
| 慢公钥抓取有界 | 10.407 秒 | 保留原真实超时验证，不计入优化收益 |
| OAuth 缓存到期 | 1.204 秒 | 统一假时钟，验证到期前与到期时刻 |
| 前端保存状态 | 固定 450 毫秒 × 3／场景 | 假 Date 与计时器推进 |
| 租户搜索防抖 | 固定 500 毫秒／场景 | 299／300 毫秒虚拟边界 |

以上是一次本地样本与源码等待预算，不能当作稳定中位或 CI 墙钟收益。证书相关类 14 个用例、ServiceClient 相关 15 个用例实测全部通过。方法时长包含自身宿主准备；本次证书类首个宿主用例的约 8.6 秒主要是数据库／宿主冷准备，不属于有效期等待。

执行依据为 [仓库质量规范](../framework/quality-assurance.md)、[模板质量规范](../template/quality-assurance.md)、[三层交付分工](../architecture/collaboration-scenarios.md)。原评估已转入本计划；不把方案与本计划复制进 Framework 随包文档或 Template 分发目录。

原始证据与独立审查：

- `.tmp/wait-audit-20261004/audit-summary.json`、`measured-tests.json`、`wait-sites.txt`。
- `.tmp/wait-audit-20261004/framework/service-client.trx`、`resource-results/signing-rotation.trx`。
- `.tmp/wait-audit-20261004/claude-plan-review.md`、`review-decisions.json`。
- `.tmp/wait-audit-20261004/review-verification/probe/`：真实 HybridCache 与 MemoryCache 10.0.11 共用假时钟，999 毫秒命中、1000 毫秒失效，Release 警告作为错误通过。当前 ISystemClock 与 Clock 属性无 Obsolete，未添加警告豁免。该实验不是 TokenCache 原用例的完成证据。

## 2. 本轮范围与交付边界

| 任务 | 主要修改位置 | 必须交付 |
| --- | --- | --- |
| T1 证书故障短超时与生产契约 | `template/backend/tests/CompanyName.ProjectName.IntegrationTests/SigningKeyRotationTests.cs` | 不可用用例短超时、独立生产超时检查、原慢抓取用例保留 |
| T2 共享抓取取消握手 | 同一测试文件及其中私有夹具 | 零固定休眠、取消不扩散、共享结果与新公钥接受证明 |
| T3 缓存时间贯通 | `framework/tests/components/service-client/Leistd.ServiceClient.Tests/OAuth/OfficialClientTests.cs` | 同一假时钟贯通 TTL／HybridCache／MemoryCache，取消 1200 毫秒等待 |
| T4 前端假计时器 | `template/frontend/src/app/features/settings/setting-section/setting-section.spec.ts`、`template/frontend/src/app/features/platform/components/tenants/tenants.spec.ts` | 取消保存与防抖固定等待，保留边界、失败与 DOM 断言 |
| T5 验证与测量 | 本轮独立 `.tmp/test-wait-implementation-<run-id>/` | 原／新样本、职责对应、反例、生成与全量 Framework 结果 |
| T6 审查与交付 | 本计划、必要维护规范／验证报告、MR | Claude 代码审查收敛、实际候选 CI 证据、最终前后对比 |

生产 `FetchTimeout=10秒`、一分钟刷新限频、令牌／证书／缓存生产寿命、除 TokenCache 迟到未命中复查外的认证处理器、公共 API、依赖版本、测试隔离、矩阵选择与 CI workflow 不因本任务改变。测试依旧运行真实官方实现、真实 PostgreSQL、真实 Chromium，不用手写协议或缓存算法替代。

影响面分别验收：Framework 的改造首先降低框架自身测试成本，不给下游应用注入假时钟；Template 的测试夹具与 spec 改造通过新生成项目交付，新业务项目可直接受益。已经生成的业务项目不会因升级 Framework 包自动更新其测试源，需要按项目流程同步对应夹具／spec 后验证；本轮没有真实业务局部 PR，不能冒称已完成这类项目的实测。业务项目仍运行自身受影响测试，不运行 leistd-net 的生成矩阵。

本轮明确保留：现有约 200 毫秒 HTTP 超时测试；通知送达 100 毫秒有界轮询；服务／数据库就绪观察；full／L3 的真实令牌到期；可选浏览器 5 秒负面观察窗口。它们不计入提速收益。

暂缓范围登记：npm audit 确定性失败重试分类；模拟令牌端点统一 40 毫秒延时；AsyncLocal 的 20／10 毫秒、锁争用的 5 毫秒、CurrentTenant 的 1 毫秒等待。这些毫秒级竞争窗口只在另有偶发失败证据或明确等价同步设计时另立范围，不能在本轮默默扩改。audit 只影响失败反馈，新增解析有误放行风险，故按审查意见暂缓；它仍是已发现而未实施的项，不能在最终报告中称“全部等待已消除”。

## 3. 执行顺序与工作项

### T0：冻结基准与准备验证环境

1. 核对 Git HEAD、develop、工作区改动和其他会话正在改的文件；仅处理本任务范围，不覆盖他人改动，不改 CRM 认证历史计划。
2. 登记基准 SHA／源码哈希、SDK、Node、浏览器、Docker、包版本和缓存策略；原观察只作参考，补足本轮正式基准。
3. 建立基准和候选两个隔离生成目录、各自 NuGet 解包目录。Framework 包通过 PackageReference 消费；已验证包源只读复用，记录包文件哈希与 Framework 输入一致性。不能确认对应源码时仅打包一次到本轮独立 feed，避免清空其他会话的共享 feed。
4. 在改实现前保存基准源码、生成结果、构建输出与过滤器。基准项目保持不变；候选单独重新生成／构建，不能执行被后续构建覆盖的旧测试 DLL。
5. Resource 集成测试需要 Docker；前端使用仓库锁文件安装依赖及真实 Chromium。还原、安装、构建的准备时长单列，不摊入单用例收益。

完成证据：`baseline-manifest.json`、源码／包哈希、环境记录、完整命令与基准测试发现结果。

### T1：仅缩短 JWKS 不可用用例

1. 在测试 `Host` 的私有配置入口增加仅该用例使用的传输超时参数，默认不覆盖生产值。
2. 使用官方 `OpenIddictValidationSystemNetHttpOptions.HttpClientActions`，通过测试 PostConfigure 追加约 300 毫秒 `HttpClient.Timeout`，在生产动作之后执行。该设置同时约束 HTTP 请求与其中的官方重试等待。
3. 不关闭官方重试管道。由于默认首个重试延迟为 1 秒，短用例不声称验证实际重试次数；它只验证获取失败时已知公钥仍可用、未知公钥被拒绝、刷新不按请求放大。
4. 保留原请求次数、已知／未知令牌与限频断言，并为用例执行增加有界挂起保护。不要把每次正常验证也改成慢故障请求。
5. 新增默认生产组合契约用例：不传短超时覆盖，验证抓取客户端的有效 Timeout 等于独立写明的生产期望 10 秒，并核对 `FetchTimeout` 与期望一致，避免两个值同时错误而自比较通过。
6. 优先检查实际抓取客户端；官方客户端使用动态名称和 AsyncLocal，不能猜一个名称。若直接获取不可靠，使用完成生产组合后的官方 HttpClientActions 对真实 HttpClient 求值，并明确只是配置契约证据。
7. 保留 `A_slow_key_set_is_bounded_by_the_fetch_timeout` 的原 10 秒真实等待与原断言。只删传输超时和只删处理器上限都可能被另一层兜住；原 M6 同时删除两层，不代表单层分别有行为拒绝证据。

必须证明：仅删除生产传输超时注册时，新增契约检查失败；同时删除原两层超时时，原慢抓取用例失败。若未完成生产契约检查，不接受短超时改造为完成，不通过缩短生产上限规避。

### T2：共享抓取取消改为可控握手

1. 在 `RotatingIssuer` 私有夹具中提供 `FetchStarted`、`ReleaseFetch` 完成信号，使用异步续体；仅此用例开启门控。
2. `/jwks` 到达时先计数、报告开始，再等待释放；登记传输请求自身 CancellationToken 的取消标记。所有等待有 5 秒左右的保护上限，不能无限挂住测试。
3. 先用已知公钥暖配置；轮换公钥后发出 A 请求，等待抓取开始；发出 B 请求，在释放前观察 B 到达请求入口且未完成；显式取消 A 并确认它抛取消异常；再释放抓取。
4. 最终确认 B 使用新公钥返回 200、JWKS 次数只增加一次、共享抓取令牌从未受到 A 的取消。
5. IdentityModel 内部“加入等待者”没有公开信号。以上是请求入口和结束态证据，不是直接观察 SDK 内部加入；仅 B 返回成功且计数为一也不足以排除 B 晚到，须结合反例核实证明力度。
6. `finally` 必须释放门控、结束待完成请求、释放取消登记与 CTS，避免失败后影响同类其他用例或耗尽原 10 秒超时。

必须拒绝：调用方取消传到共享抓取、B 在门控关闭时使用旧公钥完成。原并发刷新／伪造 kid 限频用例保留；能构造独立抓取错误配置时补充计数反例。反例通过隔离测试输入或可编译测试边界注入，不能修改 SDK 源码、手写替代管理器，或仅篡改计数制造人为失败。无法证实的 SDK 内部分支须说明未覆盖，不能包装成已完成变异。

### T3：TokenCache 与两级本地缓存统一假时钟

1. 在原到期用例使用官方 FakeTimeProvider 固定 UTC 时刻。
2. 同一实例同时注册为 DI 的 TimeProvider、传给 TokenCache，并通过测试私有 ISystemClock 适配到 MemoryCacheOptions.Clock。
3. 原 Fetch 的 `DateTimeOffset.UtcNow.AddSeconds(11)` 改为同一假时钟的当前时刻加 11 秒；buffer 仍为 10 秒，因此有效本地 TTL 为 1 秒。
4. 保留真实 HybridCache 与 MemoryCache：首次取 token-1；未推进再取仍命中；推进 999 毫秒仍命中；再推进 1 毫秒获取 token-2；取值次数准确且 Bearer 条目的分布式读写仍为零。
5. 删除 1200 毫秒 Delay，不调用 Remove 冒充到期，不替换缓存实现、不添加生产配置开关或无依据警告豁免。

必须拒绝：去掉 buffer 或计算错误 TTL；缓存设置误允许 Bearer 条目访问分布式存储。原失效／取消和远端失败断言继续通过。

### T4：保存状态与租户防抖使用假计时器

1. 使用 `vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'] })`，保留原生 rAF、performance 和微任务。
2. 先完成测试宿主／路由初始稳定，再在被测事件发出前开启假计时器；若测试结构需要提前开启，显式推进初始化计时与检查稳定，不无条件等待被伪造的任务。
3. 用 `vi.advanceTimersByTimeAsync` 推进业务计时，正常 DOM／路由变化用现有 fixture 的稳定与变更检测机制。RxJS 防抖使用 interval，不能遗漏其伪造。
4. 保存测试以写入开始记录的时刻为起点：399 毫秒仍为保存中，400 毫秒才结束；HTTP 响应提前完成时也不能立即结束。保留本行状态、文本框可编辑、离散控件禁用后恢复、较新草稿保留、刷新失败保留输入、行内错误断言。
5. 原 `settleSave` 改为推进虚拟时间，不再睡 450 毫秒。成功提示的 2 秒消退可在同一用例继续推进并验证；这个原先未量化的时间不提前算成额外收益。
6. 搜索测试记录事件前查询次数／页码：299 毫秒不应为新关键字请求；300 毫秒出现新查询、更新 URL 并回到第一页。仍使用真实 Router 和现有服务替身。
7. 在 teardown 的 finally 中清理本用例计时任务并恢复真实计时器，保留 HttpTestingController.verify；运行两份完整 spec，确认没有计时器泄漏或后续用例挂起。
8. 修改旧注释中“必须等真实时间”等失效表述；不改 UI 生产 400／300 毫秒参数，不关闭浏览器隔离。

必须拒绝：把保存最短时长改成零、把防抖改成零。边界断言中的 399／400、299／300 是外部期望，不从被变异的生产常量计算，以免自比较假绿。

## 4. 验证层次与命令

### 编辑循环 L0

只构建与运行受影响测试类、生成项目中的两份前端 spec。模板源不能直接 dotnet test；先生成，探索修改最终回写模板并重新生成。

Framework 入口：

```powershell
dotnet test framework/tests/components/service-client/Leistd.ServiceClient.Tests/Leistd.ServiceClient.Tests.csproj -c Release --filter "FullyQualifiedName~OfficialClientTests|FullyQualifiedName~ServiceClientTimeoutTests"
```

在本轮生成 Resource 的 backend 下运行：

```powershell
dotnet test tests/Wait.Resource.IntegrationTests/Wait.Resource.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~SigningKeyRotationTests"
```

`Wait.Resource` 是本轮受控生成的名称；实际项目路径从生成 manifest 取得，不能复用旧 Matrix 程序集冒称当前结果。

在生成 Identity 项目的 frontend 下运行：

```powershell
npm test -- --watch=false --browsers=chromiumHeadless --include=src/app/features/settings/setting-section/setting-section.spec.ts --include=src/app/features/platform/components/tenants/tenants.spec.ts
```

Resource 没有 tenants spec，单跑 settings。计时器变化的边界反例使用本轮隔离生成目录，构建成功后核对指定断言确实失败。

### 阶段完成 L1

1. 一次完整 `pwsh scripts/check-all.ps1` 与 Framework 全量 `dotnet test framework/Leistd.Framework.slnx -c Release`。
2. 从最终模板源重新生成并完成 `resource`、`resource-localization`、`identity`、`identity-all-features` 四场景，覆盖 RemoteTokenAuth／LocalIdentity、非本地化／本地化与组合侧。每个入选场景保留原生成形态、后端还原／构建／运行冒烟／单元／真实 PG 集成，以及适用的 lint／构建／spec 发现／完整 Chromium 测试。
3. 沿现有规范执行一次正常 HTTP OIDC E2E，确认认证接线仍通过。本轮追加 TokenCache 生产修复后，HTTP E2E 开启 IncludeExpiryWait，补验真实到期；没有浏览器交互变化，浏览器及多租户组合继续由合入 full 承担。

矩阵入口示例：

```powershell
pwsh -Command "& ./scripts/test-template-matrix.ps1 -Scenarios @('resource','resource-localization','identity','identity-all-features')"
pwsh scripts/test-template-oidc-e2e.ps1
```

第一条默认在独立 run 中打包一次。若使用有对应证据的本轮只读 feed，则明确传 `-SkipPack -LocalFeedPath <本轮.tmp内feed>`；各 run 解包仍隔离。Framework 只改测试时不另跑本地 68 包消费全集，远端沿候选质量计划验证；若实际需要改公共 API／依赖／生产行为，先重新审视范围并补足消费验证，不能伪装成测试调整。

### L2／L3

统一代码审查收敛后提交、推送、开 MR 到 develop，核对实际候选 SHA 的质量计划、场景和回执。本轮混合 Framework／Template 输入不能手动套用局部前端或后端白名单；以计划输出为准，未知取完整 PR 档。

不为本轮另建 CI、不重复手动 full。合并须依用户已有授权及现行交付流程；计划本身不是现在执行合并的指令。实际合入 SHA 的 full 与适用发布结果完成后才能标记端到端关闭，不能拿旧 SHA 的成功作替代。

## 5. 缺陷拒绝与恢复验收

| ID | 隔离反例 | 预期拒绝位置 |
| --- | --- | --- |
| V1 | 仅删除生产 HTTP 传输 10 秒注册 | 新默认生产组合契约检查 |
| V2 | 同时删除传输与处理器两层上限 | 原慢抓取有界断言；沿用有界保护防止挂死 |
| V3 | 共享抓取令牌受到 A 的取消 | 抓取令牌标记／B 新公钥验证／请求次数断言 |
| V4 | B 不等刷新就用旧公钥完成 | 释放前状态和 B 结果断言 |
| V5 | 去掉 buffer 或改变 TTL | 999／1000 毫秒缓存边界 |
| V6 | 允许 Bearer 缓存读写分布式存储 | RejectDistributedCache 计数或拒绝异常 |
| V7 | 保存最短时长改为零 | 399 毫秒仍保存中的断言 |
| V8 | 搜索防抖改为零 | 299 毫秒无新查询的断言 |

反例必须能编译，失败来自目标断言而非编译错误、测试超时或不相关异常。每次仅注入一个缺陷；记录原／新入口对应结果与目标诊断，恢复后重建并绿。测量绝不使用变异程序集。SDK 内部独立等待者分支的观察边界单列，不能用夹具计数作弊或以 coverage 替代缺陷拒绝证据。

## 6. 前后耗时测量方法与验收标准

### 本地测量

1. 保存构建后的基准／候选两个快照；同一机器、SDK、依赖、环境和过滤器。两者第一次初始化／浏览器安装等准备时长单列。
2. 对有收益的入口登记三组样本，按 B1→A1→B2→A2→B3→A3 交错执行，每组顺序固定；不同时启动其他全量基准或 CI 任务。所有样本都记录，不挑最好值，不失败后补跑换掉坏样本。
3. 分别测 ServiceClient 目标类、证书测试类、整个 Resource IntegrationTests 项目、两份完整前端 spec。对前端记录用例／suite 时间与命令入口时间；CLI 构建或启动开销不算成纯计时器收益。
4. `--no-build` 只在两个快照都已构建后使用；记录测试程序集哈希，验证执行的是该快照。代码改动导致重建时取消旧候选计时，重新登记候选并从第一组开始，保留旧记录说明原因。
5. 报告每个样本、前三组中位、绝对差、比例、用例／断言责任、失败／跳过、最长测试链与准备时间。新增生产契约用例属于新增工作量，单列成本，不能因计数变化自动认定旧责任缺失。

### 质量完成标准

- 四项主线全部实现，原关键断言保留，V1–V8 每项有实际拒绝与恢复证据；外部 SDK 未能构造的附加反例明确边界并交独立审查，不虚报。
- 缓存到期与前端业务计时不再等待真实寿命；共享抓取取消没有固定 3 秒／1 秒定时取消；短传输覆盖只作用于指定故障用例。
- 生产默认值与包 API 不变；慢抓取 10 秒真实验证、I/O 观察、L3 到期责任仍在。
- Framework 全量、代表生成、静态检查与适用 E2E 全绿；用例数变化有逐项对应，无新增静默跳过、发现遗漏或计时器泄漏。
- Claude 代码审查无未解决必须修复项；实际提交与审查候选一致，后续修改按影响复验。

### 效率完成标准

- 证书类每个 Resource 场景预计减少约 12–13 秒；将“三组中位至少减少 8 秒”作为本地类入口的保守目标，包含新增契约成本。原约 10.4 秒慢抓取仍保留。
- Resource 整个集成项目入口三组中位应改善。若最长链转移或新增检查／环境波动导致目标未达，给出实际入口数据与原因，继续排查；不能只凭类时长宣称整个入口提速，也不能通过删责任或选样达标。
- 缓存移除 1.2 秒真实等待；前端移除每场景 1.35 秒保存等待与适用的 0.5 秒防抖等待。类／suite 实测应改善；这些是固定预算和局部收益，不能相加冒称 runner 或流水线墙钟收益。
- 完整 PR 两个 Resource 的用例累加预计改善 24–26 秒，full 三个预计 36–39 秒；前端固定等待预算分别约 10.1／17 秒。此处仅为投影，项间重叠不重复相加。
- 单次普通 MR CI 报告实际 runner、队列、关键路径与必要作业墙钟，不因单次更快／更慢得出稳定因果结论。此前 92.4% 与固定三次中位 437 秒各有原口径，本轮不复用成新成果。
- 本轮不承诺重新证明 PR 稳定 ≤510 秒；若要更新这一结论，另按已有预登记方法执行同候选重复样本，不能用一次 CI 或当前本地投影替代。

## 7. 审查、提交与收尾

1. T1–T4 一次性完成，T5 的质量与本地三组数据验证完成后，将候选 diff、证据目录、前后表和保留／暂缓项通过 Orca CLI 发送给现有 Claude 会话做代码审查。方案审查完成不能替代代码审查。
2. 吸收必须修复项，按改动范围复验；有不同决策时说明背景、原责任与替代证据。不重复运行已经通过且未受影响的入口。
3. 按 [提交规范](../framework/versioning.md) 提交本任务文件，核对真实候选快照，推送并开 MR。使用能表达最终生产修复与测试优化的 Conventional Commit 类型（如 `fix(service-client)`），不冒称破坏性 API 变更。
4. 跟踪 MR 的实际候选 CI；合入后跟踪同 SHA 的 full 以及流程实际触发的发布，失败时按真实原因修复，不能把测试代码交付当已发布。
5. 长期约束只在确有新信息时更新 `docs/framework/quality-assurance.md` 或 `docs/template/quality-assurance.md`；已有原则不重复粘贴，也不新增全量等待扫描脚本。生成项目规范只同步已成立且适用的项目事实。
6. 将有长期价值的指标增补到现有质量报告的独立本轮小节，区分单次观察、三组本地中位与远端结果；原始 TRX、反例、日志与脚本留本轮 `.tmp` 证据目录。任务真正关闭后删除本计划，Git 保留历史。

最终前后对比至少包含：目标用例、证书类、Resource 集成测试入口、ServiceClient 入口、前端两套 spec、普通 PR、合入 full。每行给出候选、样本数、执行责任、旧／新时长、差值与测量限制；未执行的入口写原因，不留空白假定成功。

## 8. 任务状态

- [x] 现状审计、当前目标用例实测、官方资料与包接缝核对。
- [x] Claude 方案审查与 M1–M3 决策吸收；缓存最小零等待实验。
- [x] 统一实施计划与范围、验证、测量、收尾标准登记。
- [x] T0 基准冻结与隔离生成准备。
- [x] T1 短超时与默认生产组合契约。
- [x] T2 共享抓取取消事件握手。
- [x] T3 TokenCache 完整假时钟接入。
- [x] T4 保存状态／搜索防抖假计时器。
- [x] T5 V1–V8、最终 24 样本、L1 验证；原失败保留，最终矩阵 687.733 秒预算超限如实记录。
- [ ] T6 Claude 代码审查、提交／MR／同候选 CI、适用合入 full 与收尾。

### 实施中新增发现与当前证据

详见 [本轮验证报告](../reports/2026-10-03-quality-scenario-selection.md#2026-10-04测试等待优化的本地验证) 与 `.tmp/test-wait-implementation-20261004/`。三组全部保留；ServiceClient B3 失败是已确定性复现的原 TokenCache 旧 miss 竞争，不能视为测试时钟缺陷或重跑消除。这一发现原先仅在隔离副本修复；用户现已明确授权纳入本轮，生产源码和永久回归已经实现，最终验证见 T3b。证书类墙钟中位 -8.018秒，Resource 整体 -5.960秒；前端用例累计 -1.880秒，但CLI入口 +5.917秒，不宣称整个前端入口提速。共享负载与隔离探针重叠、L1预算未达均已登记。

Claude已审查完整候选，四项主线无阻断项；认可隔离生产复查方案。R1根因表述已纠正：常见TryAdd成功路径同样不复查，禁写仅额外影响少见锁路径。处理器单层上限被删除的检测盲区、ServiceClient入口波动已登记；审查没有独立重跑数值，不夸大为第二次实验验证。

### T3b：已授权的 TokenCache 生产竞争修复

用户于本轮明确要求纳入官方调研并按终局方案修复。保留当前稳定依赖，使用 HybridCache 官方公开的 DisableUnderlyingData、DisableLocalCacheWrite、DisableDistributedCache 组合，在外层工厂取得所有权后只读复查 L1；不新增锁字典、缓存替代实现或生产测试开关，不改变动态 TTL、缓冲、安全及取消语义。10.10.0 稳定版的常见 TryAdd 成功路径与当前 10.9.0 相同；升级不能关闭该窗口。动态工厂选项仍是上游未交付提案，不引入 .NET 11 预览作为修复前提。

- 永久回归使用真实 HybridCache、MemoryCache 与可控同步读门：第二个调用已读取 miss 后暂停，第一轮完成并写入缓存后释放第二个调用。无固定休眠；旧生产源码应编译成功并因 token-2／二次抓取断言失败，修复后通过。
- 最终候选重新构建 Framework 并全测，重新打包到独立 feed，验证全部包内容与消费构建。重新生成四场景并执行原矩阵与 HTTP E2E。旧候选失败及 24 个计时样本完整保留，不当作修复后包的验证。
- 补测最终 ServiceClient、证书类、Resource 集成入口与前端 spec 三组配对数据；首轮结果标记为测试等待改造候选，最终流水线耗时单列，不把共享机器噪声或失败样本作为稳定提速。
- 更新随包缓存契约与原报告，最终完整代码和证据再次交 Claude；审查收敛后执行提交／MR／真实 CI 闭环。

官方依据：[HybridCache 文档](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0)、[10.10.0 在途合并源码](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.Caching.Hybrid/Internal/DefaultHybridCache.Stampede.cs)、[动态选项提案](https://github.com/dotnet/extensions/issues/7669)。

T3b 本地验收完成：Framework 1,677／30 静态闸门／68 包消费／四场景后端 1,151 与前端 1,519／真实到期 HTTP E2E 10 项全部通过；最终 24 个配对样本全绿。旧 miss 删除复查反例、V5/V6 与恢复均有编译及目标断言证据。初轮 SIGTERM／数据库退出日志不覆盖，清理本轮资源后同源独立重跑通过。证据 `.tmp/test-wait-token-cache-final-20261004/`；完整数值和预算限制已追加现有报告。

T6 的代码审查子项已完成：独立解析关键 TRX／变异目标／24 样本／矩阵与真实到期回执，确认零阻断项。非阻断建议已吸收：明确工作树候选与最终 SHA 的区别、补 SDK 升级回归注释、避免历史未关闭表述误读；一致性边界登记但不冒称已复现。提交／MR／同 SHA CI 与适用 full／发布仍待交付步骤。

### develop #52 集成后的补充验收

MR 创建时 develop 已推进至 07ac3cb6；本分支正常合并，未覆盖上游修改。框架包变为 69 个；模板新增能力裁剪、认证／操作记录接线与场景。新增 IStartupFilter／IApplicationBuilder 的依赖须在全部 RemoteTokenAuth 形态可用，因此将 Builder／Hosting using 从 ResourceBrowserSession 内移至外层 RemoteTokenAuth 范围，适配有／无浏览器会话两侧。

- 重新构建 Framework 全测、打包到 `.tmp/test-wait-token-cache-develop-20261004/local-feed`，69 包消费验证、全部静态闸门。
- 完整六场景：原 resource／resource-localization／identity／identity-all-features，补 resource-capabilities-03／04，验证纯 API 关闭浏览器会话及实时组合侧。真实到期 HTTP OIDC 验证最新认证接线。
- a9 基线的性能数据保留，注明与新基线的责任／包／依赖不同，不宣称代表最新 develop。补测最新基线的证书类／完整 Resource 及两份 FE spec；官方缓存生产代码未被上游修改，确定性反例仍按当前源码核对。
- 合并后的 using 调整、实际 MR 差异与最新证据再次交同一 Claude 审查，确认实际候选 CI，保留被后续推送取消的前轮 run。

最新集成本地验收已全部通过：Framework 1,700、69 包消费、六场景后端 1,342／前端 1,521、真实到期 HTTP E2E 11 项；最新基线 24 个计时样本全绿。集成复审无必须项。代码提交 34867fd2 的 PR CI run 37205270433 成功，11 作业及 18 场景回执已核对，GitHub 合并预览树与该提交一致；墙钟 1,203 秒、作业累加 3,169 秒，510 秒目标未达，已登记上游扩容后的场景链瓶颈。当前补齐报告／计划交付记录，最终文档提交 CI 仍需跟踪；尚未合并 MR，适用 full／发布未发生，不删除本计划或宣称整体远端闭环完成。
