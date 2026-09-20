# 操作记录

记录关键操作：什么人、在什么时间、做了什么、结果如何。业务显式调用记录器，框架补齐操作人、时间与链路标识并持久化。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 事后要能回答"这条数据是谁改的、凭什么改" | 在用例里调 `IOperationRecorder.RecordSucceededAsync` |
| 业务规则拒绝了一次操作 | 调 `RecordFailedAsync`；独立提交，业务随后回滚也留得住；写不进去只记日志，不会把 403 变成 500 |
| 权限不足在**授权阶段**就被拒（请求到不了应用服务） | 端点打 `[OperationRecordAction]`，在授权结果处理器里调一行扩展方法 |
| 管理界面要列表、筛选、导出操作记录 | 路由组上调 `MapOperationRecords(...)`；自定义路由或 DTO 时直接用 `IOperationRecordQueryService` |
| 记录要有保留期（到期搬入归档表） | `AddOperationRecordRetention<TDbContext>()`，默认关闭 |
| 想知道某次请求的完整细节（入参、堆栈、耗时） | **不要往这里加字段**，按 `CorrelationId` 去请求日志里查 |
| 想追踪实体逐字段的变更前后值 | 本组件不做，用 EF 的变更追踪另行实现 |

查询、登录尝试、定时任务的例行心跳都不该记：审计表的价值来自密度，记满之后没人会去看。

## 安装

```bash
# 记录器与契约
dotnet add package Leistd.OperationRecords.Core

# EF Core 持久化
dotnet add package Leistd.OperationRecords.EntityFrameworkCore

# 授权阶段拒绝的补记（注解 + HttpContext 扩展）
dotnet add package Leistd.OperationRecords.AspNetCore
```

## 注册

```csharp
builder.Services.AddOperationRecordsEfCore<AppDbContext>();   // 内部已调用 AddOperationRecords()
```

宿主签发的 claim 用了别的名字时改配置，组件不写死：

```csharp
builder.Services.AddOperationRecords(options => options.ImpersonatorIdClaimType = "act_sub");
```

在 `OnModelCreating` 中映射记录表：

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigureOperationRecords();
}
```

**前置**：宿主须已注册 `AddUnitOfWork()` 与 `AddUnitOfWorkEfCore()`——存储经 `IDbContextProvider<TDbContext>` 取上下文，失败记录经 `IUnitOfWorkManager` 独立提交。

映射查询端点；授权口径全部必填，漏配在映射时抛出：

```csharp
app.MapGroup("/api/v1/operation-records").MapOperationRecords(options =>
{
    options.ReadPolicy = "App.OperationRecords";
    options.ExportPolicy = "App.OperationRecords.Export";
    options.ExportAction = "operation-records.exported";   // 须已登记
});
```

启用保留期归档（需要后台作业调度器与分布式锁，见[后台作业](./background-jobs.md)）：

```csharp
builder.Services.AddOperationRecordRetention<AppDbContext>();
```

## 使用

动作码、类别与授权依据都由业务自己定义并保持稳定——框架只把它们原样存下去，从不解释，因此不提供枚举也不提供常量。动作码还要保持稳定，界面按它本地化：

```csharp
public static class OperationRecordActions
{
    public const string UserCreated = "identity.user.created";
    public const string UserDisabled = "identity.user.disabled";
    public const string PasswordChanged = "identity.user.password-changed";
}

/// <summary>动作的类别，驱动界面的分类筛选。</summary>
public static class OperationRecordCategories
{
    public const string Account = "account";
    public const string Authentication = "authentication";
}

/// <summary>不由权限把守的操作，凭什么放行。</summary>
public static class OperationRecordAuthorizations
{
    /// <summary>用户改自己的数据，凭的是"已认证且是本人"。</summary>
    public const string AuthenticatedSelf = "AuthenticatedSelf";

    /// <summary>机器主体凭 client credentials 令牌调用，凭的是该客户端被授予的 scope。</summary>
    public const string ClientCredentials = "ClientCredentials";
}
```

动作码必须登记类别与可见性，未登记的码写入时直接抛出：

```csharp
public class AppOperationActionDefinitionProvider : IOperationActionDefinitionProvider
{
    public void Define(IOperationActionDefinitionContext context)
    {
        context.Add(OperationRecordActions.UserCreated, OperationRecordCategories.Account, OperationVisibility.Tenant);
        context.Add(OperationRecordActions.UserDisabled, OperationRecordCategories.Account, OperationVisibility.Tenant);
        context.Add(OperationRecordActions.PasswordChanged, OperationRecordCategories.Authentication, OperationVisibility.Actor);
    }
}

builder.Services.AddSingleton<IOperationActionDefinitionProvider, AppOperationActionDefinitionProvider>();
```

用例里显式记录：

```csharp
public class UserService(IOperationRecorder recorder, ...)
{
    public async Task<User> CreateAsync(CreateUserInput input, CancellationToken ct)
    {
        var user = await userManager.CreateAsync(input, ct);

        // 落在调用方的事务边界里：业务回滚则这条记录一并回滚
        await recorder.RecordSucceededAsync(
            OperationRecordActions.UserCreated,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            "App.Users.Create",
            ct);

        return user;
    }

    public async Task DisableAsync(Guid id, CancellationToken ct)
    {
        if (user.IsLastAdministrator)
        {
            // 独立提交：下面的抛出让业务回滚，这条记录照样留下
            await recorder.RecordFailedAsync(
                OperationRecordActions.UserDisabled,
                OperationTarget.For(id, user.DisplayName ?? user.Username),
                "App.Users.Update",
                OperationFailure.FromCode("User:LastAdministrator"));
            throw new InvalidOperationException("最后一个管理员不能停用。");
        }
        ...
    }
}
```

用户自助操作没有权限名可填，传业务自己的授权依据标记：

```csharp
await recorder.RecordSucceededAsync(
    OperationRecordActions.PasswordChanged,
    OperationTarget.For(currentUser.Id!.Value, currentUser.Name ?? currentUser.Username),
    OperationRecordAuthorizations.AuthenticatedSelf,
    ct);
```

授权阶段的拒绝：端点声明动作码，宿主在自己的授权结果处理器里补记。

```csharp
[HttpPut("{id:guid}")]
[Authorize(Policy = "App.Roles.Update")]
[OperationRecordAction(OperationRecordActions.RoleUpdated, "id")]
public Task<Role> UpdateAsync(Guid id, ...) => ...;
```

**注解就是唯一的选择权**：打了就记，没打就不记。框架不按 HTTP 方法之类的启发式二次否决——
把注解放上去已经表达了"这个动作值得留痕"，再筛一道会让打在 `GET` 导出端点上的注解悄悄失效。

授权依据记**实际未通过**的具名策略：端点叠了多个策略（如权限策略之外再要求近期 MFA）时，
被拒路径上逐个重新评估，取最具体（最后声明）的未通过者，与特性的书写顺序无关。

```csharp
public sealed class AuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context,
        AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            await context.RecordDeniedOperationAsync();
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
```

## 接口参考

| 成员 | 说明 |
| --- | --- |
| `IOperationRecorder.RecordSucceededAsync(action, target, authorizationBasis, ct)` | 记录一次成功；落在调用方的事务边界里，写入失败照常上抛。`Host` 可见的动作在租户上下文里调用即抛 `InvalidOperationException` |
| `IOperationRecorder.RecordFailedAsync(action, target, authorizationBasis, failure)` | 记录一次被拒或失败；独立提交、不可取消，写失败只记日志不抛。成功路径**没有** `failure` 参数——成功不存在"为什么没成"；失败路径**没有**取消令牌——被审计的一方断开连接不能让审计作废 |
| `OperationTarget` | 目标的标识与名字快照捆绑传递；`For(id, name)` / `None`。名字是快照，理由同 `ActorName` |
| `OperationFailure` | 失败原因；`FromCode(code, dataJson)` 走本地化码，`FromDetail(detail)` 走技术说明。**刻意不提供接受任意 `Exception` 的重载** |
| `OperationRecordInfo` | 一条记录的传输形态；各列长度上限以 `Max*Length` 常量给出。除操作人与目标标识外还带 `TargetName`（目标名快照）、`Visibility`（必填）、`ActorTenantId`（操作发生时的租户上下文；与 `TenantId` 不同的行即从租户上下文写进宿主层的失败记录），以及失败三件套 `FailureCode`（本地化码）／`FailureData`（占位参数 JSON，**刻意不设长度上限**）／`FailureDetail`（技术说明，含内部拓扑，宿主应在下发前裁剪） |
| `OperationRecordOutcome` | `Succeeded`、`Failed`；只有两档 |
| `OperationVisibility` | `Tenant` / `Host` / `Actor`；写入时由动作定义盖章。`Host` 表示记录属于宿主层，见「可见性与记录所在的层」 |
| `OperationSeverity` | `Info` / `Notice` / `Critical`；描述动作本身有多要紧，与结果好坏无关 |
| `IOperationActionDefinitionProvider` | 登记本模块的动作定义；宿主用 `AddSingleton<IOperationActionDefinitionProvider, ...>()` 注册。类别是业务定义的字符串，框架不预置清单 |
| `IOperationActionDefinitionManager` | 动作定义的只读索引；`GetOrNull` 返回 `null` 即未登记。写入时记录器据此抛错；读取历史记录时调用方据此降级（原样显示裸码） |
| `OperationRecordOptions` | `ImpersonatorIdClaimType` / `ImpersonatorNameClaimType`，默认值取自 `CustomClaimTypes` |
| `IOperationRecordStore.InsertAsync(record, ct)` | 写入；成功记录跟随调用方的事务，失败记录在 `record.TenantId` 所指的层里独立提交 |
| `IOperationRecordStore.GetPagedListAsync(filter, page, ct)` | 按创建时间倒序分页，返回 `PagedResult<OperationRecordInfo>`；`OperationRecordFilter` 的 `Scope` 必填，关键字匹配动作码、目标标识与操作人名，时间两端都是**闭区间**且按 UTC 比较；`PageRequest.Sorting` 不生效 |
| `IOperationRecordQueryService` | 查询、筛选项与导出用例：无租户上下文即宿主读者；租户读者看不到 `Host` 层、`Actor` 层只看本人；仅宿主字段（`FailureDetail`、`CorrelationId`、`ActorTenantId`）只下发给宿主读者；类别与动作维度间取交集，展开为空返回空页。**不做权限判定**，由端点策略把守 |
| `MapOperationRecords(configure)` | AspNetCore 包：`GET /`、`GET /filter-options`、`GET /export`；`ReadPolicy`、`ExportPolicy`、`ExportAction` 必填；返回路由组，端点名前缀见 `OperationRecordEndpoints.NamePrefix` |
| `AddOperationRecordRetention<TDbContext>(configure?)` | EF 包：绑定 `Leistd:OperationRecords:Retention` 并启动期校验，登记集群周期任务 `operation-records.archive`（每日 `DailyRunHourUtc` 执行） |
| `IOperationRecordArchiveService` | EF 包：逐库、分批把到期记录搬入 `OperationRecordArchive`，每批一个事务；返回搬运条数与失败库数 |
| `OperationRecordVisibilityScope` | 可见范围，**由调用方算好**，只能从三个入口取得：`Host` 见全部，`ForTenantReader(actorId)` 见租户层加本人的 `Actor` 层，`Unrestricted` 不过滤（仅供不代表读者的内部任务）。没有默认值——可见性是安全边界，漏传即越权。**存储不判定"谁是宿主"**——那需要它不该有的上下文依赖 |
| `AddOperationRecords(services)` | 注册记录器 |
| `AddOperationRecordsEfCore<TDbContext>(services)` | 注册 EF Core 存储；内部调用 `AddOperationRecords()` |
| `ConfigureOperationRecords(modelBuilder)` | 映射 `OperationRecord` 与归档表 `OperationRecordArchive` |
| `IOperationActionDefinitionContext.Add(..., targetIsActor)` | 标记自证类动作：成功且没有操作人时，查询输出的 `ActorIsTarget` 为真，界面把目标显示在操作人列 |
| `[OperationRecordAction(action, params targetRouteKeys)]` | 声明写端点的动作码；`TargetIdPrefix` 可对齐成功路径的目标标识写法 |
| `HttpContext.RecordDeniedOperationAsync()` | 在授权结果处理器里补记一条失败；端点有注解才记，匿名请求一律不记；授权依据取实际未通过的具名策略 |

## 实现行为

- **时间由记录器填充，不依赖审计拦截器。** 审计属性的自动填充要宿主把 `AuditSaveChangesInterceptor` 挂到目标 DbContext，漏挂是静默的——得到的会是一张时间全为零的审计表，而"什么时间"塌掉整张表就没用了。实体因此也**不实现** `ICreationAuditedObject`。
- **操作人名存快照。** 用户改名或销号之后，靠标识反查到的要么是新名字、要么什么都没有，而审计要回答的是"当时是谁"。
- **操作人标识读 claim 原始值，不读 `ICurrentUser.Id`。** 后者只在 `sub` 能解析成 GUID 时有值，而机器主体（`client:<client_id>`）与后台作业主体都不是 GUID；只认 `Id` 会把这两类操作全部记成无主的，而它们恰恰是最需要事后追查的那批。claim 名由 `OperationRecordOptions.ActorIdClaimType` 给出，默认 `CustomClaimTypes.Subject`。
- **后台作业、消息消费者、Hub 调用先建立环境上下文。** 这些入口不经 ASP.NET Core 中间件，主体、租户与链路标识在其中都不成立，记录器会拿到一片空白。用 `IAmbientContext.Begin(principal)` 一次性建立全部已注册维度（只切主体的 `ICurrentPrincipalAccessor.Change` 不够——租户与链路不会跟着走）。主体的 `sub` 由宿主自定前缀（如 `job:nightly-cleanup`），`name` claim 决定记录里显示的操作人名。
- **模拟登录留两个人。** 主体上的用户是被模拟者，真实操作人按 `OperationRecordOptions` 指定的 claim 读出，另存 `ImpersonatorId` / `ImpersonatorName`。只记前者等于把真正按下按钮的人从审计里抹掉。
- **事务边界按结果区分。** 成功记录落在调用方所处的边界里，与它描述的变更同生共死，否则会留下"记了但没发生"的假账。失败记录在新开的工作单元里独立写入并提交：业务在工作单元内记完失败紧接着抛出、整体回滚是被拒路径最常见的形态，而"谁在反复做他不被允许的事"正是审计最要留住的。第二个事务只向本表插入一行、不读不改业务表，常规场景不与外层冲突；**只允许单个写事务的数据库（如 SQLite）是例外**：外层已有未提交的写入时，这次写入等锁直至超时，结果是一条 `Error` 日志、记录丢失。
- **失败路径写不进去只记 `Error`，不抛。** 被拒的请求本来就要以 403/400 结束，不能因为这一条审计没记下来变成 500，把真正的拒绝原因盖掉。写入不可取消：客户端中断请求不能让这条审计作废。
- **动作码与授权依据必填且不得为空白**（`ArgumentException`），**动作码必须已登记**（`InvalidOperationException`）。两项校验都在失败路径的 `try` 之外：那个 `catch` 吞的是"写库没成功"这类运行期故障，而这两类是确定性的编码错误，必须当场响。空串落库之后，"这次操作不需要授权依据"与"调用方漏传了"就再也分不开；未登记的码没有可见性可盖，默认给租户看是泄露，默认只给宿主看又会让租户上下文里写下的记录谁都看不见。
- **超长字段就地截断，并在截断前记 `Warning`。** 审计写入不能因为一个字段超长，把一次已经成功的业务操作变成 500。
- **可见性与记录所在的层。** `Tenant` 与 `Actor` 的记录写进操作发生时的租户层。`Host` 的记录属于宿主层：在宿主上下文里照常写；租户上下文里的**失败**记录（典型是租户用户调用宿主接口被拒）改写进宿主层，来源租户记在 `ActorTenantId`——留在租户层的话，租户读者按可见性看不到、宿主按租户维度也查不到。租户上下文里的**成功**记录则直接抛错：它既不能留在租户层，也不能脱离租户库里的业务事务挪去宿主层；这类动作要么登记为 `Tenant`，要么在切到宿主上下文之后再记。
- **读取隔离靠全局查询过滤器，写入的 `TenantId` 由记录器显式盖章。** 实体是 `IMultiTenant`，存储查询不带租户条件；而写入不依赖宿主 DbContext 是否为 `BaseDbContext`——理由与时间戳相同，漏配是静默的，得到的会是一批归属为空的记录。
- **两个索引：`(TenantId, CreationTime DESC)` 与 `(TenantId, Visibility, CreationTime DESC)`**。这张表写多读少，查询形态是"某租户的最近若干条"；而可见性过滤在**每一次**查询里都出现（宿主之外的读者读不到 `Host` 层记录），不进索引会让这个必然出现的谓词退化成对时间区间结果集的逐行筛。关键字检索走时间裁剪之后的过滤，**不**单独建索引——每加一个索引的代价都摊在每一次写入上。
- **分页按 `CreationTime` 再按 `Id` 倒序**。同一毫秒内的多条记录时间相同，只按时间排序会让分页出现重复或遗漏，次级键消除这种不确定。它保证的是**顺序确定**，不是"同一时刻内更新的在前"——后者取决于 Provider 怎么比较 Guid（PostgreSQL 的 `uuid` 按网络字节序，UUIDv7 的时间序成立；SQLite 把 Guid 存成 BLOB、按 .NET 字节布局比较，前三段是小端，时间序不成立）。分页需要的是前者。
- **`Outcome` 以字符串落库**。审计表会被人直接查，序号要对着枚举定义翻译；枚举重排之后历史行的含义还会静默改变。
- 表名沿用 EF Core 默认约定，不加框架前缀污染宿主库。

## 注意事项

- **框架只定义它自己会读的字符串，且连这些也让宿主能改。** 动作码与授权依据框架都只存不读，一律由业务定义——框架穷举不了业务词汇，硬定一套只会逼着业务去凑。模拟登录的 claim 名框架要读，但它属于"宿主签发主体时的技术细节"，因此经 `OperationRecordOptions` 注入、默认值指向 `CustomClaimTypes`，而不是写死在组件里。
- **动作码一旦发布就不要改。** 它是历史记录的含义本身，改了等于篡改过去。
- **存储没有更新与删除。** 留了入口，"清理误记录"迟早变成"清理不想被看到的记录"，那时这张表已经不能作为证据了。保留期由 `AddOperationRecordRetention` 把到期记录**搬入**归档表，数据仍在库里；归档表不实现 `IMultiTenant`，将来为它开查询时须自行按租户过滤。
- **保留期默认关闭。** 保留天数受法律与合同约束，组件无从知道；启用时 `RetentionDays` 取 30–3650。
- **Minimal API 端点的查询参数不要改成 `[AsParameters]` 绑定。** 它把没有默认值的非空属性当必填，省略 `offset` 的请求会直接 400。
- **不要加请求维度字段**（IP、UA、URL）。那属于请求日志；混进来就回到了"用路由代替业务语义"。
- **不要加变更明细。** 那是实体变更追踪的量级（另一张明细表 + 追踪拦截器），加进来会得到半个审计日志却没有它的能力。
- **`IOperationRecordStore` 只能有一个实现。** 为第二个 DbContext 注册时在注册期直接拒绝：一半的审计写进宿主没预期的库，比没有审计更危险。
- **框架不接管 `IAuthorizationMiddlewareResultHandler`。** 宿主只能注册一个，那里通常还承载着应用专有的处置；框架占住它，宿主唯一的授权处置入口就没了。
- **不要改用"发布领域事件、由处理器统一订阅"来取代记录器。** 这个方案覆盖不了三条记录路径里的两条：授权阶段的拒绝根本没进领域层，没有聚合能发事件；业务在工作单元内拒绝并抛出时，事件会随回滚一起丢——`ILocalEventBus.PublishAsync` 先问 `ILocalEventDeferrer`，只要有活动工作单元就推迟到提交后发布，因此**主动发布与实体收集两条路径殊途同归**。改用事件仍须保留直接写入器去覆盖那两条，最终是两套机制并存。此外"凭什么被允许"只有调用点知道，事件里没有这个信息。成功路径上业务当然可以自己用事件驱动，但那是宿主的选择，不是组件的机制。
- **被拒记录的唯一闸门是注解，唯一的例外是匿名请求。** 匿名不记不是偏好而是安全属性——那种请求没有操作人，记下来等于把审计表变成一个不需要凭据的写入面。除此之外框架不替调用方做取舍。

## 相关

- [多租户](./multi-tenancy.md)
- [当前用户与身份信息](./security.md)
- [链路追踪](./tracing.md)
