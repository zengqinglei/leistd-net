# 设置

按 用户 → 租户 → 代码默认值 的顺序解析设置。业务用 `ISettingDefinitionProvider` 声明有哪些设置，框架负责定义注册、层级回落与持久化。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 运行期可改、按租户或按用户各不相同的值（界面语言、时区、显示名） | 定义设置，注入 `ISettingProvider` 读取 |
| 部署期决定的值（连接串、签名与摘要密钥、协议契约、事务语义） | **不要用本组件**，用 `IOptions<T>` 与配置文件 |
| 只声明设置、只读取（应用层、领域服务） | 只引用 `Leistd.Settings.Core` |
| 需要持久化设置值 | 引用 `Leistd.Settings.EntityFrameworkCore` |
| 设置页：分层读写、值域校验、机密只写 | `Leistd.Settings.AspNetCore` 的 `MapSettings()` |
| 管理员在运行期改部署配置（日志级别、发信参数、第三方登录的客户端凭据），消费方照常读 `IOptionsMonitor<T>` | `Leistd.Settings.Hosting` 的 `AddHostSettings()` |
| 一个服务管理、其他服务只读的租户设置（安全策略开关、组织启用的语言） | 提供方定义上 `IsShared = true` 并 `MapSharedSettings()`；使用方 `Leistd.Settings.ServiceClient` 的 `AddSharedSettingsClient` 与 `ISharedSettingReader` |

连接与签名凭据、信任边界、协议参数和事务隔离级别属于部署配置，不应进入运行期设置。唯一的例外是管理员在运行期维护的第三方集成客户端凭据，见[注意事项](#注意事项)。

## 安装

```bash
# 定义、解析与写入契约
dotnet add package Leistd.Settings.Core

# EF Core 持久化
dotnet add package Leistd.Settings.EntityFrameworkCore

# 设置页端点
dotnet add package Leistd.Settings.AspNetCore

# 宿主级设置 → 配置源 → IOptionsMonitor
dotnet add package Leistd.Settings.Hosting

# 读取其他服务共享的设置
dotnet add package Leistd.Settings.ServiceClient
```

## 注册

```csharp
builder.Services.AddSettingsEfCore<AppDbContext>();   // 内部已调用 AddSettingsCore()
builder.Services.AddSingleton<ISettingDefinitionProvider, DisplaySettingDefinitionProvider>();
```

在 `OnModelCreating` 中映射设置表：

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigureSettings();
}
```

**前置**：宿主须已注册 `AddUnitOfWork()` 与 `AddUnitOfWorkEfCore()`——存储经 `IDbContextProvider<TDbContext>` 取当前工作单元的上下文并参与其事务；
还须注册分布式锁：单副本 `AddMemoryLocalLock()`，多副本 `AddRedisDistributedLock(...)`（进程内锁不跨副本互斥）。
默认写入入口靠这两者按租户串行、独立提交（见[实现行为](#实现行为)）。只读设置的宿主同样要注册：`AddSettingsCore()` 总会登记默认写入入口，
经 Host 启动时缺任何一项即启动失败；宿主替换了 `ISettingManager` 时不检查。

`AddSettingsCore()` 已登记本组件错误码的非默认 HTTP 状态（具体映射见[接口参考](#接口参考)），宿主不需要另行组合。

设置页端点与显示名翻译：

```csharp
builder.Services.AddSettingsCore(options => options.LocalizationResource = typeof(AppResource));

app.MapGroup("/api/v1/settings").MapSettings(options =>
{
    // 读设置、改自己的偏好
    options.AccessPolicy = "App.CurrentUser";
    // 改租户（宿主上下文下为进程级）设置
    options.TenantWritePolicy = "App.Settings";
    // 可选：某个分组的管理权另交给持有该策略的人（同时还要满足 TenantWritePolicy）
    options.GroupPolicies["Email"] = "App.Settings.Email";
});
```

宿主级设置覆盖部署配置（需要宿主注册周期任务调度器，如 `AddInProcessBackgroundJobs()`）：

```csharp
builder.Services.AddHostSettings(bindings => bindings
    .Bind("Logging.MinimumLevel", ["Logging:LogLevel:Default"], fallback: "Information")
    .BindOption<ExportOptions>("Export.Endpoint", ExportOptions.SectionName, nameof(ExportOptions.Endpoint)));

var app = builder.Build();
app.UseHostSettings();   // 构建之后挂配置源，漏了启动失败
```

组合根拆分时可多次调用 `AddHostSettings`：同一设置的绑定以设置名、按顺序的配置键、兜底值与选项类型判定是否相同，相同即不重复生效，不同则在调用 `AddHostSettings` 时抛出 `InvalidOperationException` 并列出两处登记。

## 使用

声明设置：

```csharp
public class DisplaySettingDefinitionProvider : ISettingDefinitionProvider
{
    public void Define(ISettingDefinitionContext context)
    {
        // 租户与用户都可覆盖，且允许下发到客户端
        context.Add("Display.TimeZone", defaultValue: "Asia/Shanghai", scopes: SettingScopes.All)
               .IsVisibleToClients = true;

                // 只允许租户级覆盖，且不下发客户端
        context.Add("Export.MaxRowsPerFile", defaultValue: "50000", scopes: SettingScopes.Tenant);

                // 进程级：只允许在宿主上下文读写
        context.Add(
            "Logging.MinimumLevel",
            defaultValue: "Information",
            scopes: SettingScopes.Host,
            group: "Logging")
            .WithAllowedValues("Debug", "Information", "Warning", "Error")
            .IsVisibleToClients = true;

        // 值元数据：写入端据此校验，设置页据此渲染开关与带上下界的数字框
        context.Add("Security.RequireTwoFactor", "false").AsBoolean().IsVisibleToClients = true;
        context.Add("Security.LockoutDurationMinutes", "15").AsInteger(1, 1440).IsVisibleToClients = true;
    }
}
```

值元数据表达不了的规则写成校验器（按设置名自行判断是否适用，不合法时抛带码的业务异常）：

```csharp
internal sealed class TimeZoneSettingValidator : ISettingValueValidator
{
    public Task ValidateAsync(SettingValueValidationContext context, CancellationToken cancellationToken = default)
    {
        if (context.Definition.Name == "Display.TimeZone"
            && !(TimeZoneInfo.TryFindSystemTimeZoneById(context.Value, out var zone) && zone.HasIanaId))
        {
            throw new BusinessException(
                    "AppSetting:TimeZoneInvalid",
                    $"'{context.Value}' is not an IANA time zone id.")
                .WithData("Value", context.Value);
        }

        return Task.CompletedTask;
    }
}

builder.Services.AddTransient<ISettingValueValidator, TimeZoneSettingValidator>();
```

读取当前生效值：

<!-- no-compile: 省略号代表与本例无关的参数和实现 -->
```csharp
public class ReportService(ISettingProvider settings)
{
    public async Task<string> RenderAsync(CancellationToken ct)
    {
        var timeZone = await settings.GetOrNullAsync("Display.TimeZone", ct);
        var maxRows = await settings.GetAsync<int>("Export.MaxRowsPerFile", ct);
        // 进程级设置只在宿主上下文读得到；租户请求里读它会抛 HostScopeUnavailableException
        var logLevel = await settings.GetOrNullAsync("Logging.MinimumLevel", ct);
        ...
    }
}
```

写入：

```csharp
// 当前用户的偏好
await settingManager.SetAsync("Display.TimeZone", "Asia/Tokyo", SettingScopes.User, currentUser.Id?.ToString(), ct);

// 租户默认值
await settingManager.SetAsync("Export.MaxRowsPerFile", "10000", SettingScopes.Tenant, cancellationToken: ct);

// 清除该层级的值，读取回落到下一层
await settingManager.SetAsync("Display.TimeZone", null, SettingScopes.User, userId, ct);
```

替别人判断时读目标用户的生效值（例如按收件人偏好决定是否投递）：

```csharp
var wantsEmail = await settings.GetOrNullForUserAsync("Notifications.System.Email", recipientId, ct);
```

要在写入后做别的事（审计、同步），处理 `SettingChangedEvent`；它在本次写入提交后分发，调用方外层随后回滚也不影响它：

```csharp
public sealed class SettingChangeLogger(ILogger<SettingChangeLogger> logger) : IEventHandler<SettingChangedEvent>
{
    public Task HandleAsync(SettingChangedEvent e, CancellationToken ct = default)
    {
        if (e.Scope != SettingScopes.User)
        {
            logger.LogInformation("Setting {Name} changed at {Scope}", e.Name, e.Scope);
        }

        return Task.CompletedTask;
    }
}
```

### 跨服务共享设置

提供方在定义上标记共享，映射一个只认机器主体的端点：

```csharp
public class StepUpSettingDefinitionProvider : ISettingDefinitionProvider
{
    public void Define(ISettingDefinitionContext context)
    {
        context.Add("Security.StepUp.Enabled", "true").AsBoolean().IsShared = true;
        context.Add("Security.StepUp.WindowMinutes", "15").AsInteger(1, 60).IsShared = true;
    }
}
```

```csharp
app.MapGroup("/api/v1/internal/shared-settings")
    .MapSharedSettings(options => options.Policy = "App.SharedSettings.Read");
```

使用方登记提供方，逐项声明读不到时的兜底值，并在返回的构建器上先挂弹性管道、再按[服务客户端](./service-client.md#注册)挂机器认证（弹性管道在外层，总时限才包住取令牌）：

<!-- no-compile: 弹性管道来自 Microsoft.Extensions.Http.Resilience，不在本家族闭包内 -->
```csharp
public sealed class IdentitySharedSettingsOptions : SharedSettingsClientOptions;

var identity = builder.Services.AddSharedSettingsClient<IdentitySharedSettingsOptions>(
    "IdentitySharedSettings",
    shared => shared
        .Add("Security.StepUp.Enabled", fallback: "true")
        .Add("Security.StepUp.WindowMinutes", fallback: "15"),
    configSectionPath: "Leistd:ServiceClients:Identity");
identity.AddStandardResilienceHandler();
// 随后挂机器认证，配置节同上
```

按当前租户读取；需要知道值是否可信时看来源：

<!-- no-compile: reader 为注入的 ISharedSettingReader -->
```csharp
var enabled = await reader.GetAsync<bool>("IdentitySharedSettings", "Security.StepUp.Enabled", ct);
var window = await reader.GetAsync("IdentitySharedSettings", "Security.StepUp.WindowMinutes", ct);
if (window.Source == SharedSettingSource.Fallback) { /* 读不到，用的是兜底值 */ }
```

## 接口参考

| 成员 | 说明 |
| --- | --- |
| `ISettingDefinitionProvider.Define(context)` | 业务声明有哪些设置 |
| `ISettingDefinitionProvider.PostDefine(context)` | 可选：全部 `Define` 之后调用，用于调整别处声明的定义 |
| `ISettingDefinitionContext.Add(name, defaultValue?, scopes?, displayName?)` | 添加定义；名称全局唯一 |
| `ISettingDefinition` | `Name`、`DisplayName`、`DefaultValue`、`Scopes`、`Group`、`IsVisibleToClients`、`IsEncrypted`，值元数据 `ValueType`、`Minimum`、`Maximum`、`AllowedValues`；`DisplayName` 与 `Group` 都原样返回不翻译 |
| `AsBoolean()` / `AsInteger(min, max)` / `WithAllowedValues(...)` | 声明值元数据的链式写法 |
| `ISettingValueValidator.ValidateAsync(context, ct)` | 业务取值校验，写入前调用；清除时以清除后会生效的值调用 |
| `SettingChangedEvent` | 写入或清除后发布：`Name`、`Scope`、`UserId`，不带值 |
| `SettingScopes` | `Tenant`、`User`、`All`、`Host`、`None`；代码默认值不在其中 |
| `ISettingDefinitionManager` | 汇总全部定义并提供查询 |
| `ISettingProvider.GetOrNullAsync(name, ct)` | 读取当前生效值 |
| `ISettingProvider.GetAsync<T>(name, ct)` | 读取并转换类型 |
| `ISettingProvider.GetAllAsync(visibleToClientsOnly?, ct)` | 读取全部生效值 |
| `ISettingProvider.GetOrNullForUserAsync(name, userId, ct)` | 读取指定用户在当前租户下的生效值 |
| `ISettingManager.SetAsync(name, value, scope, userId?, ct)` | 写入；`value` 为 `null` 时清除该层级。默认实现按租户串行、每次独立提交 |
| `ISettingStore` | 持久化契约；只按层级读写原始字符串，不做校验与回落；须经当前工作单元读写并参与其事务 |
| `ISettingManagementService` | 设置页用例：`GetAsync`、`SetForCurrentUserAsync`、`SetForCurrentTenantAsync` |
| `SettingOutputDto` / `SetSettingInputDto` | 设置页的读写形状 |
| `SettingErrorCodes` | 组件抛出的错误码，默认中英译文随包分发 |
| `SettingEncryptionOptions.DataProtectionProvider` | 机密设置专用的 Data Protection；不设时用宿主的 |
| 默认 HTTP 状态 | 组件默认状态：宿主限定与身份拒绝 → 403，不可见设置 → 404。由 `AddSettingsCore()` 自动登记；宿主 `MapCode` 可覆盖 |
| `AddSettingsCore(services, configure?)` | 注册定义管理器、解析器、写入入口、设置页用例与写入依赖的启动检查；`SettingManagementOptions` 给翻译资源与默认分组 |
| `AddSettingsEfCore<TDbContext>(services)` | 注册 EF Core 存储；内部调用 `AddSettingsCore()` |
| `ConfigureSettings(modelBuilder)` | 映射 `SettingRecord` 实体 |
| `MapSettings(configure)` | AspNetCore 包：`GET /`、`PUT /current-user`、`PUT /current-tenant`；`AccessPolicy` 与 `TenantWritePolicy` 均必填（组件不套宿主默认策略），`GroupPolicies` 可选（分组 → 策略名），端点名前缀 `SettingEndpoints.NamePrefix` |
| `ISettingDefinition.IsShared` | 共享给其他服务只读；只允许非机密、允许租户层、非进程级的设置，否则首次访问定义时抛 `InvalidOperationException` |
| `MapSharedSettings(configure)` | AspNetCore 包：`GET /{tenant}`（`host` 或租户 Id）返回 `SharedSettingsDto` 与强 ETag；`Policy` 必填，端点名 `SharedSettingEndpoints.GetName` |
| `SharedSettingsDto` | 共享端点的响应：`Values`（设置名 → 生效值）；两端按 `JsonSerializerDefaults.Web` 序列化 |
| `AddSharedSettingsClient<TOptions>(provider, declare, configure?, configSectionPath?)` | ServiceClient 包：登记提供方与每项兜底值，返回该提供方的 `IHttpClientBuilder`；同一提供方只能登记一次 |
| `ISharedSettingReader.GetAsync(provider, name, ct)` / `GetAsync<T>` | Scoped，按调用方作用域的当前租户读取（缓存进程内共享），返回 `SharedSettingValue(Value, Source)`；`Source` 为 `Current`、`Stale` 或 `Fallback`。未登记的提供方或未声明的设置名抛 `InvalidOperationException` |
| `SharedSettingsClientOptions` | `Path`、`FreshFor`、`MaxStaleness`，另有服务客户端的 `BaseAddress` 等；每个提供方派生一个具体类型 |
| `AddHostSettings(bind, configSectionPath?)` / `UseHostSettings()` | Hosting 包：声明绑定并注册应用与刷新，刷新周期绑定 `configSectionPath`（默认 `Leistd:Settings:Hosting`），重复调用时不同设置的绑定累加、完全相同的绑定不重复生效，同一设置名绑定不同（含只换兜底值或键的顺序）或换用另一配置节时在该次调用抛出；构建后挂配置源 |
| `HostSettingBindingBuilder.Bind(name, keys, fallback?)` / `BindOption<TOptions>(name, section, property)` | 设置 → 配置键；后者兜底值取选项类型上的属性默认值，并参与整组校验 |

## 配置项（Leistd:Settings:Hosting）

| 键 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `RefreshInterval` | `TimeSpan` | `00:00:30` | 其它实例上改的宿主级设置多久在本实例生效；不小于 1 秒 |

## 配置项（Leistd:ServiceClients:{提供方}）

`AddSharedSettingsClient` 绑定的配置节，默认 `Leistd:ServiceClients:{provider}`，可用 `configSectionPath` 指向已有的服务配置节。启动时校验，消息按实际路径报键。

| 键 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `BaseAddress` | `string` | — | 提供方基址，同服务客户端 |
| `Path` | `string` | `api/v1/internal/shared-settings` | 提供方 `MapSharedSettings` 路由组的相对路径 |
| `FreshFor` | `TimeSpan` | `00:01:00` | 新鲜期，期内不回源；须大于 0 |
| `MaxStaleness` | `TimeSpan` | `01:00:00` | 新鲜期结束后、提供方暂不可达时还能用旧值的时长；0 表示不用旧值 |

## 实现行为

- 回落顺序为用户级 → 租户级 → 代码默认值；宿主视角走租户级那一层（`TenantId` 为 `null` 的行）。定义未允许的层级即使库里有值也不参与回落。
- `SettingScopes.Host` 是进程级：整个进程只有一份值，不与其它层级组合（`Host | User` 在定义阶段抛 `ArgumentException`）。
  它与宿主的租户级共用同一行（`ScopeKey` 为 `host:t`），读写只允许在宿主上下文，租户上下文下存储就地抛异常。
- 进程级设置不接在回落链上：宿主上下文读宿主那一行，没有值才用代码默认值；租户上下文下 `GetOrNullAsync` 抛 `HostScopeUnavailableException`，
  `GetAllAsync` 不包含它，不返回代码默认值。存储由 `ISettingStore.CanAccessHostScope` 回答可达性。
- 写入按租户串行、独立提交：默认写入入口先取 `IDistributedLock`（键 `settings:{租户 Id}`，宿主为 `settings:host`，Redis 锁的 `KeyPrefix` 照常加在前面；用户层与租户层共用一把），
  再以 `requiresNew` 开一个事务型工作单元（隔离级别与超时沿用宿主的工作单元默认选项）。锁内依次：作废同一作用域 `ISettingProvider` 的记忆化 →
  取值校验与 `ISettingValueValidator` → 存储写入 → 再次作废记忆化 → 发布 `SettingChangedEvent`（进入本次工作单元的提交后队列）→ 提交；
  提交与提交后处理器都结束之后才释放锁。校验器经 `ISettingProvider` 读另一项设置时，读到的是锁内最新已提交的值；同一作用域先写后读读到新值。
- 不并入调用方的工作单元：返回即已提交，外层随后回滚不撤销设置；同一外层里的两次写入各自提交，后一次失败不撤销前一次。
  校验或存储失败时本次回滚，不落库、不发事件。锁获取失败或等待中取消原样上抛；`ILockHandle.LockLost` 与调用方取消合并，传给锁内的读写、校验器与提交前检查；
  提交开始之后沿用工作单元的不可逆边界。
- 写入校验顺序：空串（`Setting:EmptyValueRejected`，清除只用 `null`）→ 值类型与区间（`BooleanRequired`、`IntegerRequired`、`ValueOutOfRange`）→
  候选值（`ValueNotAllowed`，按序号比较）→ 宿主注册的 `ISettingValueValidator`；任何一步不过都不落库、不发事件。
  清除不做值元数据校验，但以清除后会生效的明文值调用 `ISettingValueValidator`：用户层回落到当前租户的租户层值（定义允许时），否则为代码默认值；
  租户层与宿主层为代码默认值（被绑定的宿主级设置即部署基线）。跨设置的约束因此不会被一次清除绕过；清除后没有任何值时不调用校验器。
  宿主机密设置的部署配置回落在消费端完成，校验器拿不到它。
  错误提示的 `{Name}` 按 `Setting:{设置名}` 查 `LocalizationResource`，查不到用定义上的 `DisplayName`，再没有才用设置名；`Setting:Undefined`、`Setting:NotAvailable` 回显调用方传入的名字。
- 写入提交后分发 `SettingChangedEvent`（注册了本地事件总线时），事件不带值。处理器运行时锁仍被持有；处理器失败时值已提交，异常上抛给写入方，
  不代表写入被回滚，也不重试。没有事件总线时，宿主级设置写入后要等下一轮周期刷新才在本进程生效。
- 设置页用例按层给出原始覆盖值，不给回落后的生效值。只处理 `IsVisibleToClients` 的设置（不可见的读不到、写入 404）；租户上下文不下发进程级设置，
  写入它返回 403（`Setting:HostOnly`）；进程级设置的值放在 `TenantValue`；机密设置不下发任何值，只给 `HasSecretValue`。
- 管理授权由端点判定：`CanManage = 定义允许租户或进程级 && TenantWritePolicy 通过 && (分组未登记策略 || 分组策略通过)`，一次请求内按分组复用授权结果。
  `PUT /current-tenant` 按同一判据。策略经官方策略评估器评估：策略自带的认证方案照常生效，资源与端点授权一样是当前 `HttpContext`；
  分组策略不满足时按该策略的方案质询（401）或拒绝（403）。未定义或不可见的设置先按 404 处理，不因所在分组的策略变成 403。`GET /` 只下发能管理的设置与允许用户覆盖的设置：`IsVisibleToClients` 是进入设置页的必要条件，
  不再代表所有已登录用户都读得到租户层与进程级的管理值。直接调用 `ISettingManagementService` 时 `CanManage` 恒为 `false`。
  显示名按 `Setting:{设置名}`、分组按 `SettingGroup:{分组}` 查 `LocalizationResource`，查不到回落到定义文案；未分组归入 `DefaultGroup`。用例不查权限，由端点按上一条判定。
- 宿主级设置经配置源进入 Options（Hosting 包）：设过的宿主级设置作为优先级最高的配置源覆盖绑定的配置键，没设的回落到部署配置；
  机密设置在进程内解密后进配置。三处推进：宿主开始接收请求之前一次、写入宿主级设置的事务提交后本进程立即一次、每个副本上的 `EveryInstance` 周期任务 `settings.host-refresh`。
- 整组原子生效：推进后按 `BindOption<TOptions>` 涉及的选项类型逐个校验，任何一个不合规就整组退回上一组；与当前一组或上次被拒的一组相同时不做任何事。
- 推进失败不抛异常、不阻断写入（值已落库）：启动时记告警并沿用部署配置，写入提交后的那次记告警、下一轮周期刷新再试，周期刷新记错误。
  观察日志里的 `keeping the previous values`，而不是保存接口的返回值。
- 被绑定设置的代码默认值是部署基线：在 `PostDefine` 阶段逐个配置提供程序查绑定的键（跳过宿主设置配置源本身，后加入的源优先），
  按定义的值元数据归一，认不出时用兜底值；机密设置没有默认值。被绑定的设置必须已定义且是进程级，否则首次访问定义时抛出。
- `DisplayName` 与 `Group` 原样返回、不翻译（定义只加载一次，拿不到请求 culture）；`Group` 只承载分组标识，未分组返回 `null`。
- 机密设置（`IsEncrypted`）：`ISettingManager` 写入前加密，`ISettingProvider` 读出落库值时解密，默认用宿主的 Data Protection；宿主须配置持久化、可共享的密钥环并纳入备份。
  未注册 Data Protection 或密文无法解密时抛 `InvalidOperationException`（API 边界为 500），不回落默认值。
- 应用名不同的多个宿主读写同一批机密设置（如后台写、门户读）时，不要为此合并宿主的应用名：那会让各宿主的 Cookie、防伪令牌互相解得开。
  给每个宿主设同一个专用实例，即同一密钥存储、同一应用名，写法同官方 `CookieAuthenticationOptions.DataProtectionProvider`：

  ```csharp
  builder.Services.Configure<SettingEncryptionOptions>(options =>
      options.DataProtectionProvider = DataProtectionProvider.Create(
          new DirectoryInfo("/var/lib/myproject/setting-keys"),
          dataProtection => dataProtection.SetApplicationName("MyProject.Settings"),
          keyEncryptionCertificate));
  ```

  这个密钥环里的密钥要用来解长期保存的密文：密钥存储要持久（Redis 默认重启不保留）、纳入备份、不删除过期密钥；
  指定了存储位置后默认的密钥静态加密不再自动生效，要显式配置（如上例的证书）。共用密钥存储的宿主都能解这批机密，存储的访问权限就是边界。
  切换到专用实例后，宿主密钥环加密的旧值解不开，要重新写入。
  `Leistd.Settings.Core` 因此引用 `Microsoft.AspNetCore.DataProtection.Abstractions`，这是纯抽象包，非 Web 宿主同样可用；架构门禁可按 `*.Abstractions` 放行。
- 共享端点按路径里的租户读取：注册了 `ITenantStore` 时租户须存在且启用，没有注册多租户时只接受 `host`，其余一律 404。
  切到该租户后开新工作单元读存储，值为租户层的值、没有时为代码默认值；共享设置不会有个人覆盖、进程级或密文，所以不经 `ISettingProvider`，也不受调用方主体的影响。
  ETag 取按名称排序后响应体的 SHA-256，`If-None-Match` 命中回 304。
- 共享读取器按（提供方，租户）在进程内缓存整份值（HybridCache 一级，同键并发回源合并为一次），新鲜期过后带上一次的 ETag 重验证。
  提供方暂不可达（传输失败、超时、408/429/5xx）时：有保留期内的旧值就用它，单次回源最多等 1 秒，超时先交旧值、原回源继续并写回，同键节流 30 秒，每个节流周期一条 Warning；
  没有旧值时返回兜底值、不缓存失败，下一次照常回源。明确失败（取令牌被拒、401、403、响应无效）清掉旧值并记 Error，404（租户不存在）记 Warning，都返回兜底值，
  之后再遇暂时故障也不恢复旧值。提供方没有共享某项已声明的设置时该项返回兜底值，每次回源记一条 Warning。除取消与调用错误外读取器不抛异常。
- 匿名调用只回落到租户级，不查用户级。
- `ISettingProvider` 为 Scoped，一次请求内每个租户、每个用户的每个层级只查一次库并复用结果：缓存按 `ICurrentTenant`（未注册多租户时按宿主一份）与当前用户分键，
  同一作用域里 `CurrentTenant.Change` 后读到目标租户的值。它只选对快照；持久化连到哪个租户的库仍由工作单元决定（一个工作单元不能改变连接归属）。
  `ISettingDefinitionManager` 为 Singleton。
- 设置名重复在首次访问定义时失败。读写未定义名称抛 `UndefinedSettingException`；写入未允许层级抛 `SettingScopeNotAllowedException`。
- 层级由 `SettingRecord` 的 `TenantId`（多租户查询过滤器隔离）与 `UserId`（`null` 即租户级）共同表达，没有独立的 Scope 列。
- 唯一索引为 `(ScopeKey, Name)`；`ScopeKey` 由租户和层级派生，可空的 `TenantId`/`UserId` 因此不影响唯一约束。
- 表名沿用宿主 `DbSet` 属性名，组件不写死。
- 并发写入同一层级同一名称：首插撞唯一索引、改写或删除时行已被并发删除，EF 存储只撤下本次条目（不动宿主同一上下文里的其他待写实体），
  在当前上下文与事务内不跟踪地重读后重试一次——赢家已在就改它的值、要改的行已被删就重新插入、要删的行已不在即成功、删除期间被重建也再删一次（删除即“清空本键”）。
  结果收敛到存储实际写入的先后，不保证按请求到达顺序。首插失败而重读确认不到同键行、其他实体的失败、非并发类的更新失败、第二次仍冲突都原样抛出；取消照常抛出。
  外层事务下依赖 EF 自动保存点回滚落败的那次保存：可串行化等更高隔离级别、关闭自动保存点（`AutoSavepointsEnabled = false`）或 SQL Server MARS 下不保证恢复，
  高竞争下单次重试也可能再次失败。落败那次保存的数据库错误日志（EF 的命令失败与保存失败日志）仍会输出，组件不压制。

## 按用户时区展示时间

时间一律以 UTC 存储，只在展示时换算。换算用的时区由设置提供，约定如下；具体渲染由宿主决定。

| 项 | 约定 |
| --- | --- |
| 设置名 | `Display.TimeZone` |
| 值 | **只接受 IANA 时区名**（如 `Asia/Shanghai`）。`UTC` 本身也是合法 IANA 标识 |
| 清除覆盖 | 写入 `null` 只清掉当前层，读取继续向下一层回落（用户 → 租户 → 代码默认值） |
| 最终缺值 | 三层都没有值时由消费端明确选择回退值 |
| 层级 | `SettingScopes.All`：租户给默认值，用户可各自覆盖 |

写入端必须用 `TimeZoneInfo.HasIanaId` 将值域限制为 IANA；只调用 `TryFindSystemTimeZoneById` 会同时接受浏览器不支持的 Windows 时区 ID。

设置组件本身不做时区转换；项目模板给出了服务端与前端两侧的做法。

## 注意事项

- 设置值一律是字符串；`GetAsync<T>` 用不变文化转换，复杂结构自行序列化。
- 跨请求的变更下一请求可见，请求内不可见；不使用分布式缓存。
- `ISettingStore` 只能有一个实现，为第二个 DbContext 注册时在注册期拒绝。
- 管理员在运行期维护的第三方集成客户端凭据可以做成设置，口令与密钥定义为机密设置：整个部署共用的（发信账号与口令、外部登录的 Client ID 与 Client Secret）用宿主级，
  各租户各自开通的（如每个租户自己的人机验证密钥）用租户级；连接串、签名与摘要密钥留在配置提供程序与密钥管理服务。机密设置的界面应只写不回显。
- 业务写入经 `ISettingManager`；直接操作存储或实体会绕过定义校验，也不受按租户串行的保护。
- 串行保证的范围：同一锁域内、相关写入都经默认写入入口、读取器与存储按当前工作单元新鲜读取时，候选值校验到提交之间不会插入另一次写入。
  直接写 `ISettingStore`、外部 SQL、宿主换成自带缓存的读取器都不在其内；它也不维护"其他用户的生效组合"这类规则（例如租户改了默认值，
  让某个已覆盖另一项的用户组合违序），校验器只看当前写入者的视角。
- 需要设置与业务数据同一事务时不要依赖外层工作单元：设置总是独立提交，外层回滚不会撤销它。
- 同一租户的写入互相等待，包括各用户的个人偏好；提交后处理器（审计、宿主级设置的即时应用）在锁内运行，会延长持锁时间。
  处理器里不要同步等待同一租户的另一次 `SetAsync`：它要等当前写入释放锁，而当前写入在等处理器结束。
- 自定义 `ISettingStore` 须经当前工作单元读写并参与它的事务，读取反映数据库当前值、不复用已跟踪实体或自带缓存；否则写入失败不会整体回滚，串行校验也可能读到旧值。
- 只声明运行期可改的业务偏好，以及管理员运行期维护的第三方集成客户端凭据；连接串、签名密钥、认证协议参数（回调路径、scope、颁发者、证书）属于部署期配置。已经是实体字段的东西不要再定义成设置。
- 框架认识的值域（类型、区间、候选）写进定义，其余规则（时区、邮箱地址、开启前提）实现 `ISettingValueValidator`，不要只在界面上限制。
- 宿主级设置刷新需要调度器：宿主未注册 `AddInProcessBackgroundJobs()` 之类的调度器时，其它实例上的修改只在重启后生效。
- 在租户请求里读进程级配置时，注入经 Hosting 包绑定的 `IOptionsMonitor<T>`，不要读设置存储。
- 共享设置只用于租户级、只读的策略与组织配置。用户级的值、使用方要写回的值仍走提供方的专用端点（如账户偏好）；共享名单只扩大可读范围，不改变谁能写。
- 兜底值按设置语义选：安全策略取更严格的一侧，显示类取部署基线。生效延迟最长为 `FreshFor`，提供方故障期间最长为 `FreshFor + MaxStaleness`；不做变更推送。

## 相关

- [多租户](./multi-tenancy.md)
- [后台作业](./background-jobs.md)
- [当前用户与身份信息](./security.md)
