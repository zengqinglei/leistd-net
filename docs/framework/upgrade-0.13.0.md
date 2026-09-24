# 从 0.12.0 升级到 0.13.0

本清单不是从提交脚注回忆的：两个版本的公共成员清单都取自 Release 的随包 XML
（`<member name="…">` 逐条），再逐包比对得出，结果见[逐条对比](upgrade-0.13.0-api-diff.md)。

0.12.0 当时 `NoWarn` 里含 CS1591，随包 XML 并不完整，因此：

- **删除与搬迁是可信的**——当时记录在案、现在没有，就是真的变了；
- **"新增"一节不列**——里面会混入"0.12.0 时就有、只是没写 XML 注释"的成员，噪声大于价值。
  新能力看 GitHub Release Notes 的「新增」小节。

0.13.0 之后不再有这个问题：CS1591 已经是 Release 下的错误，随包 XML 从此完整。

## 1. 包改名（改 `PackageReference`）

| 0.12.0 | 0.13.0 |
| --- | --- |
| `Leistd.Exception.Core` | `Leistd.ExceptionHandling.Core` |
| `Leistd.Exception.AspNetCore` | `Leistd.ExceptionHandling.AspNetCore` |
| `Leistd.UnitOfWork.EfCore` | `Leistd.UnitOfWork.EntityFrameworkCore` |
| `Leistd.ObjectMapping.AutoMapper` | 已移除，改用 `Leistd.ObjectMapping.Mapster` |

`Leistd.RealTime.AspNetCore.SignalR` 里与通知无关的通用 Hub 基建移到了新包
`Leistd.AspNetCore.SignalR`；`Leistd.Security.Core` 与 `Leistd.Ddd.Domain` 各有一个通用
原语（`IDisposable` 辅助类型）下沉到 `Leistd.Core`。

## 2. 命名空间搬迁（改 `using`）

包名里的 `.Core` 不再出现在命名空间里。只有单一中心概念的小包保留
`Abstractions` / `Services`；Authorization、MultiTenancy、Settings、OperationRecords、Notifications 与
RealTime 按定义、授权、上下文、查询、发布等内容归类，契约与实现同处对应概念命名空间。

按类型数排列的主要映射：

| 0.12.0 命名空间 | 0.13.0 命名空间 | 类型数 |
| --- | --- | --- |
| `Leistd.Authorization` | `Leistd.Authorization.Checking` / `Constants` / `Definitions` / `Grants` / `Subjects` | 14 |
| `Leistd.Auditing` | `Leistd.Auditing.Abstractions` | 9 |
| `Leistd.Exception.Core` | `Leistd.ExceptionHandling` | 9 |
| `Leistd.UnitOfWork.Core.Database` | `Leistd.UnitOfWork.Database` | 6 |
| `Leistd.UnitOfWork.Core.Events` | `Leistd.UnitOfWork.Events` | 5 |
| `Leistd.UnitOfWork.Core.Uow` | `Leistd.UnitOfWork` | 5 |
| `Leistd.DependencyInjection` | `Leistd.DependencyInjection.Abstractions` / `Extensions` / `Registration` | 5 |
| `Leistd.EventBus.Core.Event` | `Leistd.EventBus.Events` | 4 |
| `Leistd.Lock.Core` | `Leistd.Lock.Abstractions` | 4 |
| `Leistd.UnitOfWork.EfCore.Database` | `Leistd.UnitOfWork.EntityFrameworkCore.Database` | 4 |
| `Leistd.Notifications` | `Leistd.Notifications.Dtos` / `Publishing` / `Stores` | 4 |
| `Leistd.Authorization.AspNetCore` | `Leistd.Authorization.AspNetCore.Permissions` | 3 |
| `Leistd.Response.Core.Wrapper` | `Leistd.Response.Wrappers` | 3 |
| `Leistd.EventBus.Core.EventBus` | `Leistd.EventBus.Abstractions` | 2 |
| `Leistd.UnitOfWork.Core.Interceptor` | `Leistd.UnitOfWork.Interceptors` | 2 |
| `Leistd.UnitOfWork.Core.Options` | `Leistd.UnitOfWork.Options` | 2 |
| `Leistd.Ddd.Application.AppService` | `Leistd.Ddd.Application.AppServices` | 1 |
| `Leistd.Ddd.Application.Contracts.AppService` | `Leistd.Ddd.Application.Contracts.AppServices` | 1 |

其中 `Leistd.Authorization` 是一对多拆分，不能只按原 `using` 批量替换；按类型选择最终命名空间：

| 最终命名空间 | 0.12.0 类型 |
| --- | --- |
| `Leistd.Authorization.Checking` | `IPermissionChecker`、`MultiplePermissionGrantResult`、`DefaultPermissionChecker` |
| `Leistd.Authorization.Constants` | `PermissionGrantProviderNames` |
| `Leistd.Authorization.Definitions` | `IPermissionDefinition`、`IPermissionDefinitionContext`、`IPermissionDefinitionManager`、`IPermissionDefinitionProvider`、`IPermissionGroupDefinition`、`PermissionDefinitionManager` |
| `Leistd.Authorization.Grants` | `IPermissionGrantManager`、`IPermissionGrantStore` |
| `Leistd.Authorization.Subjects` | `IPermissionSubjectProvider`、`PermissionSubject` |

`Leistd.DependencyInjection` 同样是一对多拆分：

| 最终命名空间 | 0.12.0 类型 |
| --- | --- |
| `Leistd.DependencyInjection.Abstractions` | `IOnServiceRegisteredContext` |
| `Leistd.DependencyInjection.Extensions` | `ServiceCollectionRegistrationExtensions` |
| `Leistd.DependencyInjection.Registration` | `OnServiceRegisteredContext`、`ServiceRegistrationActionList`、`ServiceRegistrationCallbackFactory` |

`Leistd.Notifications` 的 4 个迁移类型中，`NotificationOutputDto` 进入 `Dtos`，
`INotificationPublisher` 与 `NotificationPublisher` 进入 `Publishing`，`INotificationStore` 进入 `Stores`。
`INotificationSender` 与 `NotificationTypes` 已删除，不计入迁移数量；`Channels` 与 `Errors`
中的公开类型是 0.13.0 新增 API。

余下 30 余条是每个命名空间 1 个类型的同类搬迁（`Leistd.Notifications.EntityFrameworkCore`
拆成 `Stores` / `Entities` / `EntityConfigurations`，`Leistd.ObjectMapping.Mapster` 拆成
`Services` / `Options` / `Mapping`，等等）。

0.13.0 预发布快照中曾出现过的六个核心家族以及 Notifications 卫星包的通用命名空间不作为兼容层保留；如果代码已经跟随过这些快照，按下表再迁一次：

| 预发布命名空间 | 最终命名空间 |
| --- | --- |
| `Leistd.Authorization.Abstractions` / `.Services` / `.Permissions` | `.Checking` / `.Definitions` / `.Grants` / `.Management` / `.Subjects` / `.Errors` |
| `Leistd.MultiTenancy.Abstractions` / `.Services` | `.Context` / `.Tenancy` / `.ConnectionStrings` / `.Management` / `.Errors` |
| `Leistd.Settings.Abstractions` / `.Services` | `.Definitions` / `.Management` / `.Resolution` / `.Stores` / `.Errors` |
| `Leistd.OperationRecords.Abstractions` / `.Services` | `.Definitions` / `.Queries` / `.Stores` / `.Recording` / `.Models` |
| `Leistd.Notifications.Abstractions` / `.Services` | `.Channels` / `.Publishing` / `.Stores` / `.Errors` |
| `Leistd.Notifications.Email.Abstractions` | `Leistd.Notifications.Email.Recipients` |
| `Leistd.Notifications.AspNetCore.SignalR.Services` | `Leistd.Notifications.AspNetCore.SignalR.Channels` |
| `Leistd.RealTime.Abstractions` / `.Services` | `.Publishing` / `.Subscriptions` |

逐条对照见[公共表面逐条对比](upgrade-0.13.0-api-diff.md)。

编译器会把这些全部报成 CS0246 / CS0234，删掉旧 `using` 按提示补新的即可，没有静默失败的风险。

## 3. 分页契约上移到新包 `Leistd.Data`（此前漏写脚注的一条）

| 0.12.0 | 0.13.0 |
| --- | --- |
| `Leistd.Ddd.Application.Contracts.Dtos.PagedRequestDto` | `Leistd.Data.Paging.PageRequest` |
| `Leistd.Ddd.Application.Contracts.Dtos.PagedResultDto<T>` | `Leistd.Data.Paging.PagedResult<T>` |

同时换了三样：**包**（`Leistd.Ddd.Application.Contracts` → `Leistd.Data`）、**命名空间**、
**类型名**（去掉 `Dto` 后缀）。

理由：分页不是 DDD 应用层的概念，组件（多租户、操作记录）也要用它，留在 DDD 契约包里会让
纯组件消费者被迫依赖 DDD 分层。`Leistd.Data` 同时承载连接串契约
（`Leistd.Data.Connections.ConnectionStringNames` / `IConnectionStringResolver` /
`ConnectionStringNameAttribute`），这些在 0.12.0 尚不存在。

> 这一条在 0.13.0 的提交脚注里漏写了，是本清单要偿还的主要一笔。教训是提交脚注必须如实——
> 发版日志与升级清单都由它生成，漏写就等于下游拿不到那一条。

## 4. 成员签名变化

扣掉第 2 节的纯搬迁后，仍有 **212 条**成员是真正删除或签名改变的。按包排列的前几位：

| 包 | 条数 |
| --- | --- |
| `Leistd.UnitOfWork.Core` | 53 |
| `Leistd.Authorization.Core` | 21 |
| `Leistd.RealTime.Core` | 17 |
| `Leistd.Notifications.Core` | 14 |
| `Leistd.Ddd.Infrastructure` | 13 |
| `Leistd.RealTime.AspNetCore.SignalR` | 12 |
| `Leistd.ObjectMapping.AutoMapper` | 10（整包移除） |

三处需要改代码而非改 `using` 的：

**`IPermissionGrantStore` 由"逐条判定"收敛为"按主体批量取"。** 0.12.0 的
`IsGrantedToUserAsync` / `IsGrantedToRoleAsync` / `IsGrantedToAnyRoleAsync` /
`IsGrantedToUserOrRolesAsync` / `GetGrantedPermissionsForUserAsync` /
`GetGrantedPermissionsForRoleAsync` 共 7 个方法，全部由 3 个取代：

```csharp
// Leistd.Authorization.Grants.IPermissionGrantStore
Task<PermissionGrantSet> GetGrantsAsync(string providerName, string providerKey, CancellationToken ct = default);
Task<IReadOnlyList<PermissionGrantSet>> GetGrantsAsync(string providerName, IReadOnlyCollection<string> providerKeys, CancellationToken ct = default);
// 第三个不是"按类型取"：它按权限检查的主体取——用户直授加其所属角色的授予，一次往返
Task<SubjectPermissionGrants> GetGrantsForSubjectAsync(string userId, IReadOnlyCollection<string> roleIds, CancellationToken ct = default);
```

自定义过 `IPermissionGrantStore` 实现的，需按新契约重写；只通过 `IPermissionChecker`
消费的不受影响。`IPermissionGrantManager` 的 6 个授予/撤销方法同样被重排。

**`IUnitOfWork.SetOuter` 移出接口**，只留在实现类上；实现类 `UnitOfWork` 改名
`DefaultUnitOfWork`；接口新增 `Disposed` / `Failed` 两个事件。接口本身的
`Id` / `IsCompleted` / `CompleteAsync` / `RollbackAsync` / `Initialize` / `AddPendingEvents`
保持不变——53 条里绝大多数是内部实现类型的公开面收敛，自己实现过 `IUnitOfWork` 的才受影响。

**`Leistd.Auditing.Core` 不再提供注册入口。** `AddAuditingCore()` 已删除且 Core 包内无替代，
见第 6 节的注册对照表。其余各家族的注册入口以该家族文档 `framework/docs/components/<家族>.md` 第一节为准。

完整逐条清单：[0.12.0 → 0.13.0 公共表面逐条对比](upgrade-0.13.0-api-diff.md)（212 条，按包分组）。

## 5. 数据库迁移（0.12.0 已有组件的表）

本节只列**你在 0.12.0 就已经在用的表**。0.13.0 新增的组件家族（多租户、操作记录、后台任务、
设置、本地化、邮件、资源与数据范围授权、服务客户端）各自带表，那属于"接一个新组件"，
按该组件文档的第一节接入，不是升级动作。

变化的根因是一条：授权与通知的持久化实体实现了 `IMultiTenant`，从此按租户分区。

### `Leistd.Authorization.EntityFrameworkCore`

`PermissionGrantRecord`：

| 变化 | 0.12.0 | 0.13.0 |
| --- | --- | --- |
| 新增列 | — | `TenantId`（`uuid`，可空，`null` 即宿主授予） |
| 唯一索引 | `(PermissionName, ProviderName, ProviderKey)` | 拆成两条**过滤唯一索引**：原三列 `WHERE TenantId IS NULL`，以及 `(TenantId, PermissionName, ProviderName, ProviderKey) WHERE TenantId IS NOT NULL` |
| 检索索引 | `(ProviderName, ProviderKey)` | `(TenantId, ProviderName, ProviderKey)` |

> 可空列上的普通唯一索引约束不住宿主的重复行（`NULL` 互不相等），所以是两条过滤索引而不是一条。

新增表 `AuthorizationVersionRecord`（`Id`、`TenantId`、`ProviderName`、`ProviderKey`、
`Version` 并发令牌、`LastModificationTime`、`LastModifierId`），两条过滤唯一索引同上。
它由 `ConfigureAuthorization()` 一并映射，**不是可选项**：权限授予的乐观并发靠它收口。

### `Leistd.Notifications.EntityFrameworkCore`

`NotificationRecord` 新增 `TenantId`（`uuid`，可空），并新增单列 `CreationTime` 索引，服务的是**保留期清理的访问路径**：
整库按时间扫、最旧的先删，不带 `UserId`（作业用 `IgnoreQueryFilters()` 覆盖同库的全部租户）。
原有的两条索引都以 `UserId` 打头，这条路径一条都用不上，缺了就是每批一次全表扫加排序——
而这张表只涨不消。没有做 PostgreSQL 的部分索引一类提供程序特化。

> 操作记录表也有一条同样的索引，但 `Leistd.OperationRecords.*` 是 0.13.0 新增的组件家族，
> 它的表在 0.12.0 的库里根本不存在，因此不属于升级动作——接入它按组件文档建表即可。

### 怎么生成这份迁移

升级包引用后 `dotnet ef migrations add UpgradeTo0130`，**让 EF 按模型差异自己算**，
不要照着上表手写 DDL——表上还有审计列、长度与必填这类由配置决定的细节，手写会漏。
生成后核对 `Up()` 里是否出现上述列与索引；一条都没有说明包还没升到位。

已有数据：`TenantId` 全部为 `NULL`，即"这些授予与通知属于宿主"。这对单租户部署就是正确语义；
要把既有数据划给某个租户得自己写数据迁移，框架不猜。

## 6. 配置与注册

**没有新增必填配置项**——0.12.0 已有组件的配置在 0.13.0 都保持可选并带默认值。
新增的是两处**启动期校验**：Redis 锁的租约与重试间隔、链路标识的请求头名，
越界值从"运行期静默异常"改成启动失败。原先配的是合法值就不受影响。

注册入口的变化：

| 0.12.0 | 0.13.0 |
| --- | --- |
| `AddAuditingCore()` | 已删除且 Core 包内无替代，改用 `Leistd.Auditing.EntityFrameworkCore` 的 `AddAuditingEfCore()` |
| `AddAuthorizationEfCore<T>()` / `ConfigureAuthorization()` | 名字未变，但现在多映射一张 `AuthorizationVersionRecord` 表（见上节） |
| `AddNotificationsEfCore<T>()` / `ConfigureNotifications()` | 名字未变 |

`Leistd.Authorization.EntityFrameworkCore` 的包依赖变了：新增 `Leistd.MultiTenancy.Core`、
`Leistd.UnitOfWork.EntityFrameworkCore`、`Leistd.DependencyInjection`。单租户宿主不必注册多租户，
`TenantId` 会一直是 `null`；但这几个包会进你的还原图，锁文件与许可清单要跟着更新。

## 7. 本地化资源键

宿主按键覆盖组件译文，因此键与占位符也是公共契约：键改了而覆盖没跟着改，表现为译文静默
回落到组件默认值，不报错。

本版有三条这样的变化：

- 权限未定义的译文占位符由 `{Name}` 改成 `{Names}`（一次可以报多个权限名）。
  覆盖过 `Permission:UndefinedPermission` 的宿主，句子里的变量要跟着改。
- `Permission:SubjectUnavailable` 整条移除（读自己的权限时空主体改为返回空集合，不再报错）。
- 权限与权限分组的显示名改为按约定键查找：权限为 `Permission:{权限名}`，分组为 `PermissionGroup:{组名}`，
  与设置组件的 `Setting:{名称}` / `SettingGroup:{分组}` 同一模式。定义里的 `displayName` 从"词条键"改为
  "默认文案"：查不到词条时直接展示它，而不是回落到技术名。原先把 `displayName` 写成词条键的宿主，
  改为写可读的默认文案（通常是英文），并把资源里的分组键改成 `PermissionGroup:{组名}`；权限键若本来就是
  `Permission:{权限名}` 则无需改名。

> `scripts/check-i18n-keys.ps1` 保证各语言的键集与占位符一致，但**不比对版本之间的增删**——
> 跨版本的键变化只能靠升级清单，所以上面这三条是人工列的。

## 8. 取包时的版本陷阱

nuget.org 上存在 `1.0.0-beta.22` 与 10 个 `1.0.0-preview.*`，SemVer 排序高于 `0.13.0-*`。
在它们被 unlist 之前，**预发布依赖必须写全称**（如 `0.13.0-beta.170`），不要用 `--prerelease`。
成因见 `versioning.md` 的「nuget.org 上存在版本号虚高的历史预发布包」。

## 9. 异常与失败响应收口

异常类不再携带 HTTP 语义。`BadRequestException`、`NotFoundException`、
`ConflictException`、`UnauthorizedException`、`ForbiddenException`、
`UnprocessableEntityException`、`UnsupportedMediaTypeException`、`InternalServerException`与
`ServiceUnavailableException` 已移除；`Leistd.Core.CommonException`、`GenericErrorCodes` 与旧的
`ValidationError` 也已移除。

迁移原则：

- 参数、对象状态、基础设施或传输失败优先改用 .NET 内置异常，如
  `ArgumentException`、`InvalidOperationException`、`HttpRequestException`。这些异常默认对外只产生通用 500，
  不会泄露内部消息。
- DTO 输入校验使用 DataAnnotations 与 `[ApiController]`；显式调用 `Validator`
  产生的 `System.ComponentModel.DataAnnotations.ValidationException` 也映射为 400。
- 可预期业务规则失败统一使用
  `new BusinessException("Order:NotFound", "The order was not found.")`。第二个参数必须是
  可安全展示的回退文案；需要本地化占位符时继续调用 `WithData`。
- `BusinessException` 未命中宿主映射时返回 400。422 只在宿主需要表达更精确的内容处理语义时按错误码显式映射。原来的
  `new NotFoundException(message).WithCode(code)` 改为 `new BusinessException(code, message)`，
  并在宿主注册时用 `options.MapCode(code, 404)` 映射传输状态；400、401、403 与 409 同理。
  `StatusCode`、`Details`、`WithCode`、`WithDetails` 不再是异常 API。

组件拥有的非默认 HTTP 语义由组件在自己的 `AddXxx` 里登记，**宿主不需要逐个调用**；
宿主的 `ApiExceptionMappings` 只列项目自己的错误码，以及需要覆盖组件默认值的那几条。
回退到 400 的错误码无需登记。业务项目将错误码常量放在实际拥有它的 Domain/Application 模块，而不是统一堆入 Shared。

`GlobalExceptionOptions` 在 `Leistd.ExceptionHandling.Options`，`ExceptionDescriptor` 在
`Leistd.ExceptionHandling.Descriptors`，两者都由 `Leistd.ExceptionHandling.Core` 提供——
它们只有状态码、错误码、文案与日志级别，与响应序列化形式无关，放在 Core 才能让组件
在自己的 Core 包里声明默认状态。各组件的默认映射随之成为 Core 包的内部实现，不再是公开类型，
`Leistd.Authorization.Resource.AspNetCore` 因此不再存在——它此前只装着那一个映射类，引用它的地方直接删掉。

> 已经按 0.13.0 的早期形态适配过的项目：这两个类型此前在 `Leistd.ExceptionHandling.AspNetCore.Options`
> 与 `.Descriptors`，只需改 `using`；各组件的映射类此前在各自的 `*.AspNetCore` 包，
> 删掉宿主里逐个调用组件 `*ExceptionMappings.Configure` 的那几行即可（它们已不再公开）。

默认失败响应改为 RFC 9457 Problem Details，其中稳定业务码在字符串 `code`
扩展字段。如宿主显式调用 `AddResponseWrapper()`，成功与失败都使用可选数字信封：
数字 HTTP/业务状态放在 `code`，稳定业务错误码放在 `errorCode`。
`BusinessMessageExposure` 已移除：业务异常的安全文案始终可展示，未预期异常始终回退为通用系统错误。

**启用开关已删除，行为有变化。** 0.12 的 `Leistd:GlobalException:Enable` 默认为 `false`：注册了处理器却没打开它时，处理器不接管异常。0.13 删掉了这个开关，只要注册 `AddGlobalExceptionHandler` 并挂上 `UseGlobalExceptionHandler` 就会接管——0.12 里注册了但没打开 `Enable` 的宿主，升级后异常响应会变成 Problem Details。旧的 `Enable` 配置键不再被读取，应从配置中删除；需要排查时不挂 `UseGlobalExceptionHandler` 即可。
异常响应的 `traceId` 与日志、响应头共享请求入口选定的关联标识；`UseCorrelationId()` 应在异常处理和日志中间件之前运行。
`X-Correlation-Id` 保持原有的不透明标识契约（最多 128 字符，只含 ASCII 字母、数字、`-`、`_`）；显式 `Change()` 仍优先于当前 `Activity`。W3C 追踪上下文使用 `traceparent`。

定制宿主对精确类型调用 `MapException<TException>()`（委托可按异常属性分支），需要完全接管某类异常时按 ASP.NET Core 方式再注册一个 `IExceptionHandler`；如要替换整个失败响应的序列化形状，注册 ASP.NET Core 的 `IProblemDetailsWriter`。
`ServiceClientException` 现在直接继承 `Exception`，`RemoteServiceException` 继承前者；注册任一服务客户端即登记安全的 500/502/503/504 分类。本地观测的无效响应、传输不可用、等待超时分别映射为 502、503、504；远端明确返回失败状态默认 502，不依据远端 408/429/503 等状态推断本地响应。宿主可针对已知上游契约覆盖默认映射。
组件用 `MapDefaultCode` / `MapDefaultException` 登记默认状态，
宿主 `MapCode` / `MapException` 始终可覆盖，调用顺序无关；HTTP 状态映射只在代码里声明，不提供配置文件入口。
可选数字信封统一使用 `Result`；若代码直接引用了 `ExceptionResult`，改为构造 `Result` 并填充
`Code`、`Message`、`TraceId`、`ErrorCode` 与可选 `Errors`。

框架判定的请求错误 `BadHttpRequestException`（Minimal API 请求体解析失败、请求体过大等）按异常自带的状态码返回，不再报 500。框架只写状态码、不写响应体的失败（生产环境的请求体解析失败、415、未匹配路由、认证质询、限流）默认没有响应体；需要标准响应体时，在 `UseGlobalExceptionHandler()` 之后用 `UseWhen` 把 ASP.NET Core 的 `UseStatusCodePages()` 限定到 API 路径。这类协议层失败只带状态码、本地化标题与 `traceId`，不合成业务错误码。

所有失败响应统一经 ASP.NET Core `IProblemDetailsService` 写出，定制只走 `ProblemDetailsOptions.CustomizeProblemDetails`：

- 只有 `BusinessException` 带 `code` 扩展与 `detail`；输入校验、未预期异常和上游故障不再返回 `Error:*` 码，依据 HTTP 状态分支。`Error:*` 词条与 `ProblemTypes.SystemError` 已删除。
- 启用 `AddResponseWrapper()` 时，MVC 的 `NotFound()`、`Problem()` 等错误结果与状态码页同样输出信封。
- `ServiceClientOptions.Timeout`（配置键 `Leistd:ServiceClients:<名称>:Timeout`）已删除，组件不再设置 `HttpClient.Timeout`，它回到 .NET 默认的 100 秒作外层兜底。超时改由宿主在客户端上叠加弹性管道（如 `AddStandardResilienceHandler()`），其超时归类为 `ServiceClientFailureKind.Timeout` 并在 API 边界返回 504；确需改兜底时长时用 `ConfigureHttpClient`，并保持它大于管道的总超时。
- `GlobalExceptionOptions.ExcludePatterns`（配置键 `Leistd:GlobalException:ExcludePatterns`）已删除：命中路径只会丢掉业务映射、仍返回同一种问题详情，健康检查也不需要它（检查项异常由健康检查服务自行捕获）。个别路径需要其他错误格式时，在 `AddGlobalExceptionHandler` 之前注册自己的 `IExceptionHandler`，按路径判断后返回 `true`。

### 失败响应的字段变化（前端要改的地方）

失败响应的形状变了，字段名不是重命名那么简单：`message`/`details` 是本框架自定的扩展字段，
`detail`/`errors` 是 RFC 9457 的标准字段。前端按旧名读会拿到 `undefined`，
而 `undefined` 在界面上通常表现为"错误提示是空的"，不是报错——**这条漏改是静默的**。

| 旧（自定信封） | 新（Problem Details） | 说明 |
| --- | --- | --- |
| `message` | `detail` | 可展示的错误文案。只有 `BusinessException` 有；协议层失败为空，改读 `title` |
| `details` | `errors` | 字段级错误数组，元素形状不变（`detail` / `field` / `code`） |
| `code` | `code`（扩展字段） | **不变**，仍是稳定业务错误码。前端的分支逻辑不用动 |
| — | `title` | 新增：按状态码本地化的标题。没有 `detail` 时用它做兜底文案 |
| — | `status` / `type` / `instance` | 新增：RFC 9457 标准字段 |
| `traceId` | `traceId`（扩展字段） | 不变 |

**启用了 `AddResponseWrapper()` 的宿主不受影响**：数字信封的 `message` 保持原样，
它的 `errorCode` 承载业务码。这一节只针对默认（不套信封）的 Problem Details 形态。

前端的改法是一处收口，不要散在各个请求里：解析错误响应的那一个函数按
`detail ?? title` 取文案、按 `errors` 取字段错误、按 `code` 分支。
