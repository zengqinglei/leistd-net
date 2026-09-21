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

契约与实现分离后，各家族的公共契约统一落在 `<家族>.Abstractions`；实现类按职责分到
`Services` / `Stores` / `Interceptors` 等子命名空间。包名里的 `.Core` 不再出现在命名空间里。

按类型数排列的主要映射：

| 0.12.0 命名空间 | 0.13.0 命名空间 | 类型数 |
| --- | --- | --- |
| `Leistd.Authorization` | `Leistd.Authorization.Abstractions` | 10 |
| `Leistd.Auditing` | `Leistd.Auditing.Abstractions` | 9 |
| `Leistd.Exception.Core` | `Leistd.ExceptionHandling` | 9 |
| `Leistd.UnitOfWork.Core.Database` | `Leistd.UnitOfWork.Database` | 6 |
| `Leistd.UnitOfWork.Core.Events` | `Leistd.UnitOfWork.Events` | 5 |
| `Leistd.UnitOfWork.Core.Uow` | `Leistd.UnitOfWork` | 5 |
| `Leistd.EventBus.Core.Event` | `Leistd.EventBus.Events` | 4 |
| `Leistd.Lock.Core` | `Leistd.Lock.Abstractions` | 4 |
| `Leistd.UnitOfWork.EfCore.Database` | `Leistd.UnitOfWork.EntityFrameworkCore.Database` | 4 |
| `Leistd.Authorization.AspNetCore` | `Leistd.Authorization.AspNetCore.Permissions` | 3 |
| `Leistd.DependencyInjection` | `Leistd.DependencyInjection.Registration` | 3 |
| `Leistd.EventBus.Core.EventBus` | `Leistd.EventBus.Abstractions` | 2 |
| `Leistd.Notifications` | `Leistd.Notifications.Abstractions` | 2 |
| `Leistd.Response.Core.Wrapper` | `Leistd.Response.Wrappers` | 2 |
| `Leistd.UnitOfWork.Core.Interceptor` | `Leistd.UnitOfWork.Interceptors` | 2 |
| `Leistd.UnitOfWork.Core.Options` | `Leistd.UnitOfWork.Options` | 2 |
| `Leistd.Authorization` | `Leistd.Authorization.Services` | 2 |

余下 30 余条是每个命名空间 1 个类型的同类搬迁（`Leistd.Notifications.EntityFrameworkCore`
拆成 `Stores` / `Entities` / `EntityConfigurations`，`Leistd.ObjectMapping.Mapster` 拆成
`Services` / `Options` / `Mapping`，等等）。逐条对照见[公共表面逐条对比](upgrade-0.13.0-api-diff.md)。

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
// Leistd.Authorization.Abstractions.IPermissionGrantStore
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

本版有两条这样的变化：

- 权限未定义的译文占位符由 `{Name}` 改成 `{Names}`（一次可以报多个权限名）。
  覆盖过 `Permission:UndefinedPermission` 的宿主，句子里的变量要跟着改。
- `Permission:SubjectUnavailable` 整条移除（读自己的权限时空主体改为返回空集合，不再报错）。

> `scripts/check-i18n-keys.ps1` 保证各语言的键集与占位符一致，但**不比对版本之间的增删**——
> 跨版本的键变化只能靠升级清单，所以上面这两条是人工列的。

## 8. 取包时的版本陷阱

nuget.org 上存在 `1.0.0-beta.22` 与 10 个 `1.0.0-preview.*`，SemVer 排序高于 `0.13.0-*`。
在它们被 unlist 之前，**预发布依赖必须写全称**（如 `0.13.0-beta.170`），不要用 `--prerelease`。
成因见 `versioning.md` 的「nuget.org 上存在版本号虚高的历史预发布包」。
