# 操作记录

记录关键操作：什么人、在什么时间、做了什么、结果如何。业务显式调用记录器，框架补齐操作人、时间与链路标识，交给唯一的写入方：数据库存储（可查询、导出、归档），或结构化日志（只写出，不保存可回读的历史）。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 事后要能回答"这条数据是谁改的、凭什么改" | 在用例里调 `IOperationRecorder.RecordSucceededAsync` |
| 业务规则拒绝了一次操作 | 调 `RecordFailedAsync`；独立提交，业务随后回滚也留得住；写不进去只记日志，不会把 403 变成 500 |
| 权限不足在**授权阶段**就被拒（请求到不了应用服务） | 端点打 `[OperationRecordAction]`，在授权结果处理器里调一行扩展方法 |
| **组件映射的端点**在授权之后被业务规则拒绝（宿主在那里没有代码可写） | 同一个注解，在宿主紧接授权之后的中间件里调一行扩展方法 |
| 管理界面要列表、筛选、导出操作记录 | 路由组上调 `MapOperationRecords(...)`；自定义路由或 DTO 时直接用 `IOperationRecordQueryService` |
| 记录要有保留期（到期搬入归档表） | `AddOperationRecordRetention<TDbContext>()`，默认关闭 |
| 不要产品内的历史查询，记录交给日志采集链路（SIEM 等） | `AddOperationRecordsLogging()` 代替数据库存储；记录器与调用点完全不变 |
| 想知道某次请求的完整细节（入参、堆栈、耗时） | **不要往这里加字段**，按 `CorrelationId` 去请求日志里查 |
| 想追踪实体逐字段的变更前后值 | 本组件不做，用 EF 的变更追踪另行实现 |

查询、登录尝试、定时任务的例行心跳都不该记：审计表的价值来自密度，记满之后没人会去看。

## 安装

```bash
# 记录器与契约
dotnet add package Leistd.OperationRecords.Core

# EF Core 持久化
dotnet add package Leistd.OperationRecords.EntityFrameworkCore

# 结构化日志输出（与 EF Core 持久化二选一）
dotnet add package Leistd.OperationRecords.Logging

# 被拒与业务拒绝的补记（注解 + HttpContext 扩展）
dotnet add package Leistd.OperationRecords.AspNetCore
```

## 注册

```csharp
builder.Services.AddOperationRecordsEfCore<AppDbContext>();   // 内部已调用 AddOperationRecords() 与 AddOperationRecordQueries()
```

记录器只认一个写入方（`IOperationRecordWriter`），历史读取（`IOperationRecordReader`）与查询用例只由能回读的存储登记。
数据库存储与日志输出互斥，同时注册在注册期即抛错。

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

**前置**（全部必填，漏一个在首次解析 `IOperationRecorder` 时才暴露）。组件不替你注册别的组件——
每条记录都要回答"谁、在哪个租户、哪条请求链路、什么时间"，这四样各来自一个独立组件：

```csharp
builder.Services.AddUnitOfWork();
builder.Services.AddUnitOfWorkEfCore();            // 存储经 IDbContextProvider 取上下文，失败记录独立提交
builder.Services.AddSingleton<IClock, UtcClockProvider>();   // Leistd.Core 不提供 DI 扩展；DDD 基础设施包已代为注册
builder.Services.AddMultiTenancyCore();            // ICurrentTenant：记录写进哪一层
builder.Services.AddAmbientContext();              // ICurrentUser：谁做的
builder.Services.AddCorrelationIdCore(builder.Configuration);   // ICorrelationIdProvider：哪条请求链路
```

保留期归档另外还要逐库遍历（`ITenantDatabaseRunner`），同样由 `AddMultiTenancyCore()` 提供；
不分库时它给出的清单只有宿主库，行为与单库一致。

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

### 结构化日志输出

数据库模式的同事务保证要求在业务的环境事务工作单元内调用记录器，并使用该事务内的数据库存储。若宿主在 `AfterCommit` 事件处理器或无工作单元路径中记录，业务已经提交，记录只能另外持久化；更换适配不能让它重新加入已结束的事务。需要业务与记录原子持久化的用例应在原事务内调用记录器。

不需要产品内的历史查询时，用日志输出代替数据库存储。记录器、动作定义、失败补记与调用点**完全相同**，只换写出的去处：

```csharp
builder.Services.AddUnitOfWork();
builder.Services.AddLocalEventBus();               // 成功记录借工作单元的提交后阶段写出（宿主须启用拦截器织入）
builder.Services.AddOperationRecordsLogging();     // 记录器的四样前置同上
```

```json
{ "Logging": { "LogLevel": { "Leistd.OperationRecords": "Information" } } }
```

| 记录 | 写出时机 |
| --- | --- |
| 成功，有环境工作单元 | 事务**提交之后**（`AfterCommit` 阶段）；回滚、提交失败都不写出。嵌套的子工作单元归入外层；`requiresNew` 的独立工作单元在它自己提交后写出；非事务型工作单元在保存与 `BeforeCommit` 阶段成功之后写出；若之前失败，已落库的非事务写入不会回滚，也没有成功记录 |
| 成功，无环境工作单元 | 立即写出——调用方在变更落库之后才记录，与数据库存储"即时生效"同一调用约定 |
| 失败 | 立即写出，不等待、不跟随调用方的事务 |

- 常量命名空间为 `Leistd.OperationRecords.Logging.Constants`。日志类别 `OperationRecordLogging.CategoryName`（`Leistd.OperationRecords`）；成功为 `Information`（事件 7100），失败为 `Warning`（事件 7101）。字段与数据库存储逐一对应：`OperationRecordId`、`OperationAction`、`OperationOutcome`、`OperationTargetId`/`Name`、`OperationActorId`/`Name`、`OperationActorTenantId`、`OperationTenantId`、`OperationAuthorizationBasis`、`OperationVisibility`、`OperationImpersonatorId`/`Name`、`OperationFailureCode`/`Data`/`Detail`、`OperationTime`（UTC）、`OperationCorrelationId`。
- 记录在调用时冻结：标识、时间、操作人与租户都在记录器里定案，提交后写出时不再读取任何上下文。
- **启动期校验**：该类别对 `Information` 未开启（安全记录会被静默过滤），或缺少工作单元与本地事件总线时，宿主启动失败。
- **持久化保证弱于数据库存储**：事务已提交、日志尚未写出时进程退出，这条成功记录会丢失；日志的保留期与防篡改由采集链路负责。
- 业务已提交之后日志写不出去时，不改变业务结果、不补记失败，在 `OperationRecordLogging.DeliveryCategoryName` 指定的类别以 `Critical`（事件 7102）报告动作码与记录标识。
- 不注册历史读取与查询：`MapOperationRecords` 在本模式下映射时即抛错，保留期归档也不适用。

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

### 失败原因要带参数

上面那条只有一个码。**真正有用的拒绝记录还要带上"为什么是这一条"的那几个值**——
运维看到 `Order:CreditLimitExceeded` 只知道类别，看到额度与差额才知道该找谁批。
参数走 `FromCode` 的第二个入参（键即资源文案里的占位名），与本地化词条一一对应。
`BusinessException.LocalizationData` 正是这个类型，所以异常与记录可以用同一份参数：

```csharp
// 应用服务：业务规则拒绝，不是授权拒绝——授权那一档由端点上的
// [OperationRecordAction] 自动补记，这里是走到了业务逻辑之后才判定的
public async Task<OrderDto> PlaceAsync(PlaceOrderInput input, CancellationToken ct)
{
    var customer = await customers.GetAsync(input.CustomerId, ct);
    if (customer.Available < input.Amount)
    {
        // 独立提交：紧随其后的抛出让业务回滚，这条记录照样留下
        await recorder.RecordFailedAsync(
            OperationRecordActions.OrderPlaced,
            OperationTarget.For(customer.Id, customer.Name),
            PermissionConstant.Orders.Create,
            OperationFailure.FromCode(OrderErrorCodes.CreditLimitExceeded, new Dictionary<string, object?>
            {
                ["Limit"] = customer.CreditLimit,
                ["Available"] = customer.Available,
                ["Requested"] = input.Amount
            }));
        // 失败记录不接收取消令牌：请求被客户端中断，不能让这条审计作废（见 RecordFailedAsync 的 XML）。
        // 成功路径的 RecordSucceededAsync 反过来要传 ct——两者刻意不同。

        throw new BusinessException(OrderErrorCodes.CreditLimitExceeded, "Insufficient credit limit.")
            .WithData("Available", customer.Available)
            .WithData("Requested", input.Amount);
    }
    ...
}
```

失败原因与错误响应共用宿主资源里的同一条词条：键就是错误码，占位符是 `{Name}`：

```json
"Order:CreditLimitExceeded": "可用额度 {Available}，本次需要 {Requested}。"
```

查询时 `IOperationRecordQueryService` 按**当前请求语言**查这条词条、用 `FailureData` 填占位符，
结果放在 `FailureMessage` 里下发；界面直接显示它，不再自备失败原因词条。
词条缺失或宿主没有启用本地化时 `FailureMessage` 为空，界面回落显示裸码——不报错，所以漏配是静默的。
记录与异常的参数不同时（记录只带了其中几个键，或多带了记录才需要的键），没有对应参数的占位符原样保留。

审计需要与接口报错**不同的措辞**时（常见于记录多带了报错没有的参数，如"5 分钟内已失败 3 次"），
另备一条 `{码}:Record` 词条：查询先查它，取不到再查码本身，两者填占位符的规则相同。
两步各自走本地化器的文化回落（请求语言 → 父文化 → 默认语言），所以**审计键在整条回落链上都没有**才用码本身：
只在默认语言（如英文）备了 `:Record` 时，中文请求看到的是英文审计措辞，而不是中文的接口报错文案——要中文措辞就在中文资源里也备一条。
占位名与记录里 `FailureData` 的键逐字一致（区分大小写）：

```json
"Auth:InvalidCredentials": "The username or password is incorrect.",
"Auth:InvalidCredentials:Record": "Incorrect username or password ({attempts} failed attempts within {windowMinutes} minutes)"
```

三件事容易漏：

- **异常与记录用同一个码。** 前端按异常的码分支，运维按记录的码检索；两边不一致时，
  "用户看到的错误"和"审计里的那一行"对不上，排查要靠时间戳猜。已经有 `BusinessException`
  在手时直接 `OperationFailure.FromCode(exception.Code, exception.LocalizationData)`。
  只取 `LocalizationData`：它按约定只放可公开展示的消息参数（本就随错误响应返回给调用方）；
  基类的 `Exception.Data` 与异常文本没有这个约定，不要带进记录。`FailureData` 对租户读者可见、还会进导出，
  消息参数里的值（如邮箱、用户名）随之可见——所以抛异常时只把可公开展示的值放进 `WithData`。
- **参数只能是标量。** 字符串、布尔、数值、日期、`Guid`。传进对象不会被摊开，只会写下类型名——
  这既是防泄露（这一列租户直接可读），也因为词条占位符 `{X}` 渲染不了对象。
- **参数不要塞进 `FromDetail`。** 那一路是给技术说明用的，不过本地化，
  界面只能原样显示英文串。
- **只在真的拒绝时记。** 校验没通过就返回、并不构成一次"被拒的操作"时不要记——
  审计表的价值来自密度。

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

不传原因时补上 `OperationFailureCodes.Forbidden`（`Error:Forbidden`）——没有原因码的失败记录
事后无法按原因聚合。**这是本组件唯一自产的失败码，没有随包译文，宿主资源要为它备一条词条**
（键 `Error:Forbidden`）；漏了只会显示裸码，不报错。

授权通过之后的业务拒绝：组件映射的端点（如权限管理的整体替换）在并发冲突、目标不存在时抛出业务异常，
宿主在那里没有代码可写。在宿主**紧接 `UseAuthorization()`** 的中间件里捕获、补记、原样重抛，
**判据与被拒路径相同**——端点有注解才记，匿名请求一律不记。调用方显式传入业务异常的错误码与消息参数
（`LocalizationData`，不含 `Exception.Data`）时，查询时渲染出与接口报错同一句带具体值的原因：

```csharp
public sealed class OperationFailureRecordingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BusinessException exception)
        {
            await context.RecordFailedOperationAsync(
                OperationFailure.FromCode(exception.Code, exception.LocalizationData));
            throw; // 响应仍由外层的异常处理写出
        }
    }
}

app.UseAuthorization();
app.UseMiddleware<OperationFailureRecordingMiddleware>();
```

使用 Leistd 异常处理组件时，捕获的是它的 `BusinessException`。

- **位置决定两件事，必须紧接 `UseAuthorization()`。** 授权没通过的请求到不了这里，"授权已通过"才成立——
  授权之前的中间件（如两步验证限制）抛出的业务异常不会被记成授权之后的拒绝；请求还在租户作用域里，
  记录才写进操作发生的那一层。
- **不要放进 `IExceptionHandler`。** 它在管道最外层执行，租户中间件的作用域已随异常退出，租户内的失败会被写进宿主层；
  授权之前抛出的业务异常也会流到那里。官方的 `IExceptionHandlerFeature` 保留了端点与路由值，但保留不了应用自己的租户作用域。
- **授权依据取端点最后声明的具名策略，不重新评估**：走到这里所有策略都已通过，最后声明的是最具体的一层
  （端点级晚于路由组级与类级），同一级叠加多个时记后声明者；没有具名策略时记 `-`。这与被拒路径不同，那里取实际未通过的那个。
- **只记业务拒绝。** 参数校验失败（`ValidationException`）没有业务码，无从按原因聚合，属于请求日志；技术异常同理。
- **带注解端点的业务拒绝由这里兜底记，但它不是唯一入口。** 应用服务照常可以在拒绝处调
  `RecordFailedAsync`，它手里有业务目标名。记录器写出成功后会登记动作码与目标（契约见
  `RecordedFailureTracker`），**兜底遇到已登记的同一动作与目标就跳过**，于是留下的是先记的那条。
- **判据是动作码加目标。** 同一动作对不同目标的失败是不同的事实，都要留——批量操作里应用服务已为
  一个目标记过失败，另一个目标的失败照常补记。只按动作码会把后者一起吞掉，而丢掉的审计记录找不回来。
- **注解声明的目标要与应用服务记录的目标逐字一致**（含 `TargetIdPrefix`）。这本来就是按目标检索能
  查全的前提，去重同样依赖它；两边格式不同会让去重失效，退化成一次失败两条记录。
- **兜底推不出目标时退回只按动作码判。** 端点没声明目标路由键、或某一段路由值缺失时，兜底只能记 `-`；
  拿 `-` 去比永远不相等，一次失败又会变两条。此刻无从分辨是哪一个目标，宁可少补一条。
  代价是这类端点上同一动作的第二次失败不会被兜底补记——要逐条留痕就由应用服务**按目标逐条**调
  `RecordFailedAsync`，不要依赖兜底补齐。
- **跨 DI 作用域不生效。** 跟踪器按作用域注册，所以从新建的作用域解析记录器时去重失效（退化成多记一条，
  不丢记录）。框架里有两处会新建作用域：**本地事件分发**（`EventHandlerWrapper` 每次分发都新建）与
  **工作单元**（`IUnitOfWorkManager.Begin` 为每个非子工作单元新建，`IUnitOfWork.ServiceProvider` 即那一份）。
  应用服务按构造注入拿记录器不受影响，这也是推荐写法。
- **并发留痕要自己串行化**（`Task.WhenAll` 里每个子操作各记一条失败）：跟踪器把一个作用域视为一条逻辑
  执行流，线程安全前提见 `RecordedFailureTracker`。

## 接口参考

| 成员 | 说明 |
| --- | --- |
| `IOperationRecorder.RecordSucceededAsync(action, target, authorizationBasis, ct)` | 记录一次成功；数据库适配在有环境事务时随事务提交，日志适配在提交后输出。调用阶段异常照常上抛，日志提交后投递故障只报告。`Host` 可见的动作在租户上下文里调用即抛 `InvalidOperationException` |
| `IOperationRecorder.RecordFailedAsync(action, target, authorizationBasis, failure)` | 记录一次被拒或失败；独立提交、不可取消，写失败只记日志不抛。成功路径**没有** `failure` 参数——成功不存在"为什么没成"；失败路径**没有**取消令牌——被审计的一方断开连接不能让审计作废 |
| `OperationTarget` | 目标的标识与名字快照捆绑传递；`For(id, name)` / `None`。名字是快照，理由同 `ActorName` |
| `OperationFailure` | 失败原因；`FromCode(code, data)` 走本地化码（`data` 是标量字典，本组件负责写 JSON），`FromDetail(detail)` 走技术说明。**刻意不提供接受 `Exception` 的工厂**，`BusinessException` 也不例外 |
| `OperationFailureCodes` | 本组件自己会产生的失败码，目前只有 `Error:Forbidden`（授权被拒的默认原因）。这些码没有随包译文，宿主资源必须自备词条，否则 `FailureMessage` 为空、界面显示裸码 |
| `OperationRecordInfo` | 一条记录的传输形态；各列长度上限以 `Max*Length` 常量给出。除操作人与目标标识外还带 `TargetName`（目标名快照）、`Visibility`（必填）、`ActorTenantId`（操作人自己所属的租户，取自主体的租户 claim；宿主管理员进入租户操作时它仍是宿主；匿名请求没有主体，取请求所在的租户上下文。与 `TenantId` 不同的行即跨层写入的记录），以及失败三件套 `FailureCode`（本地化码）／`FailureData`（占位参数 JSON，**刻意不设长度上限**）／`FailureDetail`（技术说明，含内部拓扑，宿主应在下发前裁剪） |
| `OperationRecordOutcome` | `Succeeded`、`Failed`；只有两档 |
| `OperationVisibility` | `Tenant` / `Host` / `Actor`；写入时由动作定义盖章。`Host` 表示记录属于宿主层，见「可见性与记录所在的层」 |
| `OperationSeverity` | `Info` / `Notice` / `Critical`；描述动作本身有多要紧，与结果好坏无关 |
| `IOperationActionDefinitionProvider` | 登记本模块的动作定义；宿主用 `AddSingleton<IOperationActionDefinitionProvider, ...>()` 注册。类别是业务定义的字符串，框架不预置清单 |
| `IOperationActionDefinitionManager` | 动作定义的只读索引；`GetOrNull` 返回 `null` 即未登记。写入时记录器据此抛错；读取历史记录时调用方据此降级（原样显示裸码） |
| `OperationRecordOptions` | `ImpersonatorIdClaimType` / `ImpersonatorNameClaimType` 默认值取自 `CustomClaimTypes`；操作人标识按 `ClaimTypeOptions.UserIds` 读取（`ICurrentUser.SubjectId`），不在这里另配 |
| `IOperationRecordWriter.InsertAsync(record, ct)` | 写入；成功记录只在它描述的变更生效之后可见（有环境工作单元时跟随它），失败记录立即、独立于调用方事务写出。一个宿主只有一个写入方 |
| `IOperationRecordReader.GetPagedListAsync(filter, page, ct)` | 只由可回读的存储实现； 按创建时间倒序分页，返回 `PagedResult<OperationRecordInfo>`；`OperationRecordFilter` 的 `Scope` 必填，关键字匹配动作码、目标标识与操作人名，时间两端都是**闭区间**且按 UTC 比较；`PageRequest.Sorting` 不生效 |
| `IOperationRecordQueryService` | 查询、筛选项与导出用例：无租户上下文即宿主读者；租户读者看不到 `Host` 层、`Actor` 层只看本人（`ActorId` 与 `ActorTenantId` 都与读者相同）；仅宿主字段（`FailureDetail`、`CorrelationId`、`ActorTenantId`）只下发给宿主读者；类别与动作维度间取交集，展开为空返回空页；`FailureMessage` 为按请求语言渲染的失败原因（导出另成 `FailureMessage` 列）。**不做权限判定**，由端点策略把守 |
| `MapOperationRecords(configure)` | AspNetCore 包：`GET /`、`GET /filter-options`、`GET /export`；`ReadPolicy`、`ExportPolicy`、`ExportAction` 必填；未注册查询用例（日志输出模式）时映射即抛错；返回路由组，端点名前缀见 `OperationRecordEndpoints.NamePrefix` |
| `AddOperationRecordRetention<TDbContext>(configure?)` | EF 包：绑定 `Leistd:OperationRecords:Retention` 并启动期校验，登记集群周期任务 `operation-records.archive`（每日 `DailyRunHourUtc` 执行） |
| `IOperationRecordArchiveService` | EF 包：逐库、分批把到期记录搬入 `OperationRecordArchive`，每批一个事务；返回搬运条数与失败库数 |
| `OperationRecordVisibilityScope` | 可见范围，**由调用方算好**，只能从三个入口取得：`Host` 见全部，`ForTenantReader(actorId, actorTenantId)` 见租户层加本人的 `Actor` 层（标识与所属租户都相同才算本人：主体标识只在签发它的那一层内唯一），`Unrestricted` 不过滤（仅供不代表读者的内部任务）。没有默认值——可见性是安全边界，漏传即越权。**存储不判定"谁是宿主"**——那需要它不该有的上下文依赖 |
| `AddOperationRecords(services)` | 注册记录器与动作定义；不注册写入方与查询 |
| `AddOperationRecordQueries(services)` | 注册 `IOperationRecordQueryService`；要求 `IOperationRecordReader`，由可回读的存储适配调用 |
| `AddOperationRecordsEfCore<TDbContext>(services)` | 注册 EF Core 存储（写入与读取）；内部调用 `AddOperationRecords()` 与 `AddOperationRecordQueries()` |
| `AddOperationRecordsLogging(services)` | Logging 包：注册结构化日志写入方，不注册读取与查询；启动期校验日志类别与工作单元前置 |
| `OperationRecordLogging` | Logging 包：日志类别 `CategoryName`、`DeliveryCategoryName` 与事件标识常量 |
| `ConfigureOperationRecords(modelBuilder)` | 映射 `OperationRecord` 与归档表 `OperationRecordArchive` |
| `IOperationActionDefinitionContext.Add(..., targetIsActor)` | 标记自证类动作：成功且没有操作人时，查询输出的 `ActorIsTarget` 为真，界面把目标显示在操作人列 |
| `[OperationRecordAction(action, params targetRouteKeys)]` | 声明写端点的动作码；`TargetIdPrefix` 可对齐成功路径的目标标识写法 |
| `HttpContext.RecordDeniedOperationAsync()` | 在授权结果处理器里补记一条失败；端点有注解才记，匿名请求（没有任何已认证身份）一律不记；授权依据取实际未通过的具名策略 |
| `HttpContext.RecordFailedOperationAsync(failure)` | 在紧接 `UseAuthorization()` 的中间件里补记授权通过之后的业务拒绝；判据同上，授权依据取最后声明的具名策略；`failure` 为空抛 `ArgumentException`。业务异常传 `OperationFailure.FromCode(exception.Code, exception.LocalizationData)`，参数进审计与导出 |

## 实现行为

- **时间由记录器填充，不依赖审计拦截器。** 审计属性的自动填充要宿主把 `AuditSaveChangesInterceptor` 挂到目标 DbContext，漏挂是静默的——得到的会是一张时间全为零的审计表，而"什么时间"塌掉整张表就没用了。实体因此也**不实现** `ICreationAuditedObject`。
- **操作人名存快照。** 用户改名或销号之后，靠标识反查到的要么是新名字、要么什么都没有，而审计要回答的是"当时是谁"。
- **操作人标识读 claim 原始值，不读 `ICurrentUser.Id`。** 后者只在 `sub` 能解析成 GUID 时有值，而机器主体（`client:<client_id>`）与后台作业主体都不是 GUID；只认 `Id` 会把这两类操作全部记成无主的，而它们恰恰是最需要事后追查的那批。读的是 `ICurrentUser.SubjectId`：按 `ClaimTypeOptions.UserIds`（默认 `sub`，其次 `NameIdentifier`）取原始值，与 `ICurrentUser.Id`、SignalR 寻址同一处配置。自证类动作（定义为 `targetIsActor`，如登录、注册）在匿名请求里完成，操作人取目标——否则这类 `Actor` 层记录没有操作人，本人永远看不到。
- **后台作业、消息消费者、Hub 调用先建立环境上下文。** 这些入口不经 ASP.NET Core 中间件，主体、租户与链路标识在其中都不成立，记录器会拿到一片空白。用 `IAmbientContext.Begin(principal)` 一次性建立全部已注册维度（只切主体的 `ICurrentPrincipalAccessor.Change` 不够——租户与链路不会跟着走）。主体的 `sub` 由宿主自定前缀（如 `job:nightly-cleanup`），`name` claim 决定记录里显示的操作人名。
- **模拟登录留两个人。** 主体上的用户是被模拟者，真实操作人按 `OperationRecordOptions` 指定的 claim 读出，另存 `ImpersonatorId` / `ImpersonatorName`。只记前者等于把真正按下按钮的人从审计里抹掉。
- **事务边界按结果区分**（以下为数据库存储；日志输出见「结构化日志输出」）。成功记录落在调用方所处的边界里，与它描述的变更同生共死，否则会留下"记了但没发生"的假账。失败记录在新开的工作单元里独立写入并提交：业务在工作单元内记完失败紧接着抛出、整体回滚是被拒路径最常见的形态，而"谁在反复做他不被允许的事"正是审计最要留住的。第二个事务只向本表插入一行、不读不改业务表，常规场景不与外层冲突；**只允许单个写事务的数据库（如 SQLite）是例外**：外层已有未提交的写入时，这次写入等锁直至超时，结果是一条 `Error` 日志、记录丢失。
- **失败路径写不进去只记 `Error`，不抛。** 被拒的请求本来就要以 403/400 结束，不能因为这一条审计没记下来变成 500，把真正的拒绝原因盖掉。写入不可取消：客户端中断请求不能让这条审计作废。
- **动作码与授权依据必填且不得为空白**（`ArgumentException`），**动作码必须已登记**（`InvalidOperationException`）。两项校验都在失败路径的 `try` 之外：那个 `catch` 吞的是"写库没成功"这类运行期故障，而这两类是确定性的编码错误，必须当场响。空串落库之后，"这次操作不需要授权依据"与"调用方漏传了"就再也分不开；未登记的码没有可见性可盖，默认给租户看是泄露，默认只给宿主看又会让租户上下文里写下的记录谁都看不见。
- **超长字段就地截断，并在截断前记 `Warning`。** 审计写入不能因为一个字段超长，把一次已经成功的业务操作变成 500。
- **可见性与记录所在的层。** `Tenant` 与 `Actor` 的记录写进操作发生时的租户层。`Host` 的记录属于宿主层：在宿主上下文里照常写；租户上下文里的**失败**记录（典型是租户用户调用宿主接口被拒）改写进宿主层，操作人所属租户记在 `ActorTenantId`——留在租户层的话，租户读者按可见性看不到、宿主按租户维度也查不到。租户上下文里的**成功**记录则直接抛错：它既不能留在租户层，也不能脱离租户库里的业务事务挪去宿主层；这类动作要么登记为 `Tenant`，要么在切到宿主上下文之后再记。
- **读取隔离靠全局查询过滤器，写入的 `TenantId` 由记录器显式盖章。** 实体是 `IMultiTenant`，存储查询不带租户条件；而写入不依赖宿主 DbContext 是否为 `BaseDbContext`——理由与时间戳相同，漏配是静默的，得到的会是一批归属为空的记录。
- **三个索引：`(TenantId, CreationTime DESC)`、`(TenantId, Visibility, CreationTime DESC)` 与单列 `CreationTime`**。这张表写多读少，读的形态是"某租户的最近若干条"；而可见性过滤在**每一次**查询里都出现（宿主之外的读者读不到 `Host` 层记录），不进索引会让这个必然出现的谓词退化成对时间区间结果集的逐行筛。第三条服务的是另一条访问路径——**保留期归档整库按时间扫、不带 `TenantId`**（`IgnoreQueryFilters()` 覆盖同库的全部租户），前两条都以 `TenantId` 打头，用不上。关键字检索走时间裁剪之后的过滤，**不**单独建索引——每加一个索引的代价都摊在每一次写入上。归档表只写不扫，因此没有这条。
- **分页按 `CreationTime` 再按 `Id` 倒序**。同一毫秒内的多条记录时间相同，只按时间排序会让分页出现重复或遗漏，次级键消除这种不确定。它保证的是**顺序确定**，不是"同一时刻内更新的在前"——后者取决于 Provider 怎么比较 Guid（PostgreSQL 的 `uuid` 按网络字节序，UUIDv7 的时间序成立；SQLite 把 Guid 存成 BLOB、按 .NET 字节布局比较，前三段是小端，时间序不成立）。分页需要的是前者。
- **失败原因存码、读时渲染。** 库里只有 `FailureCode` 与 `FailureData`；查询与导出时用容器里的**非泛型** `IStringLocalizer`（本地化组件的 `AddJsonLocalization` 会注册）按码取文案，用与错误响应同一套规则（`LocalizationPlaceholders.Fill`）填 `{Name}` 占位符，得到 `FailureMessage`；`{码}:Record` 优先于码本身，供审计换措辞（审计键在本地化器的整条文化回落链上都未命中才查码本身）。没有本地化器、没有码或词条缺失时为空；`FailureData` 解析不了按无参数渲染，不让一条坏记录拖垮整页。导出的 CSV 照旧保留码与参数两列，另加按导出请求语言渲染的 `FailureMessage` 列——文件离开系统后没有词条可查。
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
- **`IOperationRecordWriter` 只能有一个实现。** 为第二个 DbContext 注册、或数据库存储与日志输出同时注册时，在注册期直接拒绝：一半的审计写进宿主没预期的去处，比没有审计更危险。
- **框架不接管 `IAuthorizationMiddlewareResultHandler`。** 宿主只能注册一个，那里通常还承载着应用专有的处置；框架占住它，宿主唯一的授权处置入口就没了。同理也不替宿主挂业务拒绝的中间件：记哪些异常是宿主的策略，框架只给 `RecordFailedOperationAsync` 这个零件。
- **不要改用"发布领域事件、由处理器统一订阅"来取代记录器。** 这个方案覆盖不了三条记录路径里的两条：授权阶段的拒绝根本没进领域层，没有聚合能发事件；业务在工作单元内拒绝并抛出时，事件会随回滚一起丢——`ILocalEventBus.PublishAsync` 先问 `ILocalEventDeferrer`，只要有活动工作单元就推迟到提交后发布，因此**主动发布与实体收集两条路径殊途同归**。改用事件仍须保留直接写入器去覆盖那两条，最终是两套机制并存。此外"凭什么被允许"只有调用点知道，事件里没有这个信息。成功路径上业务当然可以自己用事件驱动，但那是宿主的选择，不是组件的机制。
- **被拒记录的唯一闸门是注解，唯一的例外是匿名请求。** 匿名不记不是偏好而是安全属性——那种请求没有操作人，记下来等于把审计表变成一个不需要凭据的写入面。除此之外框架不替调用方做取舍。

## 相关

- [多租户](./multi-tenancy.md)
- [当前用户与身份信息](./security.md)
- [链路追踪](./tracing.md)
