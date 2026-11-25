# 模板用例按精简口径审查、测试结果可信与关键路径下沉

状态：进行中 · 风险档：中（只改模板测试与验证脚本；C 涉及认证、授权等关键路径的覆盖调整） · 关联：待定

## 1. 需求

### 问题与背景

`2026-11-25-test-strategy.md`（PR #321）把「精简口径」写进了模板测试规范 §4，但没有用它审查模板自带的用例。下游业务项目（由模板生成）已经按同一口径在自己的仓库里精简过：
- E2E 档从 69 条降到 35 条；
- 前端改为真实交互，多语言、多视口只留代表样本；
- 集成测试的规则表下沉，退役守卫删除，同类测试复用宿主。

逐项对照下游三个仓库的提交后，确认模板里有同类候选。另外发现两处测试结果不可信：
- **零执行假绿：** `verify.ps1` 只看测试进程的退出码。测试项目一条用例都没发现，或者没有生成报告，也会显示通过。下游的质量脚本已经拦住这种情况。
- **区域性相关的断言：** `OperationRecordLoggingTests` 提交分支的时间断言，改动前后在本机都失败，CI 上通过（见 `2026-11-25-test-strategy.md` §4）。断言用运行区域性解析时间，结果取决于机器环境。

### 取舍依据

- `docs/architecture/design-principles.md` §5；模板测试规范 §1～§4，特别是 §4 的五类精简口径、「关键路径只下沉、不删除，先补下层」，以及「PR/MR 逐条记录替代覆盖或删除理由」。
- 模板按派生项目长大后的终局定规范。精简的目的是让每条用例都在能证明它的最低一层，不是追求删减的数量。
- **不过度设计：** 不新增通用等待器、重试、第二份覆盖清单或下层用例名索引。零执行检查优先用官方选项，官方选项不够时，只核对该步骤应有的报告文件和用例数。
- **下游数据先核对再引用：** 下游已迁移完成的事实，只适用于下游自己的部署。模板仍在交付的迁移和历史形态，照常保留测试。

### 目标与非目标

- **目标：**
  - 模板自带用例按 §4 审查一遍，测实现细节的改为真实交互，重复的只留代表，可下沉的先补下层再缩上层；
  - `verify` 拒绝零执行和缺失报告；
  - 修好依赖区域性的时间断言；
  - 选一个测试类做宿主复用试点。
- **非目标：**
  - 不拆 CI 作业；
  - 不删 `LoggingSettingsMigrationTests` 和关闭历史形态的契约用例；
  - 不改框架；
  - 不为了数量删减关键路径；
  - 不改 E2E 的准入类别和上限，除非用例数变化需要同步上限。

### 功能要求与验收

**通用验收：**
- 每处删除或下沉，在 PR 描述里逐条写明类别、替代覆盖（指出具体的测试），已删功能或只测第三方的写删除理由。
- 规格 §4 记录前后的用例数和受影响测试项目的耗时：前端 spec 数和用例数、后端单元测试和集成测试数、E2E 数。

#### A 前端同层精简（低风险）

- **A1 测实现细节的改为真实交互，用例数不变。** 共 6 处：
  - 5 处进入组件或服务内部：
    - `core/services/startup-service.spec.ts:46` spy 了私有的 `isProtectedRoute`：用 `openAt` 选实际地址；
    - `external-auth-callback.spec.ts:348` 直接调用 `onReauthVisibleChange(false)`：改为由重认证对话框的取消或关闭事件驱动；
    - `two-factor-required.spec.ts:163` 直接调用 `continue()`：改为点击真实的继续入口；
    - `two-factor-setup.spec.ts:173` spy 了原型上私有的 QR 渲染：改为在 QR 外部依赖处控制；
    - `webhook-subscription-dialog.spec.ts:83` 读内部 `formModel()` 再调 setter：改为通过可见的输入驱动。
  - 1 处伪造 DOM Event 直接调用处理器：`user-edit-dialog.spec.ts:430`，改为派发真实 `change` 事件，或走浏览器文件上传。
  - 其余 9 处 `as unknown as` 是 DTO、外部边界或测试替身的类型转换，保留。
  - 验收：原有断言和行为都保留；改写后，在被测行为被破坏时这些用例仍会失败。
- **A2 重复覆盖。**
  - 三个表格（开放应用、角色、用户）的 `puts aria-sort only on the header of the sorted column` 共用同一份排序计算，但各自有独立的 `aria-sort` 模板绑定。因此各表的接线证明都保留，可以并入该表已有的排序交互用例，不另立用例。
  - `user-edit-dialog.spec.ts:232` 的开关边缘点按与 `coarse-pointer.spec.ts` 重复。确认对话框没有独立的裁切或监听逻辑后移除，启停状态和保存结果的断言保留。
- **A3 语言样本（`app-date-pipe.spec.ts`）。** 只缩减得不出新结论的样本；非拉丁文字、不同字段顺序、年份和闰日边界必须保留。
  - `:89–96`：中文和一个非中文代表各留一个。
  - `:98–106`：保留中文和一个地区格式。
  - `:128–137`：先确认哪些格式重复，再按格式代表压缩。
  - `:40`：确认有单独证据证明地区标签能正确归一后，才缩成一个中文地区。
- **A4 只测第三方的宽断言。** `locale-week.spec.ts:5`、`calendar-locale.spec.ts:35` 的宽断言，改为只验证模板负责的部分：回退规则，以及 ISO 到日历索引的转换。不再用 `Intl` 的完整语言表做断言。
- **A5 时序等待。** `external-auth-callback.spec.ts` 里多次等待下一轮 `setTimeout`，改为等可观察的结果，或现有的可控信号；不新增通用等待器。

#### B 结果可信（中风险）

- **B1 `OperationRecordLoggingTests` 的时间断言。**
  - 历史记录只能证明 `TryParse` 失败。先在失败的那一轮里，采集结构化字段的实际值和当时生效的区域性，确定原因，再定修复预期。
  - 消息模板里的 `{OperationTime:O}` 描述的是消息格式；发射器传出的值是 `record.CreationTime`；夹具读的是另一条结构化接口。修复时按实际接口和宿主固定时钟核对，不用运行区域性解析。
  - 提交和回滚两个分支都保留。
  - 这个文件有两个编译分支。复现用 `resource-capabilities-03`，同时覆盖关闭历史的本地身份形态（如 `identity-capabilities-05`）。
  - 验收：修复前，能在复现条件下看到失败；修复后，复现条件和默认条件下都通过。
- **B2 `verify` 拒绝零执行和缺失报告。** 已选中的测试步骤成功后，要确认本轮确实执行过用例：
  - 该步骤应有的 JUnit 报告存在；
  - 报告里至少有一条用例没有被跳过。
  - 不满足时，以非零退出码失败，提示指明是哪个步骤、缺报告还是零用例。
  - 有官方选项能拦住零发现的，优先用官方选项。
  - 原有的失败退出码、报告清理和耗时汇总保持不变。
  - 只在 `verify` 里，在每个选中测试步骤成功退出后检查。不在 CI 包装器或汇总里重复加闸，不做完整报告模式，不做第二份清单，不做通用重试。
  - 验收：在生成目录里分别构造「零用例」「全部跳过」「漏报告」三种情况，修复前假绿、修复后失败；仓库的生成检查加上这些反例的断言。
- **B3 工程约定（只写维护文档）。** `docs/template/development-guide.md` 写明：新增跨端的实际输入（某一端的步骤读取另一端的文件）时，同步更新 `verify.ps1` 的路径选择，以及对应的 `-List` 场景断言。

#### C 关键路径下沉与宿主复用（中风险，先补下层）

- **C1 E2E 权限用例。** 涉及 `permissions.e2e.ts:9,19`（查看、创建按钮的权限），以及 `login.e2e.ts:18` 对管理员按钮的重复断言。
  - 先在真实宿主的集成测试里固定对应端点按角色的允许和拒绝，以及前端组件按权限显示菜单或按钮的状态，不能借用另一个端点的 403 代替。
  - 下层齐备后，E2E 只保留真实登录、Cookie 会话和到达受保护页面。
  - 替代证据按实际的角色列表、创建端点和前端入口逐项核对。
  - 受限浏览器会话的现有差异，在等价覆盖成立之前保留。
- **C2 `webhooks.e2e.ts:37` 长用例。**
  - 保留一条接线：真实接收端收到签名头和事件，回到本系统后看到成功状态。
  - `WebhookManagementEndpointTests` 只验证读写权限，不能替代正向的密钥、筛选、载荷等子断言。逐项从现有的审计用例（如 `WebhookSubscriptionAuditTests`）、投递用例和前端 spec 里核对。已有等价覆盖的才从 E2E 移除；缺的那项，要么保留该断言，要么先补一个最小的等价用例。
- **C3 登录锁定。**
  - `LoginLockoutTests.cs:17,63` 和领域层 `UserAccessStatusTests.cs:86,114` 重复的阈值分支，下沉到领域层。HTTP 层保留一条锁定拒绝、清除后允许，以及按配置生效的证明。
  - `:84` 实际写的是 `LockoutDurationMinutes=0`。下沉时要覆盖时长规则；如果这条用例同时独立证明了 HTTP 错误映射，这部分边界保留。
  - 会话维持、后台解锁是不同的产品行为，保留。
- **C4 退役 OAuth scope。** `AuthenticationModeTests.cs:36–57` 现在验证两件事：开放应用 API 返回的默认目录里没有这 5 个退役名称；实际的 scope 表里也没有。
  - 默认目录是否包含这些名称，用模板目录层的单元测试作为默认目录契约证明。依据见 `docs/specs/2026-11-18-identity-machine-scope-consolidation.md` 的 AC4。
  - 存量数据库的清理（初始化器删除目录之外的旧记录），只有预置旧记录、再走真实初始化才能证明。如果用代表样本替换逐名的数据库断言，要验证一条预置的目录外记录经初始化被清除。
  - 服务级 scope 的 API 和落库接线保留。
  - 不新增生产禁名清单，不逐条发令牌，不做多层校验。现有用例没有发令牌，所以这一项不新增协议拒绝用例。
- **C5 设置并发（审查后保留，不改）。** `SettingConcurrencyTests.cs:65` 让另一条连接先提交，验证本次 `READ COMMITTED` 事务撞上唯一键后仍能恢复，并提交同批实体。框架的 SQLite 测试共用一条连接和同一个事务，模板的 HTTP 并发请求又经写入端串行化，都触发不到这条冲突路径。两条用例都保留，§4 记为「审查后保留」。
- **C6 E2E 宽度变体。** `workspaces.e2e.ts` 里 390 和 1440 两种宽度下的服务跳转，都用 `page.route` 替代系统目录和目标站。
  - 要保留的契约是：真实浏览器的跨源导航，以及落到正确的工作区路径；目标服务由测试桩响应。保留一条即可，不为此新增跨服务部署。
  - 缩掉宽度变体之前，确认组件测试（`system-switcher.spec.ts`）已覆盖移动端的行为：在抽屉里打开切换器、按 Escape 后抽屉仍保留。缺的话先补。
  - 平台与工作区两个冷启动入口都保留。
- **C7 宿主复用试点。**
  - 选一个配置相同、逐场景新建宿主的测试类试点，优先 `ResourceBrowserSessionTests`。`Host` 持有具体的 `Issuer`，后者带签名键、账户和续期状态。按实际启动的宿主数和克隆数统计，只复用配置相同、状态能简单隔离的一组。收益不足就只记录结果，不改。
  - 改用类夹具、唯一数据和可重置的替身；需要独立假时钟、Cookie 或签发配置的用例保持隔离。
  - 验收：该类前后耗时和宿主创建次数都写进 §4。

## 2. 方案

- **提交与范围：** A、B、C 分开提交，同一个 PR。
- **C 的顺序：** 每一处都先提交下层补齐，再提交上层缩减。审查时能逐条看到「替代覆盖已存在」。
- **E2E 上限：** 不随每次删减机械调整。是否收紧，看最大交付形态实际发现的用例数和实际预算。所有用例的 `@proves` 标签保持有效。
- **版本：** 只改模板测试和脚本，合入 develop 后不出框架 beta，模板正式版由 main 发布。合入后记录实际的 Release 工作流运行，以及模板验证包版本（`<VERSION>-ci.<run_id>.<attempt>`，如有）。
- **回滚：** 按提交撤回即可，不涉及运行时代码和数据。

## 3. 计划

| 步骤 | 内容 | 验证 |
| --- | --- | --- |
| 1 | B1（先复现失败）、B2（先构造假绿）、B3 | 受影响场景的集成测试；生成检查和反例；`-List` 与失败传播的断言 |
| 2 | A1～A5 | 受影响 spec，以及模板前端单测全量；改写后对被测行为做一次变异，确认仍能失败 |
| 3 | C1～C4、C6：先补下层，再缩上层（C5 审查后保留） | 受影响的集成测试项目全量，前端单测，E2E 全量与准入闸门 |
| 4 | C7 宿主复用试点 | 该类前后耗时、宿主创建次数 |
| 5 | 端到端闭环 | 全功能（有 E2E）和 Resource 两类场景各跑一次完整 `verify.ps1`；`scripts/check-all.ps1`；`template-package/test-template-package.ps1` |

## 4. 验证

### 实施前基线

从 `5e699f41` 在本机（`local`，macOS arm64，SDK 10.0.402）生成工作区并执行默认 `pwsh scripts/verify.ps1`。用例数来自本轮 JUnit，前端 spec 数同时与 Vitest 的发现结果核对。耗时为 `verify.ps1` 各步骤的墙钟秒数。

| 场景 | 前端 spec / 用例 | 后端单元 | 后端集成 | E2E | 后端单元 / 集成（s） | 前端单元 / E2E（s） | 完整入口（s） |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `identity-workspace` | 192 / 2239 | 371 | 872 | 16 | 17.79 / 362.70 | 159.86 / 53.22 | 802.07 |
| `resource-capabilities-03` | — | 201 | 192 | — | 4.85 / 231.40 | — | 287.33 |

Identity 从干净生成的工作区起步，`-Sync` 已还原和安装依赖，进入 `verify.ps1` 时尚未编译，全部通过。Resource 在同一个工作区先跑 B1 定向采集，再以 `-Sync -Discard` 恢复源码，保留了编译缓存，完整入口仅 B1 的提交分支失败。两轮没有清空节点级下载和镜像缓存。

### 实施后完整验收

从 `89e24c2a` 在本机（`local`，macOS arm64，SDK 10.0.402）分别生成并执行默认 `pwsh scripts/verify.ps1`，三种场景全部通过。Identity 在同一工作区先跑了 C3 正反例，有后端构建产物。两个 Resource 工作区在 `-Sync` 安装依赖后首次构建。它们使用节点上已有的下载、浏览器与镜像缓存。此处耗时是各轮的验收记录，性能对照使用另起的同口径运行。

| 场景 | 前端 spec / 用例 | 后端单元 | 后端集成 | E2E | 后端单元 / 集成（s） | 前端单元 / E2E（s） | 完整入口（s） |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `identity-workspace` | 192 / 2239 | 372 | 872 | 14 | 3.33 / 228.18 | 73.35 / 37.82 | 455.18 |
| `resource-capabilities-03` | — | 201 | 192 | — | 2.49 / 68.64 | — | 98.39 |
| `resource-workspace` | 142 / 1435 | 206 | 278 | — | 2.92 / 91.60 | 46.41 / — | 225.01 |

Identity 前端仍为 192 个 spec 和 2239 条用例（A2 删 1 条，C1 补 1 条）。后端单元从 371 到 372，集成保持 872（补 2 条，下沉 2 条），E2E 从 16 到 14，准入为 14/20，上限仍为 20。纯 API Resource 仍为 201 单元及 192 集成，原有的 1 条失败已修复。

### Identity 同节点、同构建缓存起点复测

在单个自动调度任务内，本机（`local`，macOS arm64，SDK 10.0.402）对两份新生成的 `identity-workspace` 依次运行默认 `pwsh scripts/verify.ps1`。两份各自用 `-Sync` 还原及安装依赖，计时开始前均无后端编译产物、Angular 构建缓存或测试报告。候选依赖源相同，共用节点级 npm 下载、Playwright 浏览器和 Docker 镜像缓存。工作区准备不计入入口耗时。

| 提交 | 前端 spec / 用例 | 后端单元 | 后端集成 | E2E | 后端单元 / 集成（s） | 前端单元 / E2E（s） | 完整入口（s） |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `5e699f41` | 192 / 2239 | 371 | 872 | 16 | 3.34 / 200.59 | 73.11 / 46.19 | 467.78 |
| `89e24c2a` | 192 / 2239 | 372 | 872 | 14 | 3.38 / 185.93 | 74.89 / 32.16 | 404.49 |

两轮均通过全部 17 步。本次同口径运行观测到完整入口少 63.29 秒，E2E 从 46.19 秒到 32.16 秒。整体对照各跑一轮，各阶段耗时仍受运行间波动影响。原基线 802.07 秒及验收 455.18 秒保留为两次原始记录，仅对照本表。

### B1 和 B2 的反例

- 生成的 `resource-capabilities-03` 在修复前运行 `dotnet test -c Release --filter FullyQualifiedName~OperationRecordLoggingTests`，两条通过，提交分支失败。失败时结构化值为 `11/25/2026 10:56:57`，有效区域性和 UI 为 `zh-SG`，使用公历。FakeLogger 按固定区域性转换原始结构化值，旧断言把它交给当前区域性解析。修复后用宿主固定时钟核对结构化值，保留真实事务的提交和回滚两支。
- 在生成的 Resource 项目用命令替身构造零用例、全部跳过、缺报告三种情况，修复前 `pwsh scripts/verify.ps1 -Fast` 三次均返回 0，修复后均返回 1，并指明出问题的测试步骤及原因。
- 在生成的 Identity 和纯 API Resource 项目执行 `check_verify_script`，核对 `-List`、改动选步、原失败码传播、默认及快速模式，以及上述三个反例。
- 修复后运行两种禁用操作历史形态的完整集成测试，`resource-capabilities-03` 192/192、`identity-capabilities-05` 648/648 通过。在两个形态的生成目录中将有效区域性固定并核对为 `zh-SG`，各自的 `OperationRecordLoggingTests` 均为 3/3 通过。
- 对真实 VSTest 传入无匹配结果的 `--filter FullyQualifiedName=NoTestMatchesThisName`，默认退出 0，加 `-- RunConfiguration.TreatNoTestsAsError=true` 后退出 1。

### 删除与下沉的替代覆盖

| 项 | 删除或下沉 | 替代覆盖与保留边界 |
| --- | --- | --- |
| A2 | 用户编辑对话框的开关边缘点按 | `coarse-pointer.spec.ts` 的 `toggles each control when tapped outside its visual box` 覆盖共享触屏命中区。`shows the role field and submits the picked roles only when roles may be assigned` 点击对话框内的开关，断言 `aria-checked` 与保存载荷。三个表各保留其 `aria-sort` 绑定断言。 |
| A3 | 缩减日期格式的重复语言样本 | 槽位保留中英文代表，往返保留 ISO、月在前、日在前及日语年月日格式，同一用例继续覆盖年初、年末和闰日。地区标签仍由中文 `zh-CN`、`zh-TW` 的既有断言覆盖。 |
| C1 | 删除管理员单独打开角色列表的用例，下沉管理员和受限会话的创建按钮断言 | `A_role_reader_can_list_roles_but_cannot_create_them` 验证真实宿主允许受限角色读取、拒绝其创建且允许管理员创建。`offers role creation only to a user with the create permission` 固定组件接线。`signs in through the login page` 和 `opens the protected role list with a restricted session` 保留真实 Cookie 会话访问受保护列表。 |
| C2 | 删除全事件选择后独立的选中状态断言 | `WebhookSubscriptionDialog` 的 `submits ["*"] for all events and drops the individual choices` 保证请求载荷。E2E 仍由浏览器点击全事件并提交，保留真实接收端、签名头、成功状态、密钥显示、筛选和投递详情接线。 |
| C3 | 登录失败阈值与零阈值的重复 HTTP 用例 | `Reaching_the_threshold_locks_temporarily_and_resets_the_count` 和 `A_zero_threshold_never_locks` 固定领域规则。`Admin_unlock_allows_sign_in_after_the_configured_threshold_locks_the_user` 使用租户阈值 2，在解锁前用正确口令登录并断言 HTTP 401、`Auth:UserTemporarilyLockedOut`，随后验证管理员解锁与恢复登录。会话维持和 `Rejects_a_zero_lockout_duration` 的 HTTP 400 映射继续覆盖。 |
| C4 | 下沉五个退役名称的否定断言 | `The_default_catalog_excludes_retired_machine_scopes` 固定默认目录。`Initialization_removes_a_stored_scope_outside_the_catalog` 预置实际旧记录并验证清理。`Identity_registers_and_publishes_its_service_scope` 保留服务级 scope 的 API 与落库接线。 |
| C5 | 审查后保留 | `Concurrent_first_writes_of_one_user_setting_both_succeed_and_leave_one_row` 覆盖 HTTP 并发写。`Interleaved_first_write_inside_an_outer_transaction_recovers_and_commits_host_entities` 覆盖 PostgreSQL 跨连接提交和保存点恢复。 |
| C6 | 删除 390 px 跨源导航变体 | `SystemSwitcher` 的 `closes only the menu on Escape inside a real drawer` 覆盖真实抽屉内打开菜单、按 Escape 并保留抽屉。`navigates to another origin with the current workspace path` 保留浏览器跨源导航及目标工作区路径。两个冷入口继续检查。 |

### C7 同机对照

本机（`local`）分别构建改动前后的 `resource-workspace` Release 集成测试项目，再按前、后交错次序运行 `dotnet test -c Release --no-build --no-restore --filter FullyQualifiedName~ResourceBrowserSessionTests`，每轮均为 39/39 通过。每轮使用已编译的各自工作区和同一节点镜像缓存，计时包括测试进程启动。宿主与数据库克隆数另在 `worker-01`（改动前）和 `local`（改动后）采集，均用各自先编译好的工作区运行全类。

| 构建 | 第 1 轮（s） | 第 2 轮（s） | 第 3 轮（s） | 中位（s） | 组内极差（s） | 宿主 / 数据库克隆 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 改动前 | 35.403 | 33.903 | 32.088 | 33.903 | 3.316 | 39 / 39 |
| 改动后 | 33.433 | 32.180 | 31.825 | 32.180 | 1.607 | 37 / 37 |

中位少 1.723 秒，未超过两组中较大的极差 3.316 秒。宿主 39 到 37，耗时无可测收益，不保留。

### 前端改单条用例耗时

两轮均在本机（`local`）。数据为 Identity Workspace Vitest JUnit 用例墙钟。前为 `5e699f41` 的完整入口，后为 A 的全量前端复测（含 C1 的组件覆盖）。运行 Vitest 时前端依赖已安装，两份前端生成目录均未运行过 Angular 测试或构建。表内改写用例均低于 100 ms。

| 单条用例 | 改写前（ms） | 改写后（ms） |
| --- | ---: | ---: |
| `StartupService > treats 401 as signed-out and still reaches success` | 6.0 | 2.3 |
| `StartupService > leaves Workspace:Forbidden on a deep link to the workspace guard instead of failing startup` | 2.4 | 1.7 |
| `StartupService > leaves Workspace:Unavailable on a deep link to the workspace guard instead of failing startup` | 6.9 | 0.8 |
| `StartupService > marks startup as failed when the auth service is unavailable (503)` | 3.0 | 0.6 |
| `StartupService > marks startup as failed on network errors (status 0)` | 2.1 | 0.5 |
| `StartupService > reaches success when the session probe resolves` | 1.9 | 0.4 |
| `StartupService > settled > waits while loading and lets the guard decide after success` | 2.4 | 0.6 |
| `StartupService > settled > holds the target url and rejects the navigation when startup failed` | 4.1 | 0.5 |
| `StartupService > retry > lands on the held target once startup succeeds, without reading the address bar` | 6.9 | 2.1 |
| `StartupService > retry > keeps the held target while startup still fails` | 4.7 | 0.7 |
| `StartupService > entry route gate > establishes the subject on a protected route` | 1.9 | 0.3 |
| `StartupService > entry route gate > does not probe the session on a public route` | 1.8 | 0.4 |
| `StartupService > lets the external sign-in callback run without probing the session` | 2.4 | 0.3 |
| `calendarLocale > maps the ISO Sunday to index zero in the calendar` | 52.3 | 0.8 |
| `calendarLocale > keeps working for an unknown locale` | 0.9 | 0.5 |
| `firstDayOfWeek > falls back to Monday when the runtime does not provide week information` | 65.9 | 0.4 |
| `ExternalAuthCallback > establishes the session before navigating` | 21.0 | 7.0 |
| `ExternalAuthCallback > goes to the second step page with the protected return address` | 11.6 | 3.6 |
| `ExternalAuthCallback > offers a usable exit when loadUser fails` | 9.1 | 53.9 |
| `ExternalAuthCallback > offers a usable exit when establish fails` | 4.6 | 56.4 |
| `ExternalAuthCallback > offers a usable exit when navigate fails` | 9.5 | 53.9 |
| `ExternalAuthCallback > uses the protected return address after establishing a session` | 9.2 | 2.8 |
| `ExternalAuthCallback > takes a restricted session to the setup page together with the protected return address` | 9.9 | 2.0 |
| `ExternalAuthCallback > continues an authorization request with a full page load` | 10.2 | 1.6 |
| `ExternalAuthCallback > shows the server reason when the sign-in is rejected` | 15.1 | 52.1 |
| `ExternalAuthCallback > shows a generic message when the server fails` | 10.0 | 55.1 |
| `ExternalAuthCallback > shows the cancellation without completing and offers a way back to sign in` | 10.7 | 54.3 |
| `ExternalAuthCallback > returns a cancelled link to the security settings without completing it` | 10.6 | 2.9 |
| `ExternalAuthCallback > asks to confirm identity and retries the link with the code` | 45.8 | 58.2 |
| `ExternalAuthCallback > signs in again back to the security settings, not to the callback` | 21.9 | 57.9 |
| `ExternalAuthCallback > returns to the security settings when the password prompt is dismissed` | 23.1 | 55.8 |
| `ExternalAuthCallback > enters the application when a replayed callback fails but the session is still valid` | 11.1 | 55.4 |
| `TwoFactorRequired > restores the setup page when loadUser fails` | 21.3 | 9.2 |
| `TwoFactorRequired > restores the setup page when establish fails` | 17.7 | 17.9 |
| `TwoFactorRequired > restores the setup page when navigate fails` | 18.7 | 59.9 |
| `TwoFactorSetup > drops a QR code that finishes after its setup expired` | 11.4 | 56.3 |
| `UserEditDialog > toggles the active switch from the edge of its touch target on coarse pointers` | 150.3 | 已移除 |
| `UserEditDialog > shows the role field and submits the picked roles only when roles may be assigned` | 34.1 | 32.2 |
| `UserEditDialog > rejects a non-image avatar with a message and keeps the current avatar` | 27.9 | 14.9 |
| `WebhookSubscriptionDialog > submits the custom headers as a map and ignores blank rows` | 30.2 | 16.2 |
| `AppDate > slot decides the precision > monthDay shows only the month and day` | 2.7 | 0.3 |
| `AppDate > slot decides the precision > precision depends only on the slot and is the same across locales` | 2.6 | 0.4 |
| `parseAppCalendarDate > parses back representative date formats across calendar boundaries` | 355.0 | 68.7 |
| `Roles page query round trip > offers role creation only to a user with the create permission` | 新增 | 25.5 |

### 覆盖变异

- 在生成项目执行 `npm test -- --watch=false --include …`，依次破坏受保护路由判断、重认证关闭、恢复码继续、过期 QR 丢弃、头像变更、请求头录入、开关保存、日历索引、周起始回退、日期精度与往返，以及角色按钮条件。各组预期用例均失败。禁用外部登录回调处理后，该 spec 的 16 条均失败。
- 在生成的 Identity Workspace 执行 `dotnet test -c Release --filter …`，放宽角色创建策略、停用存量清理、把退役名称加入默认目录、把零阈值当成有效锁定阈值。角色创建、存量清理、默认目录和 `A_zero_threshold_never_locks` 均按预期失败。
- 对 `89e24c2a` 执行 `dotnet test -c Release --filter FullyQualifiedName~Admin_unlock_allows_sign_in_after_the_configured_threshold_locks_the_user`，正例通过。在生成目录临时跳过登录路径上的两个临时锁定判定后，已锁定用户的正确口令请求返回 HTTP 200，用例在预期 HTTP 401 处失败。另一次变异固定读取默认阈值，第二次错误口令返回 `Auth:InvalidCredentials`，用例在预期 `Auth:UserTemporarilyLockedOut` 处失败。

### 静态闸门与模板验证包

对验收代码 `89e24c2a` 运行以下命令。

- `pwsh scripts/check-all.ps1`。52 道全部通过，包括模板条件分支、公开内容和规范读取成本。
- `pwsh template-package/test-template-package.ps1`。模板验证包 `0.0.1-local.20261125125512` 通过包内容比对、隔离安装、两个场景的来源标记及反例和重建零差异。
- `python3 scripts/test-template-generation.py --shard control --workers 2 --output .tmp/test-slimming/generation-control`。6144 种原始输入映射、18 个实际等价比较、4 个 CI 变体、10 种 `verify.ps1` 签名全部通过。
- 本次没有框架或随框架包文档变更，未运行 `framework/build/test-package-consumption.ps1`。
