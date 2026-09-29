# 按评审原则调整框架与模板

基线 `4f16fffb`。方案经 8 路只读审视、五轮交叉复审（Claude / Codex）与官方文档核实定稿；逐项论证不入库。实施前每项以当时 HEAD 复核前提。

## 评审原则

1. 最佳实践终局：不留中间态和兼容层；未发布 API 直接改到位，破坏性变更写进升级说明。
2. 优先官方机制：先查 ASP.NET Core / .NET / ABP，不自建平行抽象。
3. 不过度设计：新类、选项、扩展点须有两个以上真实调用点或明确替换场景；单调用点辅助类并回；说不出场景的配置项删除。
4. 不过度考虑安全：只修"默认就会出错"（静默危险默认值、缺配照常启动），不为显式选择和低概率场景加门槛。
5. 权衡复杂度：写清代价；一行文档能说明的不写成代码。
6. 开发者视角：克隆即可运行；报错指明缺哪个键；必填项少；组件在普通业务项目中按文档简单配置即可上手。
7. 组件通用性：组件只处理自己的类型；能力由宿主显式组合，不替宿主决定部署策略。
8. 扩展性：保留业务项目必要的替换与定制能力。

## 已定决策

| 项 | 结论 | 依据 |
|---|---|---|
| 前端本机开发配置 | 新增入库的 `development` 配置与 `environment.development.ts`（gateway 为空、走本机后端），`npm start` = `ng serve`；`debug` 仅作不入库的个人覆盖。`environment.dev.ts` 是已部署的 dev 环境，不挪用 | Angular CLI 默认配置名；`unit-test` 构建器默认 `::development` |
| 通知与实时连接 | 新增 `AddNotificationsSignalR<THub>()`（`IHubContext<THub>` 推送），无泛型版默认 `NotificationHub`；模板推到 `RealTimeHub`，只映射一个 Hub，客户端方法名加前缀 | ASP.NET Core 一个 Hub 一条连接；代价是 Hub 级授权与选项共享 |
| 对象映射 | 删 `MapsterProfile`/`AddProfiles`，模板改 Mapster 官方 `IRegister`；换库由 `IObjectMapper` 承担，映射配置随库重写，不做库无关配置层 | 映射配置天生属于具体库；现包装本身也返回 Mapster 类型 |
| 存量口令 | 保留 `IPasswordHasher`，返回值改为含"需重哈希"的结果；默认实现包装官方 `PasswordHasher<TUser>`，显式配置 `IterationCount`，登录时回写。模板内**不写**旧格式兼容分支，升级说明给出迁移代码（含回写与失败处理） | 口令哈希是模板代码，已派生项目收不到改动，新项目没有旧数据 |
| `ICurrentUser` 角色成员 | 删 `FindClaims`/`GetAllClaims`/`PhoneNumber`；`GetRoles`/`IsInRole` 与 Cookie 身份 roleType 修正同一提交删除 | Cookie 身份默认 RoleClaimType 与写入的 `"role"` 不一致，官方 `IsInRole` 会静默返回 false |
| 校验错误形状 | **不改写**官方 `HttpValidationProblemDetails`；框架服务客户端与模板前端同时识别 Leistd 数组与官方字典；框架自己产出仍为数组（逐字段错误码） | 原则 7：通用组件不认领官方类型、不改变普通宿主的标准响应 |
| 管理员种子 | 种子留在 API 初始化器（已在分布式锁内串行）；去掉 `DefaultAdmin:Password` 的启动期强制校验，只在确需创建管理员时校验 | 开发用 API 进程内 InMemory 库，独立的 DbMigrator 种不进去 |
| 用户标识 claim | 只共享默认读取规则（`sub` → `NameIdentifier`），不合并语义；各组件保留替换点 | GUID 自然人、`client:<id>` 原始主体、SignalR 用户标识三种语义不同 |
| 前端 Vitest | 独立任务，不与本计划其余阶段混做 | Karma 项目已弃用；Angular v21 起默认 Vitest |

## 一、P0：默认就出错

每项独立小提交，补最小组合测试（如不注册事件总线、不装代理工厂）。

| 编号 | 问题 | 做法 |
|---|---|---|
| A2 | `LocalEventSaveChangesInterceptor` 只从 Added/Modified/Deleted 收集事件，却清空全部实体；聚合根 Unchanged 时事件丢失 | 去掉状态过滤，收集与清空同一范围（与 eShop、ABP 一致） |
| A3 | 漏装 DynamicProxy 服务提供程序工厂时 `[UnitOfWork]`、阶段过滤、注册校验全部静默失效，事件处理器两趟都执行 | 工厂登记标记服务；`AddUnitOfWork` 注册的 `IHostedLifecycleService` 在 `StartingAsync` 检查标记，缺失即抛并写明 `UseServiceProviderFactory(...)`（`MvcMarkerService` 先例）。直接 `BuildServiceProvider` 不经 Host 的场景写进文档 |
| A5 | `BaseDbContext(options)` 单参构造令租户过滤、审计与开关退化 | 删除，只留 `(options, IServiceProvider?)`；检查传 `null`（设计时）路径 |
| A7 | `TenantSessionRecoveryMiddleware` 写死 `CustomClaimTypes.TenantId` | 改读 `MultiTenancyOptions.TenantClaimType` |
| A8 | 无当前用户时 `LastModifierId` 保留上一人 | 置为 null |
| A10 | 模板 `Authentication:Issuer` 占位值能过组合期校验 | 占位改空，让校验报出键名；修 `RemoteIdentityOptions` 注释漂移 |
| T3 | `environment.prod.ts` 的 `useMock.include` 非空，生产注册 Mock 拦截器与全部 Mock 数据（含演示口令） | 删 prod `include`；production 用 `fileReplacements` 把 Mock 入口换成空实现；构建后 grep 产物确认 |
| T4 | `backend/README.md` 迁移命令缺 `-- --apply`，业务迁移 `--output-dir` 固定 Identity 目录 | 补全并按服务形态修正 |
| T8 | 会话 Cookie 非 Development 一律 `SameSite=None`；无 antiforgery，且有无请求体 POST（`POST /tenants/{id}/impersonate`） | 删会话 Cookie 的 SameSite 行，用 Cookie 认证默认 `Lax`（`SecurePolicy` 不动）；加跨站部署覆盖键；外部登录状态 Cookie（手工 `new CookieOptions()`）显式 `Lax` 并读同一键；修注释。文档写明：同站按公共后缀判断；选 `None` 须自加 antiforgery（Angular 内置 XSRF 跨源不生效）；GET 不得有副作用；.NET 11 默认 CSRF 中间件替代不了本项 |

## 二、P1：收中间态、消门槛

### 框架

| 编号 | 做法 |
|---|---|
| A1 | `AddDddInfrastructure` 内部调用 `AddLocalEventBus()`；删拦截器"可选依赖、只收集不发布"分支与注释；修 `ddd-struct.md` 最小示例 |
| A4 | `AddDddDbContext<T>` 内经 `ConfigureDbContext<T>` 自动挂 DDD 拦截器，**仅当 `T : BaseDbContext`**；拦截器保持 Scoped；删公开 `AddDddInterceptors`。模型约定做成独立 EF convention（`IModelFinalizingConvention`，显式 Fluent 配置优先）：`BaseDbContext` 自动注册（密封 `ConfigureConventions`，另开 virtual 钩子），控制库上下文显式注册同一约定；控制库改完后删 `ConfigureByConvention`；更新模板基线迁移，核对两类快照与设计时建模；框架文档（ddd-struct）补"迁移快照检查"一节（`Database.HasPendingModelChanges()` 用法与 `dotnet ef migrations has-pending-model-changes`），注明升级会改模型的框架版本后必跑，升级说明引用该节 |
| A6/B1/B2 | 由 Host 提供配置的组件默认 `AddOptions<T>().BindConfiguration(configSectionPath)` 再 `Configure(configure)`，有校验链 `ValidateOnStart`；`AddUnitOfWork` 同样统一（纯 `ServiceCollection` 自行注册 `IConfiguration`），删 `IConfiguration` 重载；校验报错按实际配置节给出键名。修掉"只传委托却未绑定"的缺陷（如 `AddMultiTenancy(Action)`、`Leistd:UnitOfWork`） |
| B3 | `AddInterceptor` 按类型去重；必要时用标记服务防回调与校验器重复登记 |
| B4 | `AddAuthorizationEfCore`/`ConfigureAuthorization` → `AddPermissionAuthorizationEfCore`/`ConfigurePermissionAuthorization`，修 XML 错误引用（官方要求 `Add{Service}` 不与官方包重名）。不做全局改名 |
| TR | `TenantRouteCacheOptions` 给默认时长，保留上限校验 |
| A9 | 实际登记了周期任务且无调度器时启动告警（不抛）；设置文档写明"周期刷新兜底、非立即生效" |
| D1 | 删 service-client `TryGetErrorCode` 的 Number 分支（HTTP 状态被还原成业务码 `"404"`），升级说明写明旧数字信封不再支持 |
| D3 | 删 Response 的 `FailResult*`/`ErrorResult` |
| D6 | 删 `BusinessExceptionHandler` 的客户端取消判断，并删 `SuppressDiagnosticsCallback` 中"取消且 RequestAborted"条件（官方中间件 .NET 8+ 已先返回 499） |
| D10 | 删 `ITenantManager`/`ITenantManagementService.CreateAsync(..., Guid id)` 重载，测试改用无 id 重载 |
| E1 | 保留 `ILocalLock`。Lock.Core 提供"本地锁充当分布式锁"标记服务：内存锁以 `TryAdd` 注册 `IDistributedLock` 兜底并登记标记；Redis 见到标记才替换，否则 `TryAdd`（与顺序无关、不覆盖宿主实现）。`ILock` 只作基接口不注册；文档补"两者共存" |
| F3 | `MapRealTimeHub`/`MapNotificationHub(pattern = 默认)` 返回 `HubEndpointConventionBuilder`；删 `RealTimeOptions`；保留"必须注册订阅授权器"的启动检查；邮件通知渠道补 `Link` |

### 身份口径（按此顺序）

| 编号 | 做法 |
|---|---|
| roleType + C10 | 模板 Cookie 身份按 `"role"` 构造 RoleClaimType，同一提交删 `GetRoles`/`IsInRole` 等成员，做 Cookie 路径角色授权组合验证；删除说明注明原比较方式与官方 `IsInRole` 的差异 |
| F2 | ~~原方案保留 `ActorIdClaimType`、`UserIdClaimTypes` 独立选项~~，已被下节 T-A 取代：主体标识与租户的 claim 类型统一收归 `ClaimTypeOptions`，各组件的独立选项删除 |
| C11 | `PermissionAuthorizationHandler` 评估 `context.User`：`IPermissionSubjectProvider` 增加按主体解析（读标识用 F2 的共用方法）；显式传入的非当前主体不走作用域缓存；主体租户与当前作用域不一致时拒绝 |

### 身份口径：租户链路（阶段 4 扩展，用户已确认终局方案）

盘点结论：租户经过身份 claim、匿名请求提示、服务间委托、令牌端点、非 HTTP 入口、日志审计、键与状态七条通道，三种信任性质不同的通道混用名字、同一值多处定义，不闭环。终局做法：

| 编号 | 做法 |
|---|---|
| T-A | Security.Core 新增 `ClaimTypeOptions`（Options 模式，对应 ASP.NET Core Identity 的 `ClaimsIdentityOptions`）为 claim 类型唯一来源：`UserIds`（默认 `sub` → `NameIdentifier`）、`TenantId`（默认 `tenant_id`），并提供唯一读取规则（用户标识取第一个非空白原始值；租户 claim 三态：无 = 宿主、恰一条 GUID = 租户、多条或非 GUID = 非法）。删除 `MultiTenancyOptions.TenantClaimType`、`HubIdentityOptions.UserIdClaimTypes`、`OperationRecordOptions.ActorIdClaimType` 与 F2 的静态 `UserIdClaims`；`ICurrentUser` 新增主体标识原始值 `SubjectId`。所有读写方（框架与模板）改读该选项；非法租户 claim 一律失败关闭，含 `ICurrentUser.TenantId` |
| T-B | 三种通道分开命名、各有唯一定义点：匿名提示头默认改 `X-Tenant`（可带 id 或名称，`MultiTenancyOptions` 管头名与 query 键）；服务间委托保留 `X-Tenant-Id`（仅 GUID），调用方转发与被调方还原同源于一个常量且两侧可配；服务客户端可按客户端关闭租户转发，租户路由客户端默认不转发；前端集中常量；CORS 暴露头取配置值 |
| T-C | 令牌端点换码与刷新时按令牌主体的租户切换上下文再加载用户（修复租户用户走不通 OIDC 换码/刷新） |
| T-D | 租户日志属性名统一常量，HTTP、环境上下文与按租户库执行都写入，请求完成日志带租户；操作记录 `ActorTenantId` 取操作人自己的租户 |
| T-E | 带租户的键统一经 `CurrentTenantKeyExtensions`；登录失败计数与外部登录 state 按租户隔离，外部登录 state 绑定发起时租户并在回调核对 |
| T-F | 前端 Resource 形态接受宿主用户；`by-host` 探测走已配置解析器；服务间还原不产生重复租户 claim |

验收：租户用户完整 OIDC（授权、换码、刷新）；自定义租户 claim 名下签发、解析、判权、服务间转发全链路一致；非 HTTP 入口日志带租户。实施前逐项复核盘点所述前提。

实施记录（已实施，待审查）：
- T-A：`ClaimTypeOptions` 取代 F2 的静态 `UserIdClaims`（F2 的"各组件保留替换点"随之收敛为这一处：Hub 与操作记录的专属选项删除，定制寻址仍可替换官方 `IUserIdProvider`）。租户规则按身份判定：官方 `PolicyEvaluator` 会合并多个认证方案的身份，同一主体各身份各带一条相同租户 claim 是合法的（实测 userinfo 端点即此形态）；同一身份内多条仍拒绝。服务间还原以委托租户取代调用方身份上的租户 claim。
- T-B：调用方转发选项补三个头名；租户路由客户端 `PostConfigure` 固定不转发。前端常量集中于 `core/services/tenant-protocol.ts`。恢复头在模板里两处都引用同一默认常量，未改。
- T-C：令牌端点与 userinfo 都有此缺陷（userinfo 经 `[Authorize(服务端方案)]` 认证，发生在多租户中间件之后）。租户切换不能放进 async 辅助方法（AsyncLocal 回不到调用方），与 `UserSessionValidator` 同写法内联。
- T-D：日志作用域收在 `CurrentTenant.Change`（日志作用域按异步流环境生效），所有切换入口自动带租户；请求完成日志经 Serilog `IDiagnosticContext`。
- T-E：设置宿主键 `h:` → `host:` 属落库数据变更，升级说明给出迁移 SQL。
- 有头浏览器实测（租户登录，确认登录请求带 `X-Tenant`）暴露既有缺陷：自证类动作（登录、注册）在匿名请求里记录，`ActorId` 为空，`Actor` 层只对本人放行，租户用户看不到自己的登录记录（宿主整层可见故被掩盖）；且 T-D 改操作人租户后，匿名记录会被记成宿主。修为：匿名时 `targetIsActor` 的动作以目标为操作人、名快照取目标名，操作人租户取请求上下文；查询的 `ActorIsTarget` 改为"操作人即目标"。
- 阶段四复审后处理：`Actor` 层"本人"改为标识与所属租户同时相同（`ForTenantReader(actorId, actorTenantId)`），堵住宿主主体在租户里留下的记录被租户同标识主体认领；删去 `ActorIsTarget` 对历史空值的兼容分支，历史数据改由升级说明中的一次性 UPDATE 补齐；模板签发令牌（用户与机器）在 `sub` 之外按 `ClaimTypeOptions.UserIds[0]` 同值写一条，机器 scope 策略改按 `ClaimTypeOptions` 读主体，补自定义 `UserIds` 全流程集成测试；`ReadTenant` 维持"不带租户 claim 的身份不参与判定"（服务间还原即"机器身份 + 被代表用户"，按冲突处理会误伤），改正注释并补用例；设置键迁移写明须先于新版本执行并给出 EF 迁移写法。
- 阶段四第二轮复审后处理：Codex 构造出"两份合法用户凭据合并后，标识取宿主 42 号、租户取租户 T 的 7 号"的拼接（标识只在租户内唯一的项目会越权）。`ClaimTypeOptions` 改为用户标识与租户取自同一个身份：第一个带用户标识的身份为主体身份；其他带标识的身份若带不同租户（含主体为宿主）判非法；只带租户的身份（服务间只委托租户）仅在主体无租户时补位；带标识无租户的其他身份（调用方机器身份）不参与。`SubjectClaims` 按 Claude 建议改为 `UserIds` 不含 `sub` 时才写副本；其 `#if (LocalIdentity)` 守卫与同目录 `AuthPrincipalFactory` 一致，保留。
- 阶段四第三轮复审后处理：`DefaultPermissionChecker` 当前主体路径补租户 claim 合法性检查（非法即无授予，不要求等于当前租户）；补无参与显式当前主体两个入口的拒绝用例及宿主切入租户的放行用例；`ClaimTypeOptionsTests` 同源用例改用不同标识使按类型取值的旧实现变红。两处均变异证伪。
- 阶段四第四轮复审后处理（按用户要求本轮修完，不留后续项）：`ClaimTypeOptions` 公开 `FindSubjectIdentity`，`CurrentUser` 的 `Username`、`Name`、`Email` 只在主体身份上读取；显式主体路径在未接多租户时也先校验租户声明合法性，与当前主体路径一致；`TenantSessionRecoveryMiddleware` 改按 `ReadTenant` 判定租户会话。三处均补用例并变异证伪。
- T-F：`by-host` 改跑 `ITenantResolver`；前端 Resource 形态接受宿主用户。前端登录页 `decision=tenant` 且 `tenant=null` 的分支后端不会返回（不存在的租户在中间件即 404），未动。

### 模板

| 编号 | 做法 |
|---|---|
| CI | 迁移快照检查做成生成项目的单元测试 `MigrationSnapshotTests`（设计时工厂 + `Database.HasPendingModelChanges()`，不连库、不装 `dotnet-ef`），矩阵每个场景与业务项目自身 CI 都会执行；覆盖业务与控制面上下文，`OpenIddictDbContext` 刻意忽略动态模型差异，不纳入 |
| T1 | 去掉 `DefaultAdmin:Password` 的 `ValidateOnStart`，移到"确需创建管理员"处校验并沿用口令策略、报出键名；`appsettings.Development.json` 给满足策略的本机默认口令；README 写明它只是本机演示凭据，接共享真实开发库时须在 user-secrets 覆盖 |
| ENV + C8 | 见已定决策；开发代理改 Angular CLI `proxyConfig`：转发 `/api/**`、Hub（仅此条 `ws: true`）、`/connect/**`、`/.well-known/**`、外部登录回调；不开 `rewriteWsOrigin`；`changeOrigin` 实测本地与外部登录后定；删 SpaProxy 选项与实现，改写 README 联调章节 |
| H | 见已定决策；验收泛型与无泛型注册不会产生两个通知渠道或重复推送 |
| T7 | compose 中 backend 对 redis 改 `condition: service_healthy` |

## 三、P2：官方机制替换与清理

| 编号 | 做法 |
|---|---|
| C2 | 删 `CorrelationIdMiddleware`、`ICorrelationIdProvider`、`[CorrelationId]` 与拦截器（Tracing.Core 去掉 Castle）、`Tracing.HttpClient`、异常组件的 traceId 覆盖与 `RequestTraceId`；后台队列改为捕获 `ActivityContext`，恢复用 `new Activity(name).SetParentId(...).Start()`（不用 `StartActivity`，无监听器返回 null），覆盖入队时无 Activity 的路径；OperationRecorder 读 `Activity.Current?.TraceId`。列宽 64 已够，模型不变则不出迁移。升级说明：`traceId` 变完整 `Activity.Id`（可一行改回 32 位）、不再回写追踪响应头、采信非 W3C 入站头改用 propagator 或 HeaderPropagation |
| C4 | 见已定决策；显式扫描写 `options.Configurators.Add(c => c.Scan(asm))`；修 `UserProfile.cs` 无参 `Adapt<>` 绕开组件配置的旁路并加能检出旁路的用例；规范补：业务服务只注入 `IObjectMapper`、能按约定映射不写配置、复杂转换写普通方法、配置内嵌套映射不用无参 `Adapt<>` |
| C6 | 远端路由缓存改 `HybridCache.GetOrCreateAsync`：每次显式传 `Expiration`、`LocalCacheExpiration`、`Flags = DisableDistributedCache`（否则含口令的连接串进 Redis）；工厂内自开作用域；删协调器；保留 TTL 与跨租户校验；文档不写"跨实例立即失效" |
| C7 | `ServiceClientLoggingHandler` 只留传输异常统一（只记一次）；删日志部分与 `LogPayloads`/`MaxPayloadLength`；组件不调用任何日志扩展；脱敏保持官方默认（.NET 9 起遮蔽全部头值），不建议列举式 `RedactLoggedHeaders`；模板 HttpClient 日志级别复核 |
| C9 | 见已定决策 |
| D2 + D4 | 同一提交：服务客户端与前端把字典 `errors` 展开为现有 `ErrorItem`（保留字段名与全部消息）；删前端信封兼容（`errorCode`、数字 `code`、`message`）；文档写明两种形状的来源与"开启信封需前端适配"；实测 `AddValidation()` 产出的键名 |
| D7 | 文档写明异常本地化依赖非泛型 `IStringLocalizer` 及回落文案 |
| E2 | 删 `IEventBus`，成员并入 `ILocalEventBus`，注册改幂等；未来分布式总线自定 `IDistributedEventBus` |
| E3 | 删空方法 `AddRealTime()` |
| E4 | 保留 `RemoteTokenAuth`：凡指"验证远端令牌或 OIDC 客户端"的条件统一用它，`!LocalIdentity` 只表示无本地用户表 |
| E5 | 删 `/uploads` 静态目录、compose 卷、`.gitignore` 条目（已并入阶段 5） |
| E6 | 删 `environment.api` 除 `gateway` 外的死字段，修前端 README；`GATEWAY_SERVICE_NAME` 补文档；保留 Dockerfile 用的 `__API_GATEWAY__` 占位 |
| E7 | 删 `format.utils.ts` 及 spec |
| F1 | 新建 `Leistd.MultiTenancy.Management` 用例包（引用 Core、UoW.Core、EventBus.Core），移入租户管理用例、DTO 与开通契约；`ICurrentTenant` 等中性契约留 Core；搬包时同步清理受影响的无用 using |
| IDE0005 | framework 组件启用构建期强制（warning，Release 转 error）；模板不强制 |
| T7 余项 | api、migrator 阶段 `USER $APP_UID`；验证 compose 文件型 secrets 对 UID 1654 可读 |
| 模板上手 | launchUrl 改 `api/health/live`（已并入阶段 5）；`.http` 换真实端点；镜像名改 `${IMAGE_REGISTRY:?}/companyname-projectname:${IMAGE_TAG:-latest}`；`TZ` 默认 `UTC` 并写明它决定服务端文本的默认时区；`AddOpenApi()` + 默认仅 Development `MapOpenApi()` |
| C1 | 最后决定是否实施：判定改 `AuthorizationHandler<OperationAuthorizationRequirement, IAuthorizableResource>` + `IAuthorizationService`，ACL 拒绝用 `context.Fail()`、超管在 handler 内跳过；保留 ACL 存储、`IAuthorizableResource` 与操作常量；Resource.Core 只引独立包 `Microsoft.AspNetCore.Authorization` |
| V | 迁 Vitest：先迁配置，再 `ng g @schematics/angular:refactor-jasmine-vitest`；装 jsdom 不装 happy-dom；暂不开 browser 模式；删 karma 系列、`jasmine-core`、`@types/jasmine`、`karma.conf.js`、`istanbul-lib-instrument`（`debug` 先 `npm ls`） |

## 四、明确不改

| 项 | 理由 |
|---|---|
| `IClock` 换 `TimeProvider` | 已包装 `TimeProvider`，`Normalize` 有语义 |
| 锁换 Medallion DistributedLock | 无官方抽象，现实现很薄 |
| `ValidationException` → 400 | 文档明写，语义单一 |
| 异常本地化新增资源类型选项 | 单一用处，补文档即可 |
| 注册 API 全局改名 | 收益只在观感 |
| ServiceClient 转发改宿主组合 | 违反"组件默认值在 AddXxx 里登记" |
| compose 数据库口令必填 | 默认口令本身是危险默认值 |
| `DomainFormat` 校验 | 拦的是配置错误 |
| `ICurrentClient`、`ITenantConnectionDirectory`、`ControlPlaneConnectionStringName`、`docker-compose.override.yml` | 均有真实调用方 |
| 锁定期间的登录尝试不逐次审计 | 有意设计（防匿名刷表），触发锁定那次已记录 |

## 五、分阶段（每阶段可单独验收）

1. **前置**：修根 `Directory.Build.targets` 的过期模板符号；模板 CI 加迁移快照检查。验收：framework 构建与测试通过，快照检查在现基线通过。（已完成）
   - `Directory.Build.targets` 改为 Identity 全功能场景的符号，并按该场景排除 `Migrations/Resource`（两套业务快照同时编译时 EF 任取其一）；`check-template-symbols.ps1` 拦截其中的未定义符号。
   - 迁移快照检查落为生成项目单元测试 `MigrationSnapshotTests`；`!LocalIdentity` 对 `UnitTests/Infrastructure/**` 的整目录排除收窄为具体文件，Resource 形态也生成该测试。
   - 模板矩阵 9 场景（后端构建与测试）通过；在生成项目给实体加属性不生成迁移，测试按预期失败。
   - 仓库内直接运行集成测试时 7 个本地化用例失败：`Api/Resources/*.json` 含未经裁剪的模板条件指令，JSON 解析失败被跳过（本次启用 `IncludeLocalization` 后暴露）。模板本身由矩阵保证，不改框架；仓库内构建的边界写入 `Directory.Build.targets` 注释：只保证编译与单元测试。
   - 经 Claude 与 Codex 代码审查：补测试前提说明与注释措辞；"闸门精确校验符号组合"与"隔离测试环境变量"两条不采纳（前者需在脚本里求值模板表达式，后者经核实不影响模型）。
   - 第二轮审查：统一 OpenIddict 排除理由为运行时注释的说法；README 补"新增 DbContext 时补断言"（不做工厂自动发现：真实库 `Migrate` 会因待定模型变更抛错，漏检不是静默的）；检查保留为项目内测试，不由框架出包。
2. **P0**：A2、A3、A5、A7、A8、A10、T3、T4、T8。验收：相关测试；前端生产构建产物无 Mock；有头浏览器实测 Cookie 登录与外部登录。
   - A3：标记由 `DynamicProxyServiceRegistrationCallbackFactory.CreateBuilder` 登记（基类 `CreateBuilder` 改为 virtual）；检查实现 `IHostedLifecycleService.StartingAsync`，先于任何托管服务的 `StartAsync`；报错同时给出 `UseServiceProviderFactory` 与 `ConfigureContainer` 两种写法。框架里两处测试宿主原本未接工厂，已补。模板 DbMigrator 不启动宿主、托管服务不执行，未改。不拆分"手动工作单元"注册入口：未织入时手动工作单元内的事件处理器同样两个阶段各执行一次。
   - A10：同类的 `Leistd:ServiceClients:Identity:BaseAddress` 占位一并改空；其必填校验由框架 `AddRemoteTenantConnectionStore` 对自身客户端选项 `Validate + ValidateOnStart`（报出键名，非模板项目同样受保护，ServiceClient 通用选项仍可留空）；内存库模式不注册远端存储，无需该地址。README 补 Resource 首次启动与改用真实库时的配置。
   - T8：配置键为 `SessionCookie:SameSite`（`SessionCookieOptions`，仅 LocalIdentity）；会话 Cookie 经 `AddOptions<CookieAuthenticationOptions>(scheme).Configure<IOptions<SessionCookieOptions>>` 与状态 Cookie 读同一管道；未配置时会话 Cookie 沿用框架默认 `Lax`，状态 Cookie 显式 `Lax`；部署文档补 `None` 的适用情形（跨站 POST 到 `/connect/*`、`form_post` 回调直落 API）。
   - 审查后延后：dev/test/uat 构建配置的 Mock 替换并入阶段 4 的 ENV；登录页演示账号提示与 `login.ts` 中 `isMockEnabled` 另行解析 `useMock`（与 `shouldProvideMock` 口径不一致）留待后续阶段。
3. **框架结构**：先定 A4、A6、E1 的接口与组合规则，再实施 A1、A3 余项、A4、A6/B1/B2、B3、B4、TR、A9、D1、D3、D6、D10、E1、F3。验收：构建、测试、打包到 `.tmp/local-feed`、核对包内签名、隔离消费，覆盖只装单个组件的普通宿主路径。（已实施，经三轮审查）
   - A4：`AddDddDbContext<T>` 在首次登记且 `T : BaseDbContext` 时经 `ConfigureDbContext<T>` 挂三个拦截器（重复登记不挂第二层，测试按拦截器数量断言——重复执行多数碰巧幂等，只看行为抓不到）。约定类命名 `DddEntityConvention`（`IModelFinalizingConvention`，约定来源写入；并发标记三项分别写入，一项被显式覆盖不影响其余）；`BaseDbContext` 封闭 `ConfigureConventions`，钩子为 `ConfigureModelConventions`。模板控制库 `IdentityControlDbContext` 显式注册该约定（其实体已显式配置，模型不变）。模型影响只在 Resource 形态：`Roles`/`UserRoles` 审计列此前未走约定（`text`），改为 64，已改 Resource 基线三份文件；Identity 形态无差异。
   - A6：`AddUnitOfWork`、`AddMultiTenancy`、`AddGlobalExceptionHandler`、`AddSmtpEmailSender`、`AddServiceUserContext`、`AddRedisDistributedLock` 合并为单一入口 `(configure?, configSectionPath = T.SectionName)`，删 `IConfiguration` 重载；`ServiceUserContextOptions`、`UnitOfWorkOptions` 补 `SectionName`。`OperationRecordOptions` 注释改为不绑定配置节（claim 名属宿主签发细节，只走委托）。tracing 留给 P2 删除；ServiceClient 按服务名传 `IConfiguration` 保留。
   - 审查（Claude、Codex）后采纳：E1 标记改为携带兜底描述符，Redis 只移除那一条（此前"内存兜底 → 宿主 `Add` → Redis"会删掉宿主实现）；A1 默认 `IEventBus` 转发到最终的 `ILocalEventBus`（改 `TryAdd` 后宿主预替换总线会留下第二条发布路径）；A4 约定改用 `FindProperty`（派生实体实现契约、属性声明在已映射基实体上时会漏）；A6 `AddUnitOfWork` 也统一为单一入口、删 `IConfiguration` 重载（"纯 ServiceCollection"只关系测试，生产中只传委托的宿主同样丢配置；两边意见分歧，采纳 Claude，避免之后再破坏一次），`AddDddInfrastructure` 经它绑定；文档补"不支持 `AddDbContextFactory`"；A9 标记改 `TryAdd`。E1、A1、A4 继承属性、UoW 绑定四个新用例经变异证伪。
   - 终审后采纳：E1 内存兜底只看非 keyed 注册（具名锁曾阻止默认兜底）；Smtp、Redis 锁、多租户（域名格式与租户存储两个）的校验器按实际 `configSectionPath` 报键名；并发标记必填的理由改正（更新条件含主键，不存在"匹配所有 null 行"）；`AddDbContextFactory` 限定为默认单例生命周期；补 TTL 可选、Response 失败信封 `code`/`errorCode` 语义、自研调度器登记标记的文档；Realtime 用例断言追加的授权策略确实附着；A4 补"先登记后 `AddDbContext`"顺序用例。
   - E1：模板 Redis 分支只注册 `AddRedisDistributedLock`，没有 `ILocalLock`；模板目前无进程内锁用法，不补。
   - ~~F3 后续：模板安全提醒的 `Link` 是相对路径，邮件不附……另行评估。~~ 已并入阶段 5（邮件渠道 `PublicBaseUrl`）。
4. **身份口径**：roleType + C10 → F2 → C11。验收：权限、通知推送、操作记录端到端；C11 在同一作用域按"当前主体 → 其他主体 → 当前主体"及跨租户主体验证不串人。（已实施，待审查）
   - roleType + C10：只有会话 Cookie 路径错（令牌路径已按 `Claims.Role` 构造）；Cookie 票据序列化保留 RoleClaimType，修构造处即可。模板集成用例经真实登录→按会话方案认证→官方 `IsInRole` + `RequireRole` 判定，并断言无此角色时判否；撤回修复即红。五个成员框架与模板均无调用方。
   - F2：`UserIdClaims`（Security.Core）提供 `DefaultTypes` 与 `FindUserId`（`ClaimsPrincipal` 与 `ICurrentUser` 两个入口，共用"跳过空白"规则）。取名避开 `HubIdentityOptions.UserIdClaimTypes` 属性同名。操作记录读取口径收进 `OperationRecordOptions` 的内部方法供记录器与查询服务共用；非空 `ActorIdClaimType` 不再回落。`FakeCurrentUser` 只给 Id 时补 `sub`，与真实实现同源（原测试靠旧回落才通过）。
   - C11：不用 `IAmbientContext.Begin` 切换主体（其契约规定 HTTP 请求不使用）；接口按已确认方案直接新增 `GetSubjectAsync`，不加默认实现。"当前主体"按与 `ICurrentPrincipalAccessor.Principal` 引用相等判定（Hub 复评在 `Begin(principal)` 内，同样命中）。租户比对读 `CustomClaimTypes.TenantId`（与 `ICurrentUser.TenantId` 同源，授权 Core 看不到 AspNetCore 的 `TenantClaimType` 选项），仅对非当前主体；授权 Core 因此新增对 Security.Core 的引用。
   - 审查（Claude、Codex）后采纳：检查器快照记下加载时的主体引用与租户，作用域内当前主体或租户被切换时重新加载（此前 `Change` 后按引用相等会拿到前一个主体的授予，无参重载原本也有此粘滞）；升级说明改正"操作人标识结果一致"的说法。分歧：固定读 `tenant_id` 与 `TenantClaimType` 不跟随——Codex 要求提供替换点，Claude 认为补文档即可；采纳后者，文档写明边界。~~后续：若要支持自定义租户 claim……~~ 已由阶段 4 T-A（`ClaimTypeOptions`）实现。
5. **模板**：T1、ENV + C8、H、T7（redis）。验收：9 个生成场景；克隆后用 InMemory 直接运行且默认管理员可登录；T1 覆盖新内存库、已有管理员缺口令键、真实库首次建管理员缺键三种启动；实测本地与外部登录、SignalR 只投递一次；compose 一键启动。（已实施，已评审；外部登录与真实提供方的往返缺凭据，用户确认按验收例外处理，只验证发起跳转与回调地址）
   - 定稿（方案经 Claude、Codex 评审，用户确认）：
     - T1：删启动期校验与 `IsPasswordUsable`，只在首次创建管理员时经 `PasswordPolicy.Ensure` 报键名并提示提供方式；Development 配置入库本机口令；compose 去掉 `:?` 门槛；测试覆盖三种启动与"同名普通用户缺口令"。
     - ENV：不新增环境文件，`environment.ts` 即本机配置（网关空）；`debug` 构建配置改名 `development`（两份 `package.json`、`serve` 引用同步），删 `environment.debug.ts` 机制；`providers.ts` 默认空实现，仅 `development` 替换为真实 Mock（新环境默认安全）；Mock 判定抽共享模块，区分"是否安装"与"某请求是否 Mock"，登录页演示账号、通知连接、URL 拦截器共用，不反向引用 Mock 数据。
     - C8：选 Angular `proxyConfig`，转发 `/api/**`、`/hubs/**`（`ws`）、`OpenIddictServer` 下的 `/connect/**` 与 `/.well-known/**`；不代理前端回调页；删 SpaProxy 选项、实现与矩阵脚本注入；先实测 Resource 前端经 Identity dev server 完成授权码登录，走不通退回 YARP。
     - H：`AddNotificationsSignalR<THub>()`，渠道保持非泛型、经内部访问器取 `IHubContext<THub>`，同 Hub 幂等、异 Hub 注册期报错；客户端方法名改为带命名空间的公开常量；模板只映射实时 Hub，前端单连接并保留两类重连处理。
     - F3（并入）：邮件渠道可选 `PublicBaseUrl`，发送时把相对链接解析为绝对地址，站内仍用相对链接；不配则不附链接。
     - E5（并入）：删 `/uploads` 静态目录、compose 卷与 `.gitignore` 条目（无写入方）。
     - 模板上手之 `launchUrl` 并入：改 `api/health/live`。
   - 实施记录：C8 实测先行（Resource 前端经 Identity 前端开发服务器 4200 完成授权码登录、Resource 后端验签），走通后落地；本机经 4200 访问授权端点无 TLS，Identity 开发配置关闭 HTTPS 要求，Resource 开发配置的签发方与前端 `oidc.authority` 默认指向 4200。`Cors:AllowAnyLocalhost` 随跨域联调模式一并删除。实测暴露三处既有前端缺陷并修复：URL 拦截器给绝对地址也带凭据（OIDC 发现文档跨源失败）、登录页把 `/connect/authorize` 的 returnUrl 当前端路由、OIDC 库回调后自行跳 `/` 与回调组件竞争；Resource 首页登录入口链到不存在的本地登录页。矩阵脚本的通知服务文本标记改为新的 Mock 判定。
     - 并入（用户确认，按终局做）：Identity 为下游 API 签发令牌。scope 目录 `OAuthScopes`（由 `OAuthOptions` 推出：OIDC 标准 scope、本服务 API `OAuth:Resource`、租户路由与委托两类仅限机器 scope、`OAuth:ApiResources` 列出的下游 API）成为 `RegisterScopes`、scope 表、开放应用权限校验与令牌受众的唯一来源（此前三处各自维护且已漂移：委托 scope 只在 `RegisterScopes` 里）；访问令牌受众由授予的 scope 推出，Identity 校验受众为自己；Resource 的 `Authentication:Audience` 默认对齐 `-api`；开放应用新增可选 scope 接口，编辑界面按它生成 scope 选项；会话时长移到 `SessionCookie:ExpireDays`（standalone 此前写死 7 天），`OAuth`、`Authentication` 两节各自只在用得到的形态生成。
     - 实施评审（Claude、Codex）后采纳：scope 目录收缩时删除 scope 表中不再登记的 scope；邮件 `PublicBaseUrl` 接入模板配置与 compose（`PUBLIC_BASE_URL`）；部署文档补前端独立部署时的跨域配置；Mock 判定补单元用例（两份提供器、include/exclude 优先级）；README 条件块裁剪后的双空行逐组合修正；通知渠道去掉未使用的 `HubType`。评审前提不成立的：本机跨域被拦（Vite 对 localhost 源已回 CORS）、`signalr-service` 的 `computed` 未用（`unreadCount` 在用）。
     - 真实 PostgreSQL 复验暴露：DbMigrator 在 Development 下因构建期依赖校验失败（注册了全部运行期组件，依赖只在 API 注册的当前用户与权限主体）。Infrastructure 拆出 `AddPersistenceServices`，迁移作业只注册它；随之发现框架 `AddRemoteTenantConnectionResolution` / `AddLocalTenantConnectionResolution` 未登记解析器依赖的 `ICurrentTenant`，改为自身调用 `AddMultiTenancyCore()`（组件自闭环）。模板新增迁移作业注册面的构建期校验用例，框架新增两个入口单独使用的校验用例（撤回修复即红）。
     - 修复复核（第二轮）后采纳：迁移作业的服务组合提为 DbMigrator 的 `AddMigratorServices`，宿主与注册面测试共用，测试解析到 `DatabaseMigrationRunner` 本身（此前手抄注册、漏了它）；升级说明写明 scope 对账会删除目录之外的全部 scope、自定义 scope 须纳入目录、删除不撤销已签发令牌。真实库补验"已有管理员、撤掉口令后重启，原口令仍可登录"。外部登录与真实提供方的往返缺提供方凭据，用户确认列为验收例外。
6. **P2**：按组件分批，C1 最后。验收：各组件测试、打包、9 个生成场景、前端构建；C4 旁路用例、D2 双形状用例；C9 落地后复验默认管理员登录。
7. **V**（独立）：Vitest 迁移。验收：62 个 spec 全过，覆盖率不降。

## 横切

- 升级说明逐项写明破坏性变更：Options 重载、改名、traceId 格式、口令格式与迁移代码、删除的 API、旧数字信封。
- 框架改动按"源码 → 文档 → 打包 → 模板消费"验证；模板改动重新生成项目验证。
- A6、C11、F2、D2 的替换能力列为具体验收点。
- 完成后上收到稳定文档再删除本计划：组件不改写官方类型（原则 7 实例）、按类型契约而非选项区分行为、Options 注册形态、扩展点不按零引用删、映射配置约定、`RemoteTokenAuth` 能力符号用法。
