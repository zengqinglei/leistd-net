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

### 包依赖的变化怎么核对

本版有若干包新增了直接依赖（例如远端租户连接解析为路由缓存引入了
`Microsoft.Extensions.Caching.Hybrid`）。**这里不列清单**：人工抄的清单会错，也会漏。
请按打出来的 `nuspec` 逐包对比——那才是真正进入消费者依赖图的东西：

```bash
# 两个版本各解开一份，比较 <dependencies> 节
unzip -p Leistd.MultiTenancy.Core.0.12.0.nupkg '*.nuspec' > old.nuspec
unzip -p Leistd.MultiTenancy.Core.0.13.0.nupkg '*.nuspec' > new.nuspec

# ① 直接依赖的包名增删
diff <(grep -o 'id="[^"]*"' old.nuspec) <(grep -o 'id="[^"]*"' new.nuspec)

# ② 版本约束与目标框架分组也变了的话，比整节
diff <(sed -n '/<dependencies>/,/<\/dependencies>/p' old.nuspec) \
     <(sed -n '/<dependencies>/,/<\/dependencies>/p' new.nuspec)
```

① 只看**直接依赖的包名增删**，版本约束升降与 `targetFramework` 分组变化它看不出来，那些要用 ②。

内部的项目引用变动不必逐条关心——只有出现在 `nuspec` 里的才会传递给消费者。
在架构门禁里限制"应用层/领域层能引用什么"的项目，按这份对比结果更新白名单；
框架侧的 Core 包一律只依赖抽象（`Microsoft.Extensions.*` 与 `*.Abstractions`），
见 `docs/architecture/design-principles.md` §1.1。

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
它由 `ConfigurePermissionAuthorization()` 一并映射，**不是可选项**：权限授予的乐观并发靠它收口。

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
| `AddAuthorizationEfCore<T>()` / `ConfigureAuthorization()` | 改名为 `AddPermissionAuthorizationEfCore<T>()` / `ConfigurePermissionAuthorization()`（避免与 ASP.NET Core 的 `AddAuthorization` 混淆），并多映射一张 `AuthorizationVersionRecord` 表（见上节） |
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

`GlobalExceptionOptions.Enabled` 已删除。调用 `AddGlobalExceptionHandler()` 本身就是启用意图，
留一个默认为 `true` 的开关只是给"注册了却不生效"留了一条无声的路。临时关掉全局异常处理时不注册它即可。

`OperationFailure.FromCode(string code, string dataJson)` 与 `Create(string, string, string)` 的
第二个入参由 JSON 字符串改为 `IReadOnlyDictionary<string, object?>`，**JSON 由组件序列化**：

```csharp
// 0.13.0 早期形态
OperationFailure.FromCode(code, JsonSerializer.Serialize(new { Limit = limit }));
// 现在
OperationFailure.FromCode(code, new Dictionary<string, object?> { ["Limit"] = limit });
// 已有 BusinessException 时，两个组件经 BCL 字典对接，不必互相引用
OperationFailure.FromCode(exception.Code, exception.LocalizationData);
```

手拼 JSON 串没有转义，值里出现引号就产出坏 JSON、展示端整行原因渲染不出来。值只能是标量
（字符串、布尔、数值、日期、`Guid`）；传对象不会被摊开，只会写下类型名——这一列租户管理员
直接可读、还会进导出。漏改是编译错误，不会静默。

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

## 10. 默认配置下静默出错的收口

以下改动把"配置不当也照常运行、结果却是错的"改成启动失败或行为纠正。

| 变化 | 影响与改法 |
| --- | --- |
| `BaseDbContext(DbContextOptions)` 单参构造已删除 | 派生上下文改用 `(options, IServiceProvider? serviceProvider)`；运行时必须传作用域容器，只有设计时工厂传 `null`。单参构造曾让租户过滤退化为宿主视角、审计与运行时开关失效 |
| 本地事件收集不再按实体状态过滤 | 聚合根保存时为 `Unchanged`（只改了子实体或只登记了事件）时，它登记的事件现在会发布；此前被静默清掉 |
| 工作单元要求代理工厂 | `AddUnitOfWork()` 注册启动检查：宿主未接 `DynamicProxyServiceRegistrationCallbackFactory` 时启动失败。`WebApplicationBuilder` 用 `builder.Host.UseServiceProviderFactory(...)`，`HostApplicationBuilder` 用 `builder.ConfigureContainer(...)`。此前漏接时 `[UnitOfWork]` 不生效、事件处理器在提交前后各执行一次 |
| `ServiceRegistrationCallbackFactory.CreateBuilder` 改为 `virtual` | 派生工厂可覆写；`DynamicProxyServiceRegistrationCallbackFactory` 在此登记 `DynamicProxyWeavingMarker` |
| `AddRemoteTenantConnectionStore` 启动时校验控制面地址 | `Leistd:ServiceClients:{serviceName}:BaseAddress` 缺失或不是绝对地址时启动失败并报出键名 |
| 租户会话自恢复按主体的租户 claim 判定 | 改过租户 claim 名的宿主，自恢复现在才会生效；claim 类型的唯一配置处见第 11 节 `ClaimTypeOptions` |
| 无当前用户时 `LastModifierId` 置空 | 后台作业、机器主体的修改不再沿用上一次的修改人 |
| `AddAuthorizationEfCore` / `ConfigureAuthorization` 改名 | 见第 6 节 |
| `ITenantManager` / `ITenantManagementService` 的 `CreateAsync(..., Guid id)` 重载已删除 | 创建租户一律由组件生成标识；自定义 `ITenantManager` 实现删掉该重载即可 |
| `ControllerExtensions.FailResult` / `FailResultWithErrors` 与 `ErrorResult` 已删除 | 失败一律抛业务异常，由异常处理管道写出；启用 `AddResponseWrapper()` 时同样得到带 `traceId` 与 `errorCode` 的信封。仍需在代码里构造信封时用 `Result.Fail(code, message) with { Errors = … }` |
| 服务客户端不再把非 2xx 信封的数字 `code` 当作业务码 | 那是 HTTP 状态；业务码从 `errorCode`（或 Problem Details 的字符串 `code`）还原。旧式"非 2xx + 数字业务码"的远端不再得到 `ErrorCode`。2xx 信封的非零 `code` 仍按业务码处理 |
| `AddDddInfrastructure()` 注册本地事件总线 | 宿主不必再单独调用 `AddLocalEventBus()`（重复调用无害，注册已幂等）。`LocalEventSaveChangesInterceptor` 的两个构造参数改为必需；此前缺事件总线时解析 DbContext 抛异常 |
| `AddInterceptor` 按类型去重 | 组件的 `AddXxx` 被调用两次时，同一拦截器不再被织入两层 |
| `ILock` 不再注册为服务；内存锁与 Redis 锁可共存 | 注入 `ILock` 的代码改注入 `IDistributedLock` 或 `ILocalLock`。`AddRedisDistributedLock` 现在替换 `AddMemoryLocalLock` 的 `IDistributedLock` 兜底（与顺序无关），此前两者同时注册时总是内存锁生效；宿主自行注册的 `IDistributedLock` 不被覆盖 |
| 登记了周期任务却没有调度器时启动告警 | `AddRecurringJob` 注册启动检查；调度器实现登记 `RecurringJobSchedulerMarker`（`AddInProcessBackgroundJobs()` 已登记）。自研调度器需同样登记该标记，否则会收到这条 Warning |
| `MapRealTimeHub` / `MapNotificationHub` 返回 `HubEndpointConventionBuilder` | 可继续链式追加授权策略、CORS 等约定；此前返回 `IEndpointRouteBuilder`。`MapRealTimeHub(pattern = "/hubs/realtime")` 接收路径参数 |
| `RealTimeOptions` 与 `AddRealTimeSignalR(configure)` 的参数已删除 | 业务事件 Hub 路径改在 `MapRealTimeHub(pattern)` 给出 |
| 邮件通知改为纯文本并附链接 | `IsBodyHtml = false`，正文不再做 HTML 编码；`Link` 为绝对 http(s) 地址时附在正文末尾 |
| 宿主级组件的注册入口统一为 `AddX(configure?, configSectionPath?)` | `AddUnitOfWork`、`AddMultiTenancy`、`AddGlobalExceptionHandler`、`AddSmtpEmailSender`、`AddServiceUserContext`、`AddRedisDistributedLock(connectionString, …)` 的 `IConfiguration` 重载已删除：统一从容器里的 `IConfiguration` 绑定默认配置节，委托在绑定之后应用（代码覆盖配置）。调用处去掉 `builder.Configuration` 实参即可；配置节不在默认路径时传 `configSectionPath`。此前委托重载不绑定配置节，写在 appsettings 里的值静默不生效。多租户与工作单元注册类上的 `ConfigurationSection` 常量已删除，改用 `MultiTenancyOptions.SectionName`、`UnitOfWorkOptions.SectionName`；`AddDddInfrastructure(configure)` 经此同样绑定 `Leistd:UnitOfWork`；无主机的 `ServiceCollection` 需自行注册 `IConfiguration` |
| `AddDddInterceptors` 已删除，由 `AddDddDbContext<T>()` 挂载保存拦截器 | 删掉 `AddDbContext` 回调里的 `options.AddDddInterceptors(sp)`；派生自 `BaseDbContext` 的上下文登记时自动挂上审计、领域事件与并发标记三个拦截器，与注册先后无关。其他上下文不挂载。此前漏挂时这三项静默失效 |
| `ConfigureByConvention()` 已删除，改为 EF Core 约定 `DddEntityConvention` | 删掉实体配置里的 `b.ConfigureByConvention()`：`BaseDbContext` 自动注册该约定，对所有实现审计或并发标记契约的实体生效（此前只作用于调用了它的实体），显式 Fluent 配置优先。`BaseDbContext.ConfigureConventions` 已封闭，原覆写改为 `ConfigureModelConventions`（无需调 `base`）。不继承基类的上下文如需同一约定，自行 `configurationBuilder.Conventions.Add(_ => new DddEntityConvention())`。**会改变模型，两处**：① 此前未调用 `ConfigureByConvention` 的审计实体，审计人列（`CreatorId` / `LastModifierId` / `DeleterId`）列长变为 64；② **`ConcurrencyStamp` 变为限长 40、必填、并作为并发令牌**——这一项此前未写，未 opt-in 的实体都需要迁移。升级后按 ddd-struct 文档"迁移快照检查"一节确认并生成迁移；**接入该检查后它会报出这处模型差异**——没接入的项目仍要自己核对。旧库里若有该列为空或超过 40 字符的行（来自绕过保存拦截器的写入——手写 SQL、批量导入，或 EF 自己的 `ExecuteUpdate`/`ExecuteDelete`；走 `SaveChanges` 的拦截器一直写 32 位值），迁移会失败：先把这些行补齐或截断，再执行。表现是会报错的迁移失败，不是数据错误 |
| `TenantRouting:CacheLifetime` 不再必填 | `TenantRouteCacheOptions.CacheLifetime` 改为非空 `TimeSpan`，默认 10 分钟；仍校验大于 0 且不超过 1 小时 |
| 客户端取消的判断移出异常组件 | 官方 `ExceptionHandlerMiddleware`（.NET 8+）在调用处理器之前直接返回 499，行为不变 |

## 11. 身份口径

用户标识、角色与权限主体的读取统一到官方语义。

| 变化 | 影响与改法 |
| --- | --- |
| `ICurrentUser` 删除 `GetRoles()`、`IsInRole()`、`FindClaims()`、`GetAllClaims()`、`PhoneNumber` | 角色判断改用官方 `ClaimsPrincipal.IsInRole` 或授权策略 `RequireRole`；其他 claim 用 `FindClaim` 或直接读 `ClaimsPrincipal`。**比较方式不同**：原 `IsInRole` 同时认 `role` 与 `ClaimTypes.Role`、角色名不区分大小写；官方只认身份的 `RoleClaimType`、区分大小写。删除后若调用落到 `ClaimsPrincipal.IsInRole`，编译照样通过而结果可能变化，逐处核对角色名大小写与 claim 类型 |
| 模板会话 Cookie 的身份按 `role` 设 RoleClaimType | 已派生项目在 `SessionSignInService` 构造身份处改为 `new ClaimsIdentity(scheme, ClaimTypes.Name, "role")`。此前默认 RoleClaimType 是 `ClaimTypes.Role` 而角色写成 `role`，官方 `IsInRole` / `RequireRole` 静默判否；旧 Cookie 在重新登录后生效 |
| 主体标识与租户的 claim 类型收归 `ClaimTypeOptions`（Security.Core） | 唯一配置处：`UserIds`（默认 `sub` → `NameIdentifier`）与 `TenantId`（默认 `tenant_id`），经 `services.Configure<ClaimTypeOptions>(...)` 设置；读取规则 `FindUserId` / `ReadTenant` 也在这里。**带 `ValidateOnStart`**：选项配错（如某项配成空集合）会在启动时失败，而不是等到第一次读 claim；默认配置总是通过。**删除**：`MultiTenancyOptions.TenantClaimType`、`HubIdentityOptions.UserIdClaimTypes`、`OperationRecordOptions.ActorIdClaimType`（配置节里的对应键一并删掉，改到 `ClaimTypeOptions`）。`ICurrentUser` 新增 `SubjectId`（主体标识原始值）；`CurrentUser` 构造函数新增 `IOptions<ClaimTypeOptions>` 参数。自行签发主体的代码（登录、令牌、服务间还原）写 claim 时用同一选项的类型。模板签发令牌时始终写协议要求的 `sub`，`UserIds` 不含 `sub` 时按 `UserIds[0]` 同值再写一条（`SubjectClaims.Set`），机器令牌同理；改了 `TenantId` 的项目同步改前端 `tenant-protocol.ts` 的 `TENANT_CLAIM`，否则前端读不到租户、把租户用户当成宿主 |
| 操作人标识改读 `ICurrentUser.SubjectId` | 此前默认读 `sub`、缺失时回落 `ICurrentUser.Id?.ToString()`；现在一律记按 `ClaimTypeOptions.UserIds` 读到的原始值。差别：只有非 GUID 的 `NameIdentifier` 时此前记 `null`、现在记原值；GUID 若非标准 `D` 格式此前被规范化、现在保持原样；只实现 `Id` 而不提供对应 claim 的自定义 `ICurrentUser` 不再有回落。"本人可见"按操作人标识判定，有历史数据时核对新旧标识格式 |
| `IPermissionSubjectProvider` 新增 `GetSubjectAsync(ClaimsPrincipal, CancellationToken)` | 自定义实现须补上：只按传入主体的声明解析，与 `GetCurrentSubjectAsync` 同一口径（后者可直接转调前者或共用私有方法）。模板实现见 `PermissionSubjectProvider` |
| 权限策略按被授权的主体判定 | `PermissionAuthorizationHandler` 改为评估 `AuthorizationHandlerContext.User`，此前总是判当前用户——经 `IAuthorizationService` 为别的主体判权时得到的是当前用户的结果。`IPermissionChecker` 新增带 `ClaimsPrincipal` 的两个重载：非当前主体不走作用域快照，其租户 claim 与当前租户不一致时拒绝；当前主体的租户 claim 非法（`ClaimTypeOptions.ReadTenant`，如两份用户凭据被合并成一个主体）时同样拒绝，但不要求等于当前租户，宿主主体显式切入租户照常判定；未接多租户时两条路径都只校验合法性。自定义 `IPermissionChecker` 实现须补这两个重载 |
| 租户 claim 非法一律失败关闭；用户标识与租户取自同一个身份 | 规则：用户标识与租户取自同一个身份——按顺序第一个带用户标识的身份（主体身份）；同一身份内多条（即使值相同）或非 GUID 为非法；其他带用户标识的身份带着与主体身份不同的租户（含主体身份为宿主）为非法，这样同一请求携带的两份用户凭据拼不出"甲的标识 + 乙的租户"；不带用户标识的身份（服务间调用只委托租户时还原出的身份）只在主体身份没有租户时提供租户；带用户标识而无租户 claim 的其他身份（如服务间调用方的机器身份）不参与判定。`FindUserId` 随之改为在主体身份上取值，此前按 claim 类型跨全部身份取第一个；新增 `FindSubjectIdentity` 公开主体身份。`ICurrentUser` 的 `Username`、`Name`、`Email` 同样改为只在主体身份上读取（没有带标识的身份时仍按整个主体），此前服务间还原时可能取到调用方机器令牌上的名字，操作记录的操作人名快照随之取错；`FindClaim` 仍跨全部身份。`UseTenantSessionRecovery` 改按 `ReadTenant` 判定租户会话，租户声明非法时保留原始错误、不再注销。`AmbiguousTenantClaimException` 改名 `InvalidTenantClaimException`（错误码 `Tenant:AmbiguousClaim` → `Tenant:InvalidClaim`，宿主覆盖过译文的同步改键），此前开启注册表校验时非 GUID 的 claim 会被当成租户名去查。`ICurrentUser.TenantId` 遇非法 claim 抛 `InvalidOperationException`，此前静默当作宿主。**异常类型也变了**：`InvalidTenantClaimException` 继承 `BusinessException`（错误码 `Tenant:InvalidClaim`），而 Hub 与后台作业里此前抛的是 `InvalidOperationException`——按前者类型捕获的代码要改，把非法租户声明当基础设施故障处理的重试/告警逻辑也要跟着调 |
| 匿名租户提示头默认改名 `X-Tenant` | `MultiTenancyOptions.HeaderName` 默认 `X-Tenant`（`DefaultHeaderName`），值可为租户 Id 或名称；与服务间委托头 `X-Tenant-Id`（只带 GUID、须受信调用方）分开。前端或网关按旧名发租户头的同步改名，或把 `HeaderName` 配回旧值 |
| 服务客户端转发头名可配置 | `UserContextForwardingOptions` 新增 `UserIdHeader`、`UsernameHeader`、`TenantIdHeader`，默认值与被调方 `ServiceUserContextOptions` 同源；此前调用方写死常量，只改被调方会静默丢失转发。`AddRemoteTenantConnectionStore` 的客户端固定不转发用户与租户上下文 |
| 服务间还原写入配置的 claim 类型，并取代调用方的租户 claim | 被调方还原用户标识写 `ClaimTypeOptions.UserIds[0]`、租户写 `ClaimTypeOptions.TenantId`；调用方身份若自带租户 claim，还原时以委托的租户为准（此前两条并存） |
| 租户切换自动写入日志作用域 | `ICurrentTenant.Change` 同时打开日志作用域 `TenantLogKeys.TenantId`（`leistd.tenantId`，宿主为 `null`）；多租户中间件不再单独开作用域。模板在 `UseMultiTenancy()` 之后把租户写入 Serilog 诊断上下文，请求完成日志也带租户 |
| 操作记录的 `ActorTenantId` 改为操作人所属租户；匿名自证动作记下操作人；"本人"按标识与所属租户认定 | `ActorTenantId` 取自主体的租户 claim（匿名请求取请求所在的租户上下文），此前是操作发生时的上下文租户：宿主管理员进入租户操作（模拟登录、代管）时此前记成该租户。定义为 `targetIsActor` 的动作在匿名请求里记录时，`ActorId` 取目标：此前为空，租户用户看不到自己的登录、注册记录（`Actor` 层只对本人放行）。`OperationRecordVisibilityScope.ForTenantReader` 新增 `actorTenantId` 参数，`Actor` 层要求 `ActorId` 与 `ActorTenantId` 都相同：主体标识只在签发它的那一层内唯一，宿主主体在租户里留下的记录不再被租户里同标识的主体认领。`ActorIsTarget` 只在 `ActorId == TargetId` 时成立。**已有数据须迁移**，否则此前的自证记录仍不属于任何人：`UPDATE <schema>."OperationRecords" SET "ActorId" = "TargetId" WHERE "ActorId" IS NULL AND "Outcome" = 'Succeeded' AND "Action" IN (<各 targetIsActor 动作码>);`（模板为 `'auth.login.succeeded'`、`'auth.password.changed'`、`'auth.registered'`；归档表同样处理）。此前宿主主体在租户里留下的记录 `ActorTenantId` 已记成该租户，无从还原 |
| 设置存储宿主键 `h:` 改为 `host:` | `EfCoreSettingStore` 的 `ScopeKey` 与其他按租户隔离的键统一经 `CurrentTenantKeyExtensions.ScopeKey`：宿主行 `h:t` → `host:t`，租户行不变。**已有数据须迁移，且必须在新版本开始服务之前执行**：新版本读不到旧键时不报错，宿主级设置静默回落为定义里的默认值。语句为 `UPDATE <schema>."SettingRecords" SET "ScopeKey" = 'host' \|\| substr("ScopeKey", 2) WHERE "ScopeKey" LIKE 'h:%';`（按实际表名与数据库方言调整）；随 EF 迁移发布时新建一个空迁移，在 `Up` 里 `migrationBuilder.Sql(...)` 执行它，`Down` 反向执行 `'h' \|\| substr("ScopeKey", 5) WHERE "ScopeKey" LIKE 'host:%'`，由部署时的迁移步骤保证先于新版本生效 |
| `by-host` 探测跑宿主配置的解析链 | 此前固定 new 一个 `DomainTenantResolveContributor`，宿主替换或定制域名解析时探测与真实请求答案不一致。**注意答案不再只由主机名决定**：跑的是完整解析链，所以请求若带着会话或租户提示（Cookie、`X-Tenant-Id` 等），答案会随之变化——这与真实请求一致，是有意的；但把它当成『纯按域名查租户』的接口去用会得到意外结果，它的设计场景是登录页在未登录、未选租户时调用 |
| 模板：令牌端点与 userinfo 在令牌主体的租户内加载用户 | `IAuthPrincipalFactory` 新增 `CreateFromTokenAsync(tokenPrincipal, scopes)`，`CreateUserInfoAsync` 改为只收令牌主体。此前这两个端点的请求解析出的是宿主，**租户用户走不通授权码换令牌、刷新与 userinfo**；已派生项目按模板同步 |
| 模板：Resource 形态默认授权策略要求自然人 | 派生项目用纯机器令牌调用默认策略端点时，响应从 200 变为 403；给纯机器端点显式声明机器策略，不放宽默认自然人策略 |
| 模板：租户相关的缓存与状态键按租户隔离 | 登录失败计数、邮箱验证码限流与挑战、外部登录 state 统一经 `ScopeKey`；外部登录 state 绑定发起时的租户，回调时租户不一致即拒绝 |
| 模板前端：租户键集中到 `tenant-protocol.ts` | `TENANT_HEADER`（`X-Tenant`）、`TENANT_INVALID_HEADER`、`TENANT_CLAIM` 三个常量取代散落的字面量；Resource 形态接受没有租户 claim 的宿主用户 |

## 12. 通知推送与本机开发

| 变化 | 影响与改法 |
| --- | --- |
| 通知客户端方法名改为 `Notifications.Received` | 公开常量 `NotificationClientMethods.Received`；此前为 `NotificationReceived`。前端 `connection.on(...)` 同步改名，旧名不再推送 |
| 新增 `AddNotificationsSignalR<THub>()` | 通知可经宿主指定的 Hub 推送，与业务实时事件共用一条连接（如 `AddNotificationsSignalR<RealTimeHub>()` + 只映射 `MapRealTimeHub()`）。无泛型版本等价于 `<NotificationHub>`；同一 Hub 重复注册幂等，已选定一个 Hub 后再指定另一个在注册时抛 `InvalidOperationException` |
| `AddRemoteTenantConnectionResolution()` / `AddLocalTenantConnectionResolution<T>()` 自带多租户核心服务 | 两者调用 `AddMultiTenancyCore()`（TryAdd，宿主先注册的替换实现照旧保留）。此前单独使用（如只注册持久化的迁移作业）缺 `ICurrentTenant`，开发环境在容器构建期失败；宿主已显式调用 `AddMultiTenancyCore()` 的无需改动 |
| `SignalRNotificationChannel` 改为 internal | 宿主经 `INotificationChannel` 使用；需要定制推送时实现自己的渠道 |
| `AddEmailNotifications(configure?, configSectionPath?)` 绑定 `Leistd:Notifications:Email` | 新增可选 `PublicBaseUrl`：配置后，以 `/` 开头的站内链接拼成绝对地址附进邮件（此前相对链接一律不附）；配置值不是绝对 http(s) 地址时启动失败。无主机的 `ServiceCollection` 需自行注册 `IConfiguration` |
| 模板：默认管理员口令只在首次创建时校验 | 删除 `DefaultAdmin:Password` 的启动期校验与 `DefaultAdminOptions.IsPasswordUsable`；库里没有超级管理员、需要创建时才校验并报出键名。`appsettings.Development.json` 带公开的本机演示口令；compose 不再以 `:?` 强制 `DEFAULT_ADMIN_PASSWORD` |
| 模板前端：本机开发配置 | `npm start` 即 `ng serve`（构建配置 `debug` 改名 `development`）；`environment.ts` 即本机配置，不再需要复制 `environment.debug.ts`。Mock 提供器默认空实现，只有 `development` 构建替换为 `providers.mock.ts`，其他构建不含 Mock；Mock 判定统一为 `_mock/core/matching.ts` |
| 模板：开发代理改为 Angular `proxyConfig` | 删除后端 `SpaProxy` 选项与实现；前端 `proxy.conf.mjs` 转发 `/api`、`/hubs`（WebSocket）以及身份服务的 `/connect`、`/.well-known`，浏览器访问 `http://localhost:4200`。后端不在默认端口时设置 `API_PROXY_TARGET` |
| 模板：删除 `/uploads` 静态目录 | 没有写入方；compose 的 `feedback-uploads` 卷一并删除 |
| 模板：删除 `Cors:AllowAnyLocalhost` | 它只服务于本机跨域联调，已被开发代理取代；前端部署在另一个源时仍用 `Cors:AllowedOrigins` |
| 模板：通知与业务事件共用实时 Hub | `AddNotificationsSignalR<RealTimeHub>()`，只映射 `MapRealTimeHub()`；前端只建一条 `/hubs/realtime` 连接 |
| 模板前端：绝对地址的请求不再带凭据 | URL 格式化拦截器只给本服务的相对地址加网关前缀并带 Cookie；本来就是绝对地址的请求（OIDC 签发方的发现文档、JWKS、令牌端点）原样放行。此前一律 `withCredentials`，签发方在另一个源时带凭据的跨源请求被浏览器拦下，Resource 前端无法发起登录 |
| 模板前端（身份服务）：登录后回到授权端点用整页跳转 | 未登录的授权请求被送到登录页，`returnUrl` 以 `/connect/` 开头时登录后整页跳转；此前按前端路由处理，落到首页、授权流程中断 |
| 模板前端（Resource）：OIDC 回调后的导航归回调组件 | `provideAuth` 打开 `triggerAuthorizationResultEvent`；此前库在回调后自行跳到 `postLoginRoute`（默认 `/`），与回到登录前地址的导航竞争。首页的登录入口改为进入工作台由守卫发起登录，不再链到本形态不存在的 `/auth/login`、`/auth/register` |
| 模板（身份服务）：为下游 API 签发令牌 | 新增 `OAuth:ApiResources`：列出由本服务签发令牌的下游 API，各登记为同名 scope。scope 目录 `OAuthScopes` 成为服务端登记、scope 表、开放应用权限校验与令牌受众的唯一来源；访问令牌的受众由授予的 scope 推出（此前一律是本服务的 `OAuth:Resource`），本服务 API 只接受受众是自己的令牌。**已有客户端要调用本服务 API，须补授 `scp:<OAuth:Resource>` 并在取令牌时申请它**；服务间调用方的 `Leistd:ServiceClients:<服务>:Scope` 同时写目标 API 的 scope 与 `svc.delegate`（空格分隔），委托 scope 改为仅限机器客户端。开放应用新增 `GET /api/v1/open-applications/scopes`，编辑界面的 scope 选项取自它。**启动时按目录对账 scope 表：新建、更新，并删除目录之外的全部 scope**（不只是曾由 `ApiResources` 登记的）；派生项目在库里单独登记过的自定义 scope，升级前须改由目录提供（`OAuth:ApiResources` 或 `OAuthScopes`）。删除登记不撤销已签发的访问令牌，它们照常用到过期 |
| 模板（Resource）：`Authentication:Audience` 默认改为 `companyname-projectname-api` | 与前端申请的 scope 同名，须在身份服务的 `OAuth:ApiResources` 登记；compose 不再要求 `RESOURCE_AUDIENCE` |
| 模板：会话时长改为 `SessionCookie:ExpireDays` | 取代 `OAuth:CookieExpireDays`；不带授权服务器的形态此前写死 7 天，现在同样可配。`OAuth` 节只在带授权服务器的形态生成，`Authentication` 节只在 Resource 形态生成 |
| 模板：迁移作业只注册持久化 | Infrastructure 拆出 `AddPersistenceServices`（数据库上下文、租户连接解析、多租户控制库），`AddInfrastructureServices` 在它之上加运行期组件与外部适配器；DbMigrator 只调用前者。此前迁移作业注册了全部运行期组件，它们依赖只在 API 注册的当前用户与权限主体，开发环境下迁移在容器构建期失败 |

## 13. 官方机制替换与清理

| 变化 | 影响与改法 |
| --- | --- |
| 新包 `Leistd.MultiTenancy.Management`：租户管理用例移出 Core | `ITenantManagementService`、`ITenantConnectionManagementService`、`AddTenantManagement()`、管理用 DTO（命名空间 `Leistd.MultiTenancy.Management.Dtos`）、开通契约 `ITenantProvisioner` / `ITenantActivationGuard` / `ITenantDatabaseErrorDescriber`（`…Management.Provisioning`）、事件 `TenantChangedEvent` / `TenantConnectionChangedEvent`（`…Management.Events`）移入新包。资源服务回源用的线上 DTO（运行时连接、迁移连接、库清单）留在 Core。`MultiTenancy.Core` 不再依赖工作单元与事件总线 |
| `AddMultiTenancyEfCore<T>()` 不再注册管理用例 | 只注册存储。提供租户管理界面的宿主另行 `AddTenantManagement()`（引用 Management 包）；只读控制库的宿主（租户连接解析、迁移作业）不需要 |
| 关联标识：`[CorrelationId]` 特性与拦截器删除 | 没有使用方；非 HTTP 入口用 `IAmbientContext.Begin(principal, correlationId)` 建立作用域，进程内后台队列自动捕获还原。`Leistd.Tracing.Core` 不再依赖动态代理 |
| `ICorrelationIdProvider.Create()` 删除 | 需要新值时用 `ActivityTraceId.CreateRandom().ToHexString()` 再 `Change(...)` |
| `AddCorrelationIdCore` / `AddCorrelationId` 改为 `(configure?, configSectionPath?)` | 与其他宿主级组件一致：先绑定 `Leistd:CorrelationId` 再应用委托；无主机的 `ServiceCollection` 需注册 `IConfiguration`。原 `AddCorrelationId(builder.Configuration)` 改为 `AddCorrelationId()` |
| `CorrelationIdOptions`：`HeaderNames` → `HeaderName`，`IncludeInResponseHeaders` → `SetResponseHeader` | 单个请求头名；空白时启动失败并报 `Leistd:CorrelationId:HeaderName`。`Enabled` 保留，全局关闭 HTTP 传播（用于不采信外部请求头的边缘服务）；只是不想向某个第三方转发，不给那个客户端挂转发处理器即可 |
| 入站关联标识优先于 Activity | 合法的入站 `X-Correlation-Id`（不超过 64 字符，只含字母、数字、`-`、`_`）优先；没有时取 `Activity.TraceId`，再没有则新建。此前有 Activity 时入站值被丢弃，出站转发到下游后关联断开。超过 64 的值此前接受、落库截断，现在丢弃 |
| 日志键改为 `leistd.correlationId` | `CorrelationIdConstants.TraceIdLogKey`（`leistd.correlationId.traceId`）与 `InboundTraceIdLogKey` 删除，改 `LogKey`。日志检索按新键；TraceId 由日志库按 Activity 另行记录 |
| 错误响应的 `traceId` 改为官方链路标识 | 中间件不再改写 `HttpContext.TraceIdentifier`，`RequestTraceId` 删除。问题详情的 `traceId` 为当前 `Activity.Id`（W3C `00-<TraceId>-<SpanId>-<flags>`，取第二段检索），没有 Activity 时为请求标识；异常日志记同一值。关联标识在响应头 `X-Correlation-Id`。前端只展示该值的无需改动 |
| 后台队列工作项接上入队时的链路与关联标识 | 工作项在自己的 `Activity` 与还原后的环境上下文中执行，失败日志也在其中（此前在上下文释放后才记，带不上入队时的主体与关联标识）；父链路为入队时的 Activity，没有则为新的根链路。非 HTTP 入口还原关联标识时同时打开日志作用域 `leistd.correlationId`。OpenTelemetry 订阅 `ActivitySource` `Leistd.BackgroundJobs` 即可导出 |
| 远端路由缓存改用 `HybridCache` | `TenantRouteResolutionCoordinator` 删除；`AddRemoteTenantConnectionResolution()` 调用 `AddHybridCache()`，只用进程内一级（连接串不进分布式缓存）。取消语义随官方：单个等待者取消只影响自己，全部取消时回源取消；失败不缓存 |
| 服务客户端不再写调用日志 | `ServiceClientLoggingHandler` 改为内部的传输异常统一处理器，`ServiceClientOptions.LogPayloads` / `MaxPayloadLength` 与 `LoggerCategoryPrefix` 删除。调用摘要看 `System.Net.Http.HttpClient.<客户端名>` 的官方日志；要记正文用 `AddExtendedHttpClientLogging`（`Microsoft.Extensions.Http.Diagnostics`，需 `AddRedaction()`），示例见服务客户端文档 |
| 服务客户端识别官方字典形 `errors` | `RemoteServiceException.Errors` 同时还原 Leistd 数组与 `HttpValidationProblemDetails` 字典（键为属性名，每条消息一项，`Code` 为空）。此前字典形被忽略 |
| `MapsterProfile` 与 `AddProfiles` 删除，映射配置改用 Mapster 官方 `IRegister` | `class XxxProfile : MapsterProfile` → `class XxxMappings : IRegister`，`ConfigureMappings()` → `Register(TypeAdapterConfig config)`，`CreateMap<A, B>()` → `config.NewConfig<A, B>()`；注册处 `options.AddProfiles(asm)` → `options.Configurators.Add(config => config.Scan(asm))`。配置里的嵌套映射不要调用无参 `Adapt<T>()`（它用全局配置，登记的规则静默失效），直接映射源对象或集合 |
| 资源实例授权改走官方授权管线；新包 `Leistd.Authorization.Resource.AspNetCore` | 删除 `IResourceAuthorizationHandler<T>`、`ResourceAuthorizationContext<T>`、`ResourceAuthorizationDecision`、`DefaultResourceAuthorizationService`、`AddResourceAuthorizationHandler`。规则改写为 `AuthorizationHandler<OperationAuthorizationRequirement, TResource>`（`context.Allow()` → `Succeed(requirement)`，`Deny()` → `Fail()`，操作名 `requirement.Name`，主体取 `context.User`），注册为 `IAuthorizationHandler`。宿主增加 `AddResourceAuthorization()`（新包；EF 入口不再注册判定服务），Web 宿主需 `AddSecurity()` 提供当前主体。同名包曾因只装一个映射类被删除，这次内容完全不同 |
| `IResourceAuthorizationService.IsGrantedAsync` 删除 `CancellationToken` 参数 | 官方授权管线不接收取消令牌，保留参数也传不下去；调用处去掉最后一个实参。没有经过认证的当前主体一律拒绝（任一身份已认证即算，与官方一致）。ACL 处理器执行与功能权限相同的主体租户规则：租户声明非法、或显式判定的主体不属于当前租户时失败关闭 |
| `IEventBus` 不再注册为服务 | 接口保留为本地与将来分布式总线的共同基接口；注入 `IEventBus` 的发布方改注入 `ILocalEventBus`。与 `ILock` 同一处理：避免接入分布式总线后注入方静默换成另一种投递语义 |
| `ICurrentUser` 恢复 `IsInRole(role)`、新增 `FindClaims(claimType)` | 修订第 11 节的删除：`IsInRole` 只看主体身份、按其 `RoleClaimType` 精确匹配（服务间调用时机器身份上的角色不算，与官方整个主体的语义不同，差异写在 XML 注释）；`FindClaims` 跨全部身份读多值 claim。`GetRoles`、`GetAllClaims`、`PhoneNumber` 维持删除：经 `ICurrentPrincipalAccessor.Principal.Claims` / `Identities` 读取。自定义 `ICurrentUser` 实现需补这两个成员 |
| 模板前端：错误解析认官方字典形 `errors`，不再兼容响应信封 | `ApplicationHttpError` 把 `{字段: [消息…]}` 展开为逐条 `ApiErrorItem`；删除对信封 `errorCode`、数字 `code`、`message` 与条目 `message` 的读取（成功一侧从未解包，只兼容失败一侧会误导）。开启 `AddResponseWrapper()` 的项目要同时改拦截器的成功解包与失败读取，见前端编码规范 |
| 模板：OIDC 客户端与远端令牌的条件改用 `RemoteTokenAuth` | 前端全部与后端令牌验证相关的 `!LocalIdentity` 改为 `RemoteTokenAuth`；`!LocalIdentity` 只表示没有本地用户表。当前两者取值相同，生成结果不变 |
| 模板前端：`environment.api` 只保留 `gateway` | `authService`、`appService`、`envService` 没有读取方，删除；按服务名经网关分流用 `GATEWAY_SERVICE_NAME` 请求上下文（前端 README）。`format.utils.ts` 删除 |
| 模板镜像以非 root 运行 | API 与迁移镜像 `USER $APP_UID`（UID 1654）。compose 文件型 secret 保留宿主机权限，证书须对 UID 1654 可读（`chown 1654` 或属组可读）；用文件目录存 Data Protection 密钥时目录须可写。部署文档已写明 |
| 模板上手项 | `.http` 改为健康检查与 OpenAPI 端点；新增官方 OpenAPI（`AddOpenApi()`，Development 下 `MapOpenApi()`，`/openapi/v1.json`）；compose 镜像名改为 `${BACKEND_IMAGE:-companyname-projectname:latest}` / `${MIGRATOR_IMAGE:-…}`，不再写死个人仓库；`TZ` 默认 `UTC`（进程本地时区，可经 `.env` 覆盖） |

## 14. 模板前端单测迁到 Vitest

| 变化 | 影响与改法 |
| --- | --- |
| `ng test` 改由 `@angular/build:unit-test` 驱动 Vitest，在 Playwright 的无头 Chromium 里运行 | Karma、Jasmine 及其配置删除（`karma.conf.js`、`@types/jasmine`、`istanbul-lib-instrument` 等）。新机器首次运行前执行 `npx playwright install chromium`；CI 同样需要安装（`--with-deps`）。有头运行：`npm test -- --browsers=chromium` |
| spec 写法改为 Vitest | `jasmine.createSpyObj` → 由 `vi.fn()` 组成的对象，类型用 `Pick<MockedObject<T>, …>`；`spyOn` → `vi.spyOn`（**默认调用原实现**，要桩掉副作用须显式 `mockReturnValue` / `mockResolvedValue`）；`.and.returnValue` → `.mockReturnValue`；`toBeTrue()` → `toBe(true)`；`done` 回调改为 `async` + `firstValueFrom`。已有项目可先跑官方 `ng g @schematics/angular:refactor-jasmine-vitest`，再按上述差异核对 |
| 单测专用构建配置 `unit-test` | 不做开发构建的 Mock 提供器替换，`_mock/core/providers.ts` 保持部署形态。自定义了 `development` 配置的项目，单测不再跟着它变 |
| `vitest-base.config.ts` 开启 `restoreMocks`、`unstubGlobals`；`isolate: true` | 每条用例开始前自动还原 `vi.spyOn` 替身与 `vi.stubGlobal` 的全局值（其他直接修改与假计时器仍需自己还原）；每个 spec 文件在独立页面运行，并发执行的文件之间不共享全局对象上的桩 |
| 测试名统一英文 | 前端 `describe` / `it` 标题与后端测试方法名、`DisplayName` 用英文句子；中文只在注释与测试数据里 |
| **`_mock` 目录下的 spec 此前从未执行** | `angular.json` 的 `unit-test.include` 原为 `_mock/**/*.spec.ts`，而 include 的 glob 以 `sourceRoot`（`src`）为工作目录，它被解析成不存在的 `src/_mock/**`，于是该目录下的用例一条也没跑过、测试照样全绿。**这不是 Vitest 迁移引入的**，Karma 时期同样如此。改法：写成 `../_mock/**/*.spec.ts`。自建了 `src` 之外测试目录的项目一并核对——用 `ng test --list-tests` 列出实际被发现的文件，与磁盘上的 spec 集合比对，不要只看用例总数 |
| **官方 schematic 的转换要逐文件核对** | `refactor-jasmine-vitest` 是实验性的，官方也要求检查转换结果。已复现会**静默改变测试语义**的形态：<br>① **分离的策略调用**——`const spy = spyOn(x, 'm');` 之后另起一行 `spy.and.callThrough();`，会转成 `vi.spyOn(x, 'm').mockReturnValue(undefined)` 加一句悬空的 `spy;`，测试从"测真实实现"变成"测桩值"。紧跟写法 `spyOn(x, 'm').and.callThrough()` 转成 `vi.spyOn(x, 'm')`，行为等价（`vi.spyOn` 默认调用原实现），**不要一律当成错误转换**。<br>② 已有桩值之后再 `callThrough()` 同样丢失切回真实实现。<br>③ `// prettier-ignore` 块被重排、文件末尾注释被丢。<br>④ Vitest 的 `toContain` 不接受 `objectContaining`。<br>⑤ 未处理的 Promise 拒绝会让整轮失败。<br>⑥ 被其他 spec 导入的"测试辅助 spec"会被重复登记用例。<br>**核对方法**：在**迁移前的代码与迁移 diff 上**搜 `callThrough`（迁移后它已被删掉，事后搜不到）；`expect(` 计数只能作辅助——上述①②发生时断言数完全可以不变；关键处核对真实副作用或返回值，并用"撤回修复"的变异确认判据真的会红 |

## 15. 组件端点的业务拒绝留痕

| 变化 | 影响与改法 |
| --- | --- |
| 新增 `HttpContext.RecordFailedOperationAsync(failure)`（`Leistd.OperationRecords.AspNetCore`） | 与 `RecordDeniedOperationAsync` 对称：授权通过之后被业务规则拒绝时，按端点上的 `[OperationRecordAction]` 补一条失败记录。在宿主**紧接 `UseAuthorization()`** 的中间件里捕获、调用、原样重抛；不要放进 `IExceptionHandler`（租户作用域已退出，租户内的失败会写进宿主层）。只新增，不影响现有调用 |
| 业务拒绝留痕记录消息参数 | 模板中间件改为 `RecordFailedOperationAsync(OperationFailure.FromCode(exception.Code, exception.LocalizationData))`，查询时渲染出与接口报错同一句带具体值的原因——此前只记码，带占位符的码显示成 `Email '{Email}' is already in use.`。**审计与导出会出现这些值（如邮箱、用户名）**：异常作者只把可公开展示的值放进 `LocalizationData`（`WithData`），排查用的内部信息放日志；基类 `Exception.Data` 与异常文本不要带进记录。参数值按 `OperationFailure.FromCode` 的规则序列化，只记标量 |
| "是否匿名"统一为任一身份已认证；新增 `ClaimsPrincipal.HasAuthenticatedIdentity()`（`Leistd.Security.Core`） | 此前各处只看第一个身份，首身份未认证、后续身份已认证的主体被当成匿名，而授权管线放行了它：租户可被请求头改写、失效租户会话不被收回、环境上下文不建立租户、Hub 不复评、操作记录不记或操作人为空。现统一为官方 `DenyAnonymousAuthorizationRequirement` 的口径，作用于 `ICurrentUser.IsAuthenticated`、租户解析与多租户中间件、租户会话恢复、租户环境上下文、SignalR 复评、`RecordDeniedOperationAsync` / `RecordFailedOperationAsync`。标识、名字、租户仍只取自带标识的主体身份；服务间调用的机器身份判定仍只看第一个身份（有意）。自定义 `ICurrentUser` 实现按同一口径调整 |
| 模板新增 `Api/Middlewares/OperationFailureRecordingMiddleware` | 组件映射的端点（如权限整体替换）被业务规则拒绝（并发冲突、权限未定义、主体不存在）时留下失败记录，此前只有授权阶段被拒才记。派生项目照模板加这个中间件，放在 `UseAuthorization()` 之后，调用 `RecordFailedOperationAsync(OperationFailure.FromCode(exception.Code, exception.LocalizationData))` 连同消息参数一起记。只记 `BusinessException`，参数校验失败不记 |
| 失败记录按动作码与目标去重，兜底不再与应用服务冲突 | **推荐写法：应用服务照常在拒绝处调 `IOperationRecorder.RecordFailedAsync`，中间件只作兜底。**它手里有业务目标名，而兜底只拿得到错误码与路由值。同一动作与目标在本次请求里已经记过时，`RecordFailedOperationAsync` 会跳过，**保留先记的那条，不覆盖也不替换**——此前那条『挂了注解的端点，应用服务不要再调 `RecordFailedAsync`』的约定随之作废，它逼人二选一（要参数就没有兜底，要兜底就丢参数）。<br>去重是作用域级，判据为**动作码 + 目标**：同一请求里多条不同动作、或同一动作不同目标的失败照常都记（前者如『改口令失败』之后紧跟『账号被锁定』，后者如批量删除里逐个目标失败）。**注解声明的目标（含 `TargetIdPrefix`）要与应用服务记录的目标逐字一致**，这本来就是按目标检索能查全的前提，去重同样依赖它。兜底推不出目标时（端点没声明目标路由键、或某段路由值缺失）退回只按动作码判，这类端点上同一动作的第二次失败不会被兜底补记——要逐条留痕由应用服务按目标逐条记。写库失败时不登记，兜底仍会补记，不会出现一次拒绝一条记录都没有。<br>**因此也不需要按端点类型把控制器排除在中间件之外**：那会让同一个动作从控制器搬到 Minimal API 就改变审计行为。已经这么绕过的项目可以撤掉，回到照模板全量挂中间件 |

## 16. 工作单元与账号安全（CRM 拆分反馈）

| 变化 | 影响与改法 |
| --- | --- |
| `IUnitOfWorkManager.BeginAsync` 改为同步的 `Begin`，返回 `IUnitOfWork` | `using var uow = await manager.BeginAsync(...)` → `using var uow = manager.Begin(...)`；自定义实现改签名。原方法本就没有异步操作。**工作单元由使用它的那个方法自己开启**：当前工作单元存放在 `AsyncLocal` 里，在 `async` 辅助方法里开启后返回，调用方拿到的不是它的当前工作单元，写入会各自提交，且没有任何报错 |
| 工作单元日志分级 | 调用方主动取消（取消异常且令牌已取消）且发生在提交开始之前：事务型改记 Debug（什么都没提交），非事务型记 Warning（已保存的部分不会回滚）；提交开始之后的失败、令牌未取消的取消异常（如数据库超时）与其余提交失败仍记 Error。回滚日志由 Warning 改为 Debug：每次业务拒绝都会回滚，原因已由异常处理记下。依赖这两条日志做告警的，改为按提交失败的 Error 告警 |
| 多租户：有独立库租户的系统，每个宿主都要注册租户连接路由 | 不注册路由时 `DbContext` 一律沿用宿主连接，独立库租户的数据会静默写进共享库。多宿主时把路由注册放进共用的组合方法，见多租户组件文档"解析租户连接" |
| 模板：用户新增 `SecurityStamp`（基线迁移已含该列） | 登录第二步的挑战记下签发时的值，第一步之后改口令、管理员重置、启用或停用两步验证、解绑外部登录，挑战即作废（此前仍能完成登录）。**已部署的派生项目**要新增一条迁移加这一列（`character varying(32)`，非空）；存量行可填任意值（如 `md5(random()::text)` 截取 32 位），下次凭据变更时自动轮换 |
| 模板：两步验证挑战的有效期不再被输错延长 | 此前每次输错都把 5 分钟重新算满，最长约 25 分钟；现从签发起算，到期以注入的时钟为准 |
| 模板："退出其他设备"的返回值只计有效设备 | 已过期的会话照旧一并删除，但不计入提示数与操作记录 |
| 模板：新增每日过期会话清理作业 `auth.sessions.cleanup` | 不再登录的用户的过期会话（连同原始 IP）此前会无限期保留。作业逐库执行、含停用租户的库，删除时关闭租户过滤。多副本部署需 Redis（集群锁），锁键前缀规则同其他周期任务 |

## 17. 外部登录按邮箱关联与令牌撤销边界

| 变化 | 影响与改法 |
| --- | --- |
| 模板：外部登录按邮箱关联已有账号，改为两边都已验证才关联 | 此前外部账号的邮箱与本地账号相同就直接关联，任何人在提供商那里填上别人的邮箱即可接管对方账号。现要求提供商确认邮箱已验证（`ExternalUserInfo.EmailVerified`：Google 取 `verified_email`，GitHub 取 `/user/emails` 的主邮箱）**且**本地账号邮箱已确认；不满足而邮箱已被占用时返回 409 `ExternalAuth:AccountExistsSignInToLink`，回调页显示服务端原因，用户先登录原账号再在账号设置里绑定。外部登录新建账号时只采用已验证的邮箱，并记为已确认；未验证的邮箱改用占位地址，只留在外部连接上，防止抢占别人的地址。自定义 `IOAuthProvider` 须按提供商的 API 填 `EmailVerified`，不填即按未验证处理。**已部署的派生项目**：升级前按旧规则自动关联产生的绑定仍然有效，系统分不清哪些是被利用过的。用户表只记邮箱是否已确认、不记确认时间，仅凭用户与绑定两张表还原不了当时的验证状态；保留了足够的邮箱确认与绑定操作记录时，可以用它们辅助排查 |
| 文档：资源服务的撤销传播窗口 | 停用或删除账号、停用或删除租户：在身份服务立即生效，在只验签的资源服务要等已签发的 Access Token 过期（模板为 10 分钟），之后也刷新不到新令牌。撤销会话不撤销 OAuth 令牌：刷新令牌默认滑动续期，持续刷新的客户端对资源服务的访问会一直延续，调短 Access Token 有效期也无济于事。内省只能让已撤销的令牌即时失效。见多租户组件文档"注意事项"与模板部署文档 |
| 模板：外部登录回调改为路径形式 `/auth/external-callback/{provider}` | 此前回调页从查询串 `?provider=` 取提供商，回调地址须写成 `/auth/external-callback?provider=github`，而 GitHub 连查询串一起比对登记值，极易配错；`deploy/docker-compose.yml` 的示例还误指向 API 的 POST 端点。现 `ExternalAuth:{Provider}:RedirectUri` 与提供商后台登记的回调都改为 `https://<站点>/auth/external-callback/github`（Google 同理），两处逐字一致 |
| 模板：只分配给已删除用户的角色可以删除 | 删除用户是软删除、角色关联行保留，此前角色的"已分配用户数"与删除校验把它们也算上，角色永远删不掉，界面上又找不到可改派的人。现只数未删除的用户；删除角色时连带删除剩余的关联行。删除角色改为单一工作单元：角色、关联、授权与成功记录一起提交，任一步失败整体回滚（此前角色与授权分两次提交，失败后会留下"接口报错但角色已删"的状态） |
| 模板：管理员启用、停用用户与重置密码留操作记录 | 新增动作 `user.enabled`、`user.disabled`、`user.password-reset`（账号类，Notice）；状态没变的重复启停不留记录。控制器同时挂上 `[OperationRecordAction]`，被拒的操作也留痕。派生项目若自定义了操作记录的动作展示，补这三个码的文案 |
| 操作记录的失败原因由后端本地化：`OperationRecordOutputDto` 新增 `FailureMessage`，导出新增 `FailureMessage` 列；新增 `LocalizationPlaceholders.Fill`（`Leistd.ExceptionHandling.Core`），`Leistd.OperationRecords.Core` 因此直接依赖该包 | 查询服务用容器里的非泛型 `IStringLocalizer` 按 `FailureCode` 取文案、用 `FailureData` 填 `{Name}` 占位符，与错误响应同一条词条、同一套填充规则；审计要与接口报错措辞不同时（如记录多带了次数、时长），备一条 `{码}:Record`，它优先于码本身（两步各走本地化器的文化回落，审计键在整条回落链上都没有才查码本身，因此只在默认语言备了审计键时其他语言也显示它）；没有本地化器或缺词条时为空。自定义 DTO 或前端直接显示 `failureMessage`，缺失时回落到码；前端原先按 `operationRecords.failures.<码>` 自备的词条可以删掉，改在后端资源里为码备文案（本组件自产的 `Error:Forbidden` 也要备，键不再把冒号换成下划线）。导出 CSV 的仅宿主列因此后移一列，按列序解析导出文件的脚本要调整。模板：删除前端 `Error_Forbidden` 等三条失败词条、后端资源补 `Error:Forbidden`；登录失败与锁定两条记录带着错误响应没有的参数，后端资源新增 `Auth:InvalidCredentials:Record`、`Auth:UserTemporarilyLockedOut:Record`，前端 `operationRecords.failures` 整节删除（不含本地化的形态仍内置英文句子，按同样的 `{name}` 占位符填参数）；操作记录页切换语言时重新拉取列表 |
| 多租户：租户名须是单个 DNS 标签（`TenantConfiguration.NamePattern`：字母、数字与连字符，不以连字符开头或结尾，≤63），创建与改名（名称变化）时校验，不合法返回 400 `Tenant:NameInvalid`（占位 `Name`、`Pattern`）；`CreateTenantInputDto` 的 `Name` 长度上限由 64 改为 63；`UpdateTenantInputDto` 仍按存储容量 64（`TenantConfiguration.MaxStoredNameLength`），名称原样不变时照常接受 | 此前只校验非空与长度，能建出 `Invalid Name!` 这类名字，配置 `DomainFormat` 后它们拼不成主机名、经子域名访问不到且不报错。**不做数据迁移**：已有的不合规租户照常解析（请求头、查询串与令牌声明不受影响），编辑其他字段也照常保存（名称逐字不变就不校验，含 64 个字符与首尾空白的旧名）；只有改名时须符合新规则。启用了子域名解析的部署，这些租户本来就经子域名访问不到，可查出名字含空格、下划线、点或非 ASCII 字符、以连字符开头或结尾、或恰为 64 个字符的租户，择机改成合规名字并告知其用户。绕过 `ITenantManager` 直接写租户表的代码不受这条校验保护 |

## 18. 前端实时连接的默认日志等级

| 变化 | 影响与改法 |
| --- | --- |
| `signalr-service.ts` 的 `configureLogging` 默认值从 `Information` 改为 `Warning` | WebSocket 传输带不了请求头，令牌只能拼进 URL 查询串（`access_token=`），而 `@microsoft/signalr` 连接成功那条日志以 `Information` 打印**整个 URL**——令牌原文进浏览器控制台。只有资源服务形态（配了 `accessTokenFactory`）真会带令牌，但默认值不按形态分叉。排障可临时调高，调高之后日志里可能带出凭据。**日志等级管不到浏览器自己打的那条**：握手失败时浏览器会输出 `WebSocket connection to 'wss://…?access_token=…' failed`，它不经过该库的日志管道，调等级与过滤器都拦不住；要彻底避免只能让访问令牌本身从一开始就不进入浏览器发起的连接 URL——改变客户端认证方式，例如协商阶段换一张短时效的一次性连接票据（票据自身仍在 URL 里，泄露面从长期令牌缩小到一次性票据）；**在反向代理层改写没有用**，浏览器已经用带令牌的 URL 发起了连接。本模板未实现票据这一层。代价是失去常规连接信息，`Warning` / `Error` 仍在 |

## 19. 模板前端 lint 结果缓存

默认与本地化前端的 `lint:ts`、`lint:style`、`format` 从无条件重跑改为对应工具的官方 content cache，检查范围和规则不变；没有双入口或迁移开关。派生项目同步这三个 scripts、依赖安装时删除 `.cache/lint/` 的 `postinstall`，并在 Git/格式检查中忽略该目录。现有 `prepare: husky` 保留。缓存不随模板生成分发，不加入依赖包或公共 API。

插件更新可能不进入格式检查器的缓存键，因此不能只同步 `--cache` 而遗漏安装清理；显式忽略 npm 生命周期脚本安装后，先删除 `.cache/lint/` 再检查。框架运行时与测试用例未删除。维护与验收口径见[模板质量验证](../template/quality-assurance.md)。

## 20. 日志不再出现邮箱原文

| 变化 | 影响与改法 |
| --- | --- |
| `Leistd.Email.Smtp` 的投递日志改为记**脱敏后的收件人** | 原先 `Email sent to {To} with subject {Subject}` 把收件人邮箱原文写进 Information 级日志；邮箱是个人数据，而日志通常被集中采集、保留更久、可见范围更大。**字段名不变**，仍是 `{To}`，值改为经 `TextRedactor.RedactEmail` 脱敏：`zhangsan@example.com` → `zha***@example.com`（本地部保开头几位 + 完整域名）。保住域名是为了能按域名聚合，看出"某个租户或某个邮件服务商整体收不到"；地址取不出域名（配错了）时记成 `not***` 这样的形态，**不回落成原文**（否则"配错的地址"会成为唯一泄露原文的路径，而那恰好是最容易被翻到的一类日志）。**按完整收件地址做过精确匹配的告警规则或日志管道要改**：改为按域名后缀匹配，或改用操作记录追查具体收件人 |
| 模板的三处日志不再记邮箱 | `AuthAppService`（注册）、`UserAppService`（管理员建用户）、`EmailSettingsAppService`（测试发信失败）改为记脱敏后的地址（`al***@example.com`），经 `Leistd.Core` 新增的公共入口 `Leistd.Redaction.TextRedactor.RedactEmail`（纯函数，无需注册、无新依赖）。派生项目自己写的日志按同一口径核对一遍：**联系方式（邮箱、手机号）不进日志** |
| **账号名照常记，不改** | 模板的用户名受 `^[a-zA-Z0-9_]+$` 约束、不可能是邮箱，是系统自身的账号标识而非联系方式，而且正是这些日志可读性的来源——换成 GUID 会让排障的人每条都要回库查一次。不要为此把用户名从日志里去掉 |
| 操作记录的参数口径**不变** | 审计按设计会显示邮箱、用户名等可公开展示的值（见组件文档里 `LocalizationData` 的约定），这是审计的用途所在，不受本条影响。不要误以为审计也脱敏了 |
| `Leistd.Email.Smtp` 新增对 `Leistd.Core` 的包依赖 | 为取 `TextRedactor`。`Leistd.Core` 只含原语、只依赖抽象，通常已在依赖闭包里；在架构门禁里限制应用层/领域层可引用包名的项目按 `nuspec` 对比结果更新白名单（方法见第 1 节） |
| 新增公共入口 `Leistd.Redaction.TextRedactor`（`Leistd.Core`，纯静态方法、无新依赖） | `RedactEmail(address)` → `al***@example.com`（保本地部开头几位 + 完整域名：从第一个字母或数字起最多 3 位且不超过一半——`zhangsan@`→`zha***@`、`alice@`→`al***@`、`bob@`→`b***@`）；`RedactPartially(value, keepStart, keepEnd)` → `158***90`（位数由业务定：手机号常用 `(3,2)`，卡号按 PCI DSS 最多 `(6,4)`，证件号 `(0,4)`）。写日志与对外展示共用。**只提供形态，不维护数据类型目录**——不要期待框架为每种业务数据加方法 |
| **没有引入脱敏组件** | 评估过 `Microsoft.Extensions.Compliance.Redaction`（数据分类 + `IRedactorProvider` + 日志脱敏），本次未采用：缺陷是"组件默认把个人数据写进日志"，终局修法是默认不写，而不是建一套"把个人数据安全写出去"的机制。实测结论留档在仓库的 `docs/assessments/`，其中两条对派生项目有用：**普通模板日志（`logger.LogInformation("{To}", to)`）永远不会被脱敏**，脱敏只作用于带 `[LoggerMessage]` 与数据分类标注的源生成方法；以及 **`builder.Services.AddSerilog(configure)` 与 `EnableRedaction()` 冲突，两者同时存在时日志会全部消失**（不是丢字段）。自行接入脱敏的项目注意这两点 |

## 21. 官方 Token Exchange 与服务客户端认证替换

服务间用户委托改用 OpenIddict 7.7 的 Token Exchange；机器回源也改用 OpenIddict.Client。源码不保留旧委托头协议。只签发 access token 时创建的是令牌记录，不自动创建临时授权记录。

**混合版本存在中断窗口，不能承诺无中断滚动升级。** 新 Identity 的 `SystemInitializer` 在初始化时立即删除目录中已不存在的 `svc.delegate` scope，官方 scope 注册也不再包含它。因此 Identity 首先升级后，旧调用方在冷缓存或缓存过期时申请包含该 scope 的令牌即可失败（`invalid_scope`）；已缓存的旧令牌也不能使新下游恢复用户身份，访问自然人端点返回 403。部署顺序可按 Identity → 调用方 → 下游安排，但不能将最后“清理旧权限”理解为延后删除 scope。不接受此窗口的部署须同步切换相关服务，或在部署层将流量切到版本一致的新服务组；代码不增加兼容路径。

工作负载 client ID 要与来源 API 受众一致（例如 `orders-machine` → `orders-api`）。改名后机器主体变为 `client:orders-api`，按 client ID／主体写的机器策略、审计筛选与密钥配置均要更新。旧 client ID 与密钥只有在旧应用仍保留、相应 grant/scope 仍获授权且目标接受机器身份时才能继续取机器令牌；删除／改名应用后不能假定旧密钥可用于新 client ID，须为新应用配置其有效密钥。保留旧应用也无法恢复已删除的 `svc.delegate`，不保证旧用户委托链路可用。

| 原调用／配置 | 影响与替代方式 |
| --- | --- |
| `AddClientCredentials(configuration)` 与旧凭据 Options | 先全局 `AddServiceAuthentication()`，再在命名客户端调用 `AddClientCredentials()`；`Authority/ClientId/ClientSecret` 绑定 `Leistd:ServiceAuth`，机器 `Scope` 绑定 `Leistd:ServiceClients:{Name}`；`ExpirationBuffer` 属性及配置删除，机器缓存固定提前 60 秒失效，用户交换固定提前 10 秒失效 |
| 生成 Client 包自动安装机器认证 | 注册返回的 builder 不再自动认证。未显式组合的宿主访问受保护端点会稳定返回 401；使用 `services.AddMyProjectClient(configuration).AddClientCredentials()`，或按用户调用选择 `.AddTokenExchange()` |
| 用户／租户委托头与恢复管道 | Resource 注册 `AddUserAccessTokenAccessor(实际 Bearer 验证方案)`，用户客户端 `AddTokenExchange()`；目标 `Audience/Scope` 绑定 `Leistd:ServiceClients:{Name}:TokenExchange`，身份与租户来自已验证 JWT |
| 机器调用自动转发 `ICurrentTenant` | 不再转发租户；按租户执行的后台作业若仍只调用 `ICurrentTenant.Change()`，下游可能在宿主上下文执行且不报错。租户必须作为显式参数，例如租户回源使用的路由 tenant ID；下游机器端点自行验证调用权限、租户有效性并建立业务租户上下文 |
| `Leistd:ServiceAuth:Scope/ExpirationBuffer/TokenEndpoint` 与 `ResolveTokenEndpoint` | 旧全局 scope/buffer 不再读取，scope 改为命名客户端配置，buffer 固定为机器 60 秒／交换 10 秒；手工端点删除，官方客户端按 `Authority` 发现端点并协商认证 |
| `Leistd:ServiceClients:{Name}:UserContext` 与 `Leistd:ServiceUserContext` | 整个配置节删除；不检测旧配置，也不保留旧协议兼容路径 |
| Cookie／后台用户上下文 | 不提供 Token Exchange 证明；默认适配器只读验证方案保存的用户访问令牌。普通后台调用选择机器认证，不能仅设置 ambient 用户来委托 |
| 收到下游 401 后自动重放 | 仅清本地令牌缓存，下次独立调用重新取令牌；调用方按业务幂等性决定是否重试，分布式删除失败不能覆盖原 401，取消仍传播 |
| `ConsentType` DTO、前端选项与错误码 | 删除；服务端固定 implicit。存量应用升级前将其同意类型归一为 implicit |

供派生项目搜索的已删除或收敛公共契约（不要仅删配置而保留调用）：

- OAuth：`IServiceTokenProvider`（含 `GetAccessTokenAsync/Invalidate`）、`ClientCredentialsTokenProvider`、`DependencyInjection.ServiceAuthSectionName`；旧 `ClientCredentialsOptions.Authority/ClientId/ClientSecret/TokenEndpoint/ResolveTokenEndpoint/ExpirationBuffer`，以及接收 `IConfiguration` 的旧 `AddClientCredentials` 重载。`ClientCredentialsDelegatingHandler` 从 public 改为 internal，不再直接构造，改用命名客户端的 `AddClientCredentials()`。
- Core：`ServiceClientHeaders`（`UserId/Username/TenantId`，即 `X-User-Id/X-Username/X-Tenant-Id`）、`ServiceClientScopes.Delegation`（`svc.delegate`）、`UserContextForwardingOptions`、`ServiceClientOptions.UserContext`、`UserContextDelegatingHandler<TOptions>`、`TenantContextDelegatingHandler<TOptions>`。
- AspNetCore：`ServiceUserContextOptions`、`ServiceUserContextClaimsTransformation`、`ServiceUserContextMiddleware`、`AddServiceUserContext`、`UseServiceUserContext`；内部 `ServiceUserContext` 与 `CompositeClaimsTransformation` 一并删除，相关身份恢复与转换组合不再注册。
- 配置属性：旧 `UserContext.Enabled/ForwardUsername/UserIdHeader/UsernameHeader/ClaimHeaderMap/ForwardTenantId/TenantIdHeader`；旧 `Leistd:ServiceUserContext` 下的 `Enabled/UserIdHeader/UsernameHeader/TenantIdHeader/HeaderClaimMap/RemoveUntrustedHeaders/RequiredScope/AuthenticationType`；模板派生变量 `ServiceUserContextEnabled` 不再存在。

存量同意类型数据示例：`UPDATE "<schema>"."OpenIddictApplications" SET "ConsentType"='implicit' WHERE "ConsentType" IS DISTINCT FROM 'implicit';`。没有同意页与永久授权的第三方场景不支持。

模板新增每日 `auth.openiddict.prune` 集群任务，使用官方管理器，清理早于 14 天阈值创建的无效令牌与授权（并非过期后额外保留 14 天）。控制库不按租户重复执行，多副本沿用 Redis 集群锁与共享水位。Cookie `auth_time` 为真实认证时刻，续期不改变它；`prompt=none` 无会话返回 `login_required`，`prompt=login/max_age` 引导重新认证并保留原会话，凭据成功后替换会话。现存 Cookie 缺 `auth_time` 时，带 `max_age` 的请求要求重新登录。

交换令牌最多 120 秒，最终 `exp` 不超过来源；角色与超管信息留给下游本地授权，用户名、邮箱、显示名由 Identity 回查权威资料。Bearer 缓存只在进程内，按官方客户端返回的到期时间提前失效，机器 60 秒、用户交换 10 秒。组件不设置宿主日志过滤；官方客户端脱敏协议中的令牌与密钥字段。`AddServiceAuthentication` 的 `DisableTokenStorage` 是官方客户端全局选项，同宿主的 OpenIddict.Client 交互式登录也受影响。需要 state 存储的宿主在所有组件注册之后调用 `services.Configure<OpenIddict.Client.OpenIddictClientOptions>(options => options.DisableTokenStorage = false)`，并按官方方案接入 Core、令牌存储、证书与交互式宿主集成；覆写须晚于 `AddServiceAuthentication`，其后再次调用组件注册会关闭存储，无需强制分开部署或增加框架开关。

## 22. 质量入口合并与模板 CI 分片

仓库维护入口 `framework/build/check-docs-api-drift.ps1` 在一个进程中执行原 8 个正反例和完整正文扫描，共用本次源码索引；原独立 `-SelfTest` 模式及对应 `check-all.ps1` 清单行删除。原自检能抓的规则失效由合并入口的正反例接替，正文漂移仍由同一完整扫描接替；不删除单元测试。调用方移除旧 `-SelfTest` 参数，直接调用脚本。

CI 原串行“打包 → 包消费 → 九场景”改为一次 `framework-pack` 产出不可变候选包，独立 `package-consumption` 和两片 `template-shards` 下载到各自目录并行验证。默认全量包消费还须与当前源码的完整包集一致，漏包失败；人工 `-PackageIds` 的缩小入口保留。原 `template-matrix` 必过检查名保留为汇总：必要作业全部成功，两份结果恰好覆盖登记全集、完整阶段与容器责任；失败、取消、跳过或缺片不能放行。原包内容/隔离消费由独立必过作业接替；每场景原测试与断言由所属分片原入口接替，没有新旧两套校验或迁移开关。OIDC 作业形态保留，发布继续等待同一候选的全部质量结果。

场景与分片归属只维护在 `scripts/template-matrix-scenarios.ps1`；PR 的容器范围从完整 base 到 head 判定，替代会漏掉较早提交的 `HEAD^` 差异，范围不明时执行容器验证。维护口径见[质量检查与验证分工](./quality-assurance.md)与[模板质量验证](../template/quality-assurance.md)。这是 leistd-net 仓库 CI/维护脚本调整，派生项目无需修改运行时 API。

模板默认与本地化前端的 Angular 运行时、CDK、编译器和 CLI/build 统一更新到 22.2.0，两套 lock 同步；替换 22.1 系列依赖以通过既有 high 审计阈值，不修改 lint 缓存、测试隔离或发现范围。派生项目按两套依赖文件更新并重新安装。安全依据见 [Angular Router 官方公告](https://github.com/advisories/GHSA-ff3f-86qr-9cv3)（公告利用路径为 Node SSR）。

## 23. 外部登录的本地用户名与账号标签

`ExternalUserInfo` 原来用一个 `Username` 同时表示"绑定的是哪个外部账号"和本地用户名，现拆成两个：
`ProviderAccountLabel`（必填，展示用）与 `SuggestedUsername`（可空，**只有真正的公开句柄**才填，由提供商显式给出）。
GitHub 两个都填 `login`；Google 没有句柄，标签放完整邮箱、`SuggestedUsername` 留 `null`。
自定义 `IOAuthProvider` 实现要同步这两个属性，**不要**用邮箱本地部顶替 `SuggestedUsername`。

本地用户名改由外部登录领域服务生成：基底取 `SuggestedUsername ?? DisplayName`，清洗成
[`UsernameRules`](../../template/backend/src/CompanyName.ProjectName.Domain/Users/Constants/UsernameRules.cs)
允许的字符集，裸基底被占用（以及清洗后不可用而回落到 `user`）时加六位随机后缀。
原先直接采用提供商给的值有两个后果：`alice@x.com` 与 `alice@y.com` 两个不同的人会撞上 `Username` 的
租户内唯一索引，第二个人首次登录直接失败；提供商给的值还可能含 `.` `+`，造出用户自己在账号设置里都改不回去的名字。
邮箱未验证时填的占位地址改为 `{guid:N}@{provider}.local`，不再由用户名或提供商标识派生——
用户名可改、外部连接可解绑，而用户行连同它的邮箱一直在（软删除也仍占着唯一索引），派生值会让下一个人算出同一个地址。

实体字段 `ExternalLoginConnection.ProviderUsername` 随之改名为 `ProviderAccountLabel`，
列名、输出 DTO 字段与前端 `providerAccountLabel` 一并改。**模板的基线迁移直接改写，没有新增迁移**：
生成项目拿到的是已经正确的基线。**已部署的派生项目**要新增一条迁移改列名，并在新版本开始服务之前执行：
`ALTER TABLE "<schema>"."ExternalLoginConnections" RENAME COLUMN "ProviderUsername" TO "ProviderAccountLabel";`
（表在 `HasDefaultSchema` 指定的业务 schema 里，PostgreSQL 默认搜索路径找不到它，必须限定；
用户与外部连接表在**共享库与每个独立租户库里各有一份**，逐库执行）。
用 `migrations add` 让 EF 比对实体时要核对生成结果：它可能给出"删列再加列"而不是 `RenameColumn`，那会丢数据——
改成 `RenameColumn`，或直接在空迁移里 `migrationBuilder.Sql(...)` 执行上面的语句。
基线迁移 Id 未变，存量库不会自动应用，不补则运行时报列不存在。
用户名与占位邮箱**不回填**：存量行保持原值，功能上不受影响（新占位地址是随机的，不会与旧值相撞）。

用户名与邮箱的可用性判定（注册、建用户、改资料）改为关闭软删除过滤后查询。两者的唯一索引都没有排除
`IsDeleted`，被删用户仍占着这两个值，而仓储默认过滤掉软删除行：判定原先答"可用"，随后落库撞唯一索引，
用户看到 500。现在答"已被占用"并返回 `User:UsernameTaken` / `User:EmailTaken`。
外部登录按已验证邮箱关联已有用户时同样关闭该过滤，且**被删用户不参与自动关联**：
新增错误码 `ExternalAuth:EmailOwnedByDeletedAccount`（映射 409，中英文词条各一条），
不复用 `User:EmailTaken`——启用本地化后词条会整条替换抛出时的消息，而那一条只说"已被使用"，
既丢掉"账号已删除"这层信息，又带一个需要回显邮箱地址的占位符。
前端若按错误码分支，要把这个码加进处理。
派生项目若希望被删用户交还用户名与邮箱，要自行在唯一索引上加 `IsDeleted = false` 过滤并新增迁移，
那会改变"删掉再建同一个人"的语义，本次不做。

## 浏览器认证与官方外部处理器

模板自带前端停止充当 OAuth public client，移除 angular-auth-oidc-client、environment.oidc、旧 /auth/callback 组件和令牌拦截/SignalR token 注入及 Hub query 令牌转换；开发代理与同源 Angular 托管保留。第三方 public-client 授权码能力仍可单独登记。

- 外部 HTTP 契约改为 GET challenge → 官方 /api/v1/external-auth/{provider}/signin → POST complete；绑定使用受保护的 GET link/challenge → POST link/complete，不再接收前端 code/state；提供商后台重新登记完整 HTTPS signin 回调。登录与绑定意图受保护，complete 保留一次消费，失败后重新 challenge；第二步凭据只走 JSON/导航状态。
- Resource 新增 Authentication:ClientId/ClientSecret 必填后端配置（缺失时启动报出键名）；Identity 登记 web/confidential、授权码、PKCE、refresh token、offline_access 和本 API scope。登录/退出回调分别 /api/v1/auth/signin、/api/v1/auth/signout；前端 GET login、GET me、整页 POST logout。
- SaveTokens 必须配 ITicketStore。所有会话 Cookie 只携引用及版本，完整票据加密留服务器；部署共享缓存与 Data Protection 密钥。删除票据立即拒绝旧 Cookie，显式再次登录使旧引用版本失效。应用 SessionCookie:SameSite 不覆盖官方 correlation/nonce 的 None/Secure Always。模板未启用 antiforgery；默认写请求校验 Origin；浏览器页面与所属 API 必须同源，分进程须经部署代理统一外部源，详见生成项目浏览器认证文档。
- OAuth:ApiResources 由字符串数组改为对象，例如 `{ "Name": "https://api.example/orders", "Scope": "orders.read", "OwnerClientId": "orders-worker" }`。Scope/OwnerClientId 默认 Name，重复资源或 scope 启动失败。发起方按所有资源集合验证，允许客户端拥有多个资源；不再要求 client_id、scope、audience 字符串相等，授权码 presenter 与资源所有者可不同。
- 删除 IOAuthProvider/OAuthTokenInfo、GetExternalLoginUrlAsync 与 code/state DTO；保留规范化 ExternalUserInfo 和业务账号政策。ExternalLoginConnection 删除 AccessToken/RefreshToken/ExpiresAt、UpdateTokens，基线迁移同步删列；派生项目已有数据库须显式删列并覆盖各业务/租户库；若已覆盖模型快照，EF 不会自动推导出删列，需保留旧快照生成迁移或自行编写 DropColumn。
- `AuthenticationSchemeNames` 移至 `Application.Shared` 命名空间，使用方更新 using。提供商 scheme 使用 `AuthenticationSchemeNames.ExternalProviderPrefix + provider` 登记官方远程处理器，目录不接受普通 Cookie/Bearer/策略 scheme。外部登录站内 returnUrl 贯穿受保护票据与第二步验证；Resource 的 me 删除 isActive/creationTime。旧在飞请求的退出或刷新失败仅删除同一引用版本。
- Google UserInfo 改用官方 v3 的 sub/email_verified；旧 id→sub 真实账号连续性尚未验证，已有外部账号连接迁移须先实测。绑定列表字段 providerAccountLabel 的既有改名同前节。

完整当前契约见 [模板浏览器认证维护规则](../template/browser-authentication.md)。
