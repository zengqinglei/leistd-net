# 组件纵向内聚与后台作业组件

> 已选方案的任务分解。**全部任务完成后删除本文**，长期结论上收到 `docs/architecture/` 与各组件文档。

## 一、已定决策

| 项 | 结论 | 依据 |
| --- | --- | --- |
| 组件边界 | 带持久化的组件拥有完整纵向切片：契约与用例（Core）、存储与数据维护（EntityFrameworkCore）、HTTP 端点（AspNetCore）。宿主只做组合、覆写与业务词汇 | 微软 HealthChecks（Abstractions / 实现 / `MapHealthChecks`）与 Identity（`Extensions.Identity.Core` / `MapIdentityApi`）的分包 |
| 端点形态 | Minimal API 的 `Map*` 扩展；前缀由宿主 `MapGroup` 决定；**授权策略名必填**（选项里给，缺失启动期失败），组件不内置默认策略；端点名带组件前缀；不用 .NET 10 已弃用的 `WithOpenApi` | `MapIdentityApi` 源码与文档；Hangfire 面板"默认策略叠在宿主策略之上"的坑；ApplicationParts 只能事后排除、易 404 |
| 定制分级 | ① 用 `Map*` 加宿主前缀、策略、过滤器 ② 不调 `Map*`，在 Core 用例服务上自写端点 ③ 经 DI 替换服务（`TryAdd`） | — |
| 业务接缝 | 组件只在确实依赖宿主模型处开**窄钩子**（主体目录、租户开通、设置值校验、收件人解析），不引用宿主实体 | 现有"组件不依赖业务模型"原则 |
| 后台作业 | 新建 `Leistd.BackgroundJobs` 家族：周期任务 + 非持久进程内队列；持久化可重试作业只留契约，由提供器包对接成熟调度器，不自建存储 | ABP 把 Workers 与 Jobs 分开；ABP 默认作业存储为内存、持久化需另装模块 |
| 并发控制 | 周期任务注册时**必填** `RecurringJobScope`：`Cluster`（全集群一份）或 `EveryInstance`（每副本一份）。`Cluster` = 分布式锁零等待 + 持久化"已完成时段"水位。**不做**通用的 AOP 锁特性 | Quartz `[DisallowConcurrentExecution]` 由调度器按作业定义解释；Hangfire 自认其锁特性可能静默丢锁；ABP 各周期任务一律显式 `TryAcquireAsync`；Kleppmann：锁只保效率，正确性靠幂等 |
| 保留期默认值 | 审计类（操作记录）默认不删、启用时保留天数必填；运营类（通知）默认开启 | OWASP：保留期受法律合同约束，类库无从知道；Duende / OpenIddict 同类清理的默认值 |
| 保留期配置 | 组件选项 + `ValidateOnStart`；宿主级设置经配置源覆盖、`IOptionsMonitor` 每轮取值 | 微软 options-library-authors；本仓既有做法 |

## 二、前置：跨组件的基础契约

1. **分页契约下沉**：`PagedResultDto` 只在 `ddd-struct`，组件不能依赖它。在 `Leistd.Core` 定义分页请求与结果，`ddd-struct` 的 DTO 改为复用它（同一事实只一处）。
2. **响应包装支持端点组**：`Leistd.Response.AspNetCore` 目前只有 MVC 过滤器，组件的 Minimal API 端点不会被包装。补一个端点过滤器与 `WithResultWrapper()` 约定，宿主按需挂在路由组上。模板不使用包装，行为不变。
3. **错误码随组件分发**：组件端点抛出的业务异常**无条件** `WithCode`，默认译文随组件嵌入资源（沿用 `Leistd.Localization.Core/Resources` 的做法）。模板现有 `#if (IncludeLocalization)` 包裹的同名错误码随代码迁走。
4. **组件内映射手写**：组件不引入 Mapster 依赖；模板的 `*Profile` 随用例迁入后改为手写投影。

## 三、后台作业组件（`Leistd.BackgroundJobs`）

**包**

| 包 | 内容 |
| --- | --- |
| `Leistd.BackgroundJobs.Core` | `IRecurringJob.ExecuteAsync(RecurringJobContext, CancellationToken)`；`AddRecurringJob<TJob>(name, schedule, scope)`；进程内调度器；`IBackgroundTaskQueue`（非持久，**显式标注进程退出即丢**）；全局开关 `BackgroundJobOptions.Enabled`；持久化作业契约 `IBackgroundJobManager`（只定义，无默认实现） |
| `Leistd.BackgroundJobs.EntityFrameworkCore` | `IRecurringJobStateStore` 的 EF 实现（作业名 → 最近完成时段） |
| 提供器包（按需） | `Leistd.BackgroundJobs.Quartz` / `.Hangfire`：实现周期调度与持久化作业 |

**调度器行为**

- 每次执行新建作用域；每次执行自行捕获异常（.NET 6 起未处理异常默认停掉宿主）；首次执行加随机抖动（OpenIddict、Duende 同做法）。
- 周期取 `IOptionsMonitor`，改完下一轮生效；关着照常排期、到点跳过。
- `Cluster`：`IDistributedLock.TryLockAsync(key, 0)`，抢不到跳过；拿到锁后比对水位，本时段已完成则跳过；`LockLost` 取消传给作业的令牌。锁键 `{应用前缀}:job:{作业名}`，应用前缀来自选项（模板给 `MyProject`）。
- `EveryInstance`：不加锁、不记水位。
- 队列：入队时捕获环境上下文（租户、主体、链路），执行时经 `IAmbientContext` 还原；满时的行为（等待 / 丢弃并记日志）由调用方二选一，不设默认。
- DbMigrator 这类一次性进程关闭全局开关。

**多租户逐库执行**：在 `Leistd.MultiTenancy.EntityFrameworkCore`（已依赖工作单元）提供 `ITenantDatabaseRunner`，封装"库清单 → 切租户 → `requiresNew` 工作单元 → 逐库隔离失败 → 汇总结果"。模板里 `Change(id)` 紧跟 `BeginAsync(requiresNew: true)` 的三处写法与归档服务一并改用它。

**模板里的去向**

| 现有 | 去向 |
| --- | --- |
| `Api/HostedServices/Workers/BackgroundTaskWorker.cs`、`IBackgroundTaskQueue.cs`、`Program.cs` 的三重注册 | 删除，改用 `Leistd.BackgroundJobs.Core` 的队列 |
| `Api/HostedServices/BackgroundJobs/OperationRecordArchiveJob.cs` | 删除，由操作记录组件注册 `Cluster` 周期任务 |
| `Api/HostedServices/BackgroundJobs/HostSettingRefreshJob.cs`、`Api/Options/HostSettingRefreshOptions.cs` | 删除，由设置组件注册 `EveryInstance` 周期任务 |
| `Api/HostedServices/Initializer/ApplicationInitializer.cs` | 留（一次性启动任务，不是周期任务） |
| `Api/HostedServices/Initializer/RemoteIdentityReadinessInitializer.cs` | 留；归属 Security / ServiceClient 的就绪门禁另议，不在本计划 |
| `framework/.../Leistd.Lock.Memory` 的 `MemoryLockCleanupHostedService` | 改为 `EveryInstance` 周期任务 |

## 四、各组件的迁移清单

标记：**迁入** = 整体进组件；**拆分** = 通用部分进组件、业务部分经钩子留模板。路径相对 `template/backend/src/`。

### 1. 操作记录

| 模板现有 | 处理 | 目标 |
| --- | --- | --- |
| `Application/OperationRecords/AppServices/OperationRecordAppService.cs`、`IOperationRecordAppService.cs` | 拆分 | Core 新增查询用例：读者范围判定、对读者可见的动作定义、类别展开与可见性求交（区分"不过滤"与"过滤后为空"）、结果解析、CSV 导出（BOM、公式注入防护、仅宿主列）、导出本身留痕。权限名不再在服务里二次检查，交给端点策略 |
| `Application/OperationRecords/Dtos/OperationRecordDtos.cs` | 迁入 | Core 契约（分页改用第二节 1） |
| `Application/OperationRecords/Mappings/OperationRecordProfile.cs` | 拆分 | 仅宿主字段裁剪进 Core 投影；"主体即目标"的动作集合改为动作定义上的标记（`IOperationActionDefinitionContext.Add(..., selfActing: true)`），模板只给三个动作打标 |
| `Api/Controllers/OperationRecordController.cs` | 迁入 | AspNetCore `MapOperationRecords(o => { o.ReadPolicy; o.ExportPolicy; })`，路由保持 `api/v1/operation-records` 下的现有形状，前端不改 |
| `Application/OperationRecords/Provider/OperationRecordActions.cs` 中的 `OperationRecordsExported` 及其登记 | 迁入 | 组件自带的动作定义提供器（组件会读的码才归组件） |
| `Infrastructure/OperationRecords/OperationRecordArchive.cs`、`Persistence/EntityConfigurations/OperationRecordArchiveEntityConfiguration.cs` | 迁入 | EntityFrameworkCore；`ConfigureOperationRecords()` 一并映射归档表 |
| `Infrastructure/OperationRecords/IOperationRecordArchiveService.cs`、`OperationRecordArchiveService.cs` | 迁入 | EntityFrameworkCore，`<TDbContext>` 泛型，经 `ITenantDatabaseRunner` 逐库执行。存储契约仍只增不减：归档服务与存储分离 |
| `Api/Options/OperationRecordRetentionOptions.cs` | 迁入 | Core 选项；`DailyRunHourUtc` 改为周期任务的调度表达 |
| 保留期的两条设置定义（`SettingConstant.Audit`、`SettingDefinitionProvider` 审计分组、`HostSettingBindings` 两行） | 留 | 业务选择"是否开放成界面设置"；绑定表达式改用设置组件的新 API |
| `OperationRecordActions`（其余）、`OperationActionDefinitionProvider`、`Api/Auth/ApiAuthorizationResultHandler.cs`、句子资源 | 留 | 业务词汇与组合根 |

### 2. 通知与实时

| 模板现有 | 处理 | 目标 |
| --- | --- | --- |
| `Infrastructure/Notifications/INotificationCleanupService.cs`、`NotificationCleanupService.cs` | 迁入 | `INotificationStore` 补删除（单条、本人全部）、分页、仅未读；EF 实现随存储。模板的清理服务删除 |
| `Api/Controllers/NotificationController.cs` | 迁入 | 新包 `Leistd.Notifications.AspNetCore`：`MapNotifications()`，只作用于当前用户，条数上限（现状 `maxCount` 无上限）。"身份不能操作"错误码随组件 |
| （缺口）旧通知无人清理 | 新增 | EntityFrameworkCore 保留期服务 + `Cluster` 周期任务，默认开启 |
| （缺口）删除用户后通知成孤儿 | 新增 | 存储提供按用户删除；模板 `UserAppService.DeleteAsync` 调用 |
| `Api/Filters/SettingsNotificationDeliveryFilter.cs` | 拆分 | 新桥接包 `Leistd.Notifications.Settings`：按 `{前缀}.{类型}.{渠道}` 读收件人的用户级布尔设置，未定义即投递；前缀与"必达组合"（安全 + 站内）经选项给出，留模板 |
| `Api/Notifications/EmailNotificationChannel.cs` | 拆分 | 通用邮件渠道（HTML 编码、经队列异步发送）进桥接包 `Leistd.Notifications.Email`；"取收件地址、是否已验证"经 `INotificationRecipientResolver` 由模板实现 |
| `Api/Middlewares/HubAccessTokenMiddleware.cs` | 迁入 | `Leistd.AspNetCore.SignalR`：只在已映射的 Hub 路径上把查询串令牌转为 Bearer 头，路径取自注册的 Hub 而非写死 |
| `Api/RealTime/PublicResourceSubscriptionAuthorizer.cs` | 拆分 | `Leistd.RealTime.Core` 提供按前缀放行的授权器；前缀 `public:` 留模板 |
| `Application/Notifications/AppNotificationTypes.cs`、`AppNotificationChannels.cs`、`Application/Auth/SecurityAlerts/*`、`Api/Notifications/NotificationSecurityAlertPublisher.cs`、通知偏好的设置定义 | 留 | 业务词汇 |

已知遗留（不在本计划）：已建立的 Hub 连接在账号停用后不会断开（`NotificationHub` 无方法，重校验过滤器不触发）。

### 3. 设置

| 模板现有 | 处理 | 目标 |
| --- | --- | --- |
| `Application/Settings/AppServices/SettingAppService.cs`、`ISettingAppService.cs`、`Dtos/SettingDtos.cs` | 拆分 | Core 查询与写入用例：分层读取（租户层、用户层，而非回落值）、对租户读者隐藏宿主设置、机密值不回传只报"已设置"、`IsVisibleToClients` 写入守卫、按定义选层与越权 403、空串拒绝、分组与本地化键约定、变更留痕与事件。测试邮件、各业务设置的取值校验留模板 |
| `Api/Controllers/SettingController.cs` | 拆分 | 新包 `Leistd.Settings.AspNetCore`：`MapSettings()`；测试邮件端点与两步验证放行元数据留模板 |
| `SettingConstant.BooleanSettings`、`NumericRanges` | 迁入 | `ISettingDefinition` 补值元数据（布尔、区间、枚举候选），写入端据此校验；模板删两张表 |
| （缺口）业务取值校验 | 新增 | `ISettingValueValidator` 扩展点；语言、时区、日志级别、发件地址等校验器留模板实现 |
| `Application/Settings/Events/HostSettingChangedEvent.cs`、`EventHandlers/HostSettingChangedEventHandler.cs`、`Hosting/IHostSettingApplier.cs`、`Hosting/HostSettingDefaults.cs` | 迁入 | Core：`ISettingManager` 写宿主层后发布事件，提交后应用 |
| `Api/Configuration/HostSettingsConfigurationProvider.cs`、`HostSettingsConfigurationApplier.cs`、`HostSettingBindings.cs` 的构建 API 与 `CaptureDefaults` | 迁入 | 新包 `Leistd.Settings.Hosting`：宿主级设置 → 配置源 → `IOptionsMonitor`，整组原子应用、校验失败回退上一组；刷新改为 `EveryInstance` 周期任务 |
| `HostSettingBindings.All`（具体绑定清单）、`SettingConstant`、`SettingDefinitionProvider`、时区提供器 | 留 | 业务设置与组合 |

### 4. 权限

| 模板现有 | 处理 | 目标 |
| --- | --- | --- |
| `Application/Permissions/AppServices/PermissionAppService.cs`、`IPermissionAppService.cs`、`Dtos/PermissionDtos.cs` | 拆分 | Core 管理用例：当前有效权限（含超管短路）、定义树（剔除停用项与空组）、授予查询、带版本整体替换、变更留痕。主体是否存在、主体显示名经 `IPermissionSubjectDirectory` 由模板实现 |
| `PermissionAppService.MatchesCurrentSide` | 迁入 | `IPermissionDefinitionManager` 按当前侧过滤（检查器已有同一边界，定义侧补齐） |
| `Api/Controllers/PermissionController.cs` | 迁入 | `Leistd.Authorization.AspNetCore`：`MapPermissionManagement()`，策略名选项必填；主体类型可配置（现状只开放角色） |
| `SystemInitializer` 与 `TenantSeeder` 里重复的"从未写过才全量授予" | 迁入 | Core 提供"首次授予"辅助方法；角色创建留模板 |
| `PermissionConstant`、`PermissionDefinitionProvider`、`PermissionSubjectProvider`、`RoleAppService`、`UserAppService` | 留 | 业务词汇与业务模型 |

`authorization-data-scope`、`authorization-resource`：模板未使用，无迁移项。

### 5. 多租户

| 模板现有 | 处理 | 目标 |
| --- | --- | --- |
| `Application/TenantConnections/AppServices/TenantConnectionAppService.cs`、接口、DTO、`Mappings/TenantConnectionProfile.cs` | 迁入 | Core 用例 + AspNetCore `MapTenantConnections()`（含机器端点 `runtime/{tenantId}`、`migration`，策略名选项必填） |
| `Domain/Tenants/Connections/ITenantConnectionDirectory.cs`、`Infrastructure/TenantConnections/TenantConnectionDirectory.cs` | 迁入 | 契约进 Core、实现进 EntityFrameworkCore |
| `Application/TenantConnections/ConnectionStringGuard.cs`、`EnsureValidName` | 迁入 | 连接管理器对非法连接名、不可解析的连接串直接抛带码的 400，不再以 `ArgumentException` 变 500 |
| `Infrastructure/TenantConnections/IdentityTenantConnectionClient.cs`、`IdentityTenantConnectionStore.cs`、选项 | 迁入（**待确认**） | 端点契约由框架定义之后，远端连接存储可随之由框架提供（`Leistd.MultiTenancy.ServiceClient`）。这推翻了 `ITenantConnectionConfigurationStore` 注释里"框架不认识任何具体服务的 HTTP 接口"，需确认 |
| `Infrastructure/TenantConnections/InMemoryTenantDatabaseEnumerator.cs` | 迁入 | Core：未登记连接存储时的单库清单，按注册自动选择 |
| `Application/Tenants/AppServices/TenantAppService.cs`、接口、DTO、`TenantProfile` | 拆分 | Core 租户管理用例：分页、查询、更新、启停、删除、按名与按域名解析；创建编排（先停用登记 → 同一控制面工作单元登记连接 → 新工作单元开通 → 启用）与失败补偿（顺序固定）。开通与清理经 `ITenantProvisioner`（`Seed` / `Purge`），启用前置条件经 `ITenantActivationGuard`，数据库错误描述经提供程序钩子（现为 PostgreSQL 错误码） |
| `Api/Controllers/TenantController.cs` | 拆分 | `MapTenantManagement()`；`POST {id}/impersonate` 留模板 |
| `Api/Middlewares/TenantSessionRecoveryMiddleware.cs` | 迁入 | MultiTenancy.AspNetCore 可选中间件；Cookie 方案与响应头名经选项 |
| `Application/Tenants/TenantSeeder.cs`、`TenantImpersonationAppService.cs`、`CreateTenantInputDto` 的管理员字段 | 留 | 实现 `ITenantProvisioner`；模拟登录依赖会话与用户模型 |
| `tests/.../TenantConnectionResolutionTests.cs` | 迁入 | 测的是框架远端解析（按租户分区缓存、单飞取消、拒绝他租户结果），移到框架测试 |

### 6. 锁

只作原语使用，无迁移项。Redis / 内存二选一的注册可由锁组件提供 `AddLock(configuration)` 辅助方法，模板删掉手写分支。

## 五、任务顺序

依赖决定顺序；每步独立验收（框架相关测试 + 打包 + 受影响模板场景生成、编译、相关测试 + 静态闸门），不攒到最后。

1. 前置契约：分页下沉、端点组响应包装、组件错误码资源（第二节）。
2. `Leistd.BackgroundJobs`：周期任务、调度器、水位存储、进程内队列；`ITenantDatabaseRunner`；模板队列与锁清理任务切换。
3. 操作记录：查询用例、端点、归档与保留期任务迁入；模板删对应代码。
4. 通知：存储删除与分页、端点、保留期任务、两个桥接包、SignalR 令牌中间件、前缀授权器。
5. 设置：值元数据与校验扩展点、用例与端点、`Leistd.Settings.Hosting`。
6. 权限：管理用例、端点、按侧过滤、首次授予。
7. 多租户：连接管理与目录、租户管理编排与钩子、会话恢复中间件、（确认后）远端连接存储。
8. 收尾：模板全矩阵、PostgreSQL 场景、有头浏览器走查；长期结论上收 `docs/architecture/`，删除本文。

## 六、验收标准

- 模板里不再有"框架不提供 / 没有……因此模板自己实现"一类的注释；残留的只能是业务决定。
- 模板 `Program.cs` 对每个组件只剩 `Add*` / `Map*` 与业务钩子的注册。
- 前端不改：端点路由与响应形状与现状一致（逐个对照现有控制器的路由与 DTO）。
- 每个新端点：未配置策略名时启动失败；租户读者读不到宿主数据（沿用现有集成测试，迁到组件后在组件测试与模板测试各保一份关键断言）。
- 每个周期任务：两个副本同时到点只执行一次；落后副本在同一时段不重跑；锁丢失时本轮取消。
- 组件文档（`framework/docs/components/*.md`）同步新契约，示例只用该组件真实依赖。

## 七、待确认

1. 远端连接存储是否由框架提供（第四节 5）。
2. 通知保留期默认值：建议启用、已读 90 天、未读 365 天。
3. 桥接包的粒度：`Notifications.Settings`、`Notifications.Email` 各成一包，还是合并为一个 `Notifications.Integration`。
