# 设置

按 用户 → 租户 → 代码默认值 的顺序解析设置。业务用 `ISettingDefinitionProvider` 声明有哪些设置，框架负责定义注册、层级回落与持久化。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 运行期可改、按租户或按用户各不相同的值（界面语言、时区、显示名） | 定义设置，注入 `ISettingProvider` 读取 |
| 部署期决定的值（连接串、密钥、协议契约、事务语义） | **不要用本组件**，用 `IOptions<T>` 与配置文件 |
| 只声明设置、只读取（应用层、领域服务） | 只引用 `Leistd.Settings.Core` |
| 需要持久化设置值 | 引用 `Leistd.Settings.EntityFrameworkCore` |
| 设置页：分层读写、值域校验、机密只写 | `Leistd.Settings.AspNetCore` 的 `MapSettings()` |
| 管理员在运行期改部署配置（日志级别、发信参数），消费方照常读 `IOptionsMonitor<T>` | `Leistd.Settings.Hosting` 的 `AddHostSettings()` |

凭据、信任边界、解析协议和事务隔离级别属于部署配置，不应进入运行期设置。

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

**前置**：宿主须已注册 `AddUnitOfWork()` 与 `AddUnitOfWorkEfCore()`——存储经 `IDbContextProvider<TDbContext>` 取上下文。

设置页端点与显示名翻译：

```csharp
builder.Services.AddSettingsCore(options => options.LocalizationResource = typeof(AppResource));

app.MapGroup("/api/v1/settings").MapSettings(options =>
{
    // 读设置、改自己的偏好
    options.AccessPolicy = "App.CurrentUser";
    // 改租户（宿主上下文下为进程级）设置
    options.TenantWritePolicy = "App.Settings";
});
```

宿主级设置覆盖部署配置（需要宿主注册周期任务调度器，如 `AddInProcessBackgroundJobs()`）：

```csharp
builder.Services.AddHostSettings(bindings => bindings
    .Bind("Logging.MinimumLevel", ["Serilog:MinimumLevel", "Serilog:MinimumLevel:Default"], fallback: "Information")
    .BindOption<SmtpOptions>("Email.SmtpHost", SmtpOptions.SectionName, nameof(SmtpOptions.Host)));

var app = builder.Build();
app.UseHostSettings();   // 构建之后挂配置源，漏了启动失败
```

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

        // 只允许租户级覆盖，且不下发客户端（运维阈值不该出现在界面上）
        context.Add("Export.MaxRowsPerFile", defaultValue: "50000", scopes: SettingScopes.Tenant);

        // 进程级：一个进程只有一个 logger，按租户各存一份无从生效。只允许在宿主上下文读写
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
            throw new BadRequestException($"'{context.Value}' is not an IANA time zone id.")
                .WithCode("Setting:TimeZoneInvalid").WithData("Value", context.Value);
        }

        return Task.CompletedTask;
    }
}

builder.Services.AddTransient<ISettingValueValidator, TimeZoneSettingValidator>();
```

读取当前生效值：

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

要在写入后做别的事（审计、同步），处理 `SettingChangedEvent`；在工作单元内写入时，它在提交后分发：

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

## 接口参考

| 成员 | 说明 |
| --- | --- |
| `ISettingDefinitionProvider.Define(context)` | 业务声明有哪些设置 |
| `ISettingDefinitionProvider.PostDefine(context)` | 可选：全部 `Define` 之后调用，用于调整别处声明的定义 |
| `ISettingDefinitionContext.Add(name, defaultValue?, scopes?, displayName?)` | 添加定义；名称全局唯一 |
| `ISettingDefinition` | `Name`、`DisplayName`、`DefaultValue`、`Scopes`、`Group`、`IsVisibleToClients`、`IsEncrypted`，值元数据 `ValueType`、`Minimum`、`Maximum`、`AllowedValues`；`DisplayName` 与 `Group` 都原样返回不翻译 |
| `AsBoolean()` / `AsInteger(min, max)` / `WithAllowedValues(...)` | 声明值元数据的链式写法 |
| `ISettingValueValidator.ValidateAsync(context, ct)` | 业务取值校验，写入前调用，清除不经过它 |
| `SettingChangedEvent` | 写入或清除后发布：`Name`、`Scope`、`UserId`，不带值 |
| `SettingScopes` | `Tenant`、`User`、`All`、`Host`、`None`；代码默认值不在其中 |
| `ISettingDefinitionManager` | 汇总全部定义并提供查询 |
| `ISettingProvider.GetOrNullAsync(name, ct)` | 读取当前生效值 |
| `ISettingProvider.GetAsync<T>(name, ct)` | 读取并转换类型 |
| `ISettingProvider.GetAllAsync(visibleToClientsOnly?, ct)` | 读取全部生效值 |
| `ISettingProvider.GetOrNullForUserAsync(name, userId, ct)` | 读取指定用户在当前租户下的生效值 |
| `ISettingManager.SetAsync(name, value, scope, userId?, ct)` | 写入；`value` 为 `null` 时清除该层级 |
| `ISettingStore` | 持久化契约；只按层级读写原始字符串，不做校验与回落 |
| `ISettingManagementService` | 设置页用例：`GetAsync`、`SetForCurrentUserAsync`、`SetForCurrentTenantAsync` |
| `SettingOutputDto` / `SetSettingInputDto` | 设置页的读写形状 |
| `SettingErrorCodes` | 组件抛出的错误码，默认中英译文随包分发 |
| `AddSettingsCore(services, configure?)` | 注册定义管理器、解析器、写入入口与设置页用例；`SettingManagementOptions` 给翻译资源与默认分组 |
| `AddSettingsEfCore<TDbContext>(services)` | 注册 EF Core 存储；内部调用 `AddSettingsCore()` |
| `ConfigureSettings(modelBuilder)` | 映射 `SettingRecord` 实体 |
| `MapSettings(configure)` | AspNetCore 包：`GET /`、`PUT /current-user`、`PUT /current-tenant`；`AccessPolicy` 与 `TenantWritePolicy` 均必填（组件不套宿主默认策略），端点名前缀 `SettingEndpoints.NamePrefix` |
| `AddHostSettings(bind)` / `UseHostSettings()` | Hosting 包：声明绑定并注册应用与刷新；构建后挂配置源 |
| `HostSettingBindingBuilder.Bind(name, keys, fallback?)` / `BindOption<TOptions>(name, section, property)` | 设置 → 配置键；后者兜底值取选项类型上的属性默认值，并参与整组校验 |

## 配置项（Leistd:Settings:Hosting）

| 键 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `RefreshInterval` | `TimeSpan` | `00:00:30` | 其它实例上改的宿主级设置多久在本实例生效；不小于 1 秒 |

## 实现行为

- 回落顺序为 **用户级 → 租户级 → 代码默认值**。宿主视角走租户级那一层（`TenantId` 为 `null` 的行），不额外引入「全局」层。
- 定义未允许的层级即使库里有值也不参与回落——改一次 `Scopes` 不会让历史遗留行悄悄重新生效。
- `SettingScopes.Host` 是**进程级**：整个进程只有一份值，且**不与其它层级组合**——`Host | User` 这类组合在定义阶段就被拒绝（`ArgumentException`），因为它没有一致的读取解释。给的是日志级别这类一个进程只有一个实例的东西：按租户各存一份无从生效，写进去只会让界面显示一个不起作用的值。它与宿主的租户级共用同一行（`ScopeKey` 为 `h:t`，宿主视角本就走租户层），读写都只允许发生在宿主上下文：租户上下文下查询过滤器会滤掉宿主行（专属库形态连的还是租户自己的库），因此存储实现就地抛异常，而不是静默读到空或写成租户行。
- 进程级设置**不接在回落链上**：`ISettingProvider` 在宿主上下文直接读宿主那一行，没有值才用代码默认值；租户上下文下它读不到——`GetOrNullAsync` 抛 `HostScopeUnavailableException`，`GetAllAsync` 干脆不包含它。刻意不返回代码默认值：那个值看着有效，调用方分不出「这就是当前生效的级别」和「这一层在当前上下文根本读不到」。存储侧由 `ISettingStore.CanAccessHostScope` 回答可达性，解析端据此决定要不要去读，而不是靠捕获异常判断上下文。
- **同一作用域先写后读读到新值**：`ISettingProvider` 按作用域记忆化，经 `ISettingManager` 写入后，同一作用域的记忆化结果即作废，下次读取重新查存储。
- **写入校验按固定顺序**：空串（`Setting:EmptyValueRejected`，清除只用 `null`）→ 值类型与区间（`BooleanRequired`、`IntegerRequired`、`ValueOutOfRange`）→ 候选值（`ValueNotAllowed`，按序号比较）→ 宿主注册的 `ISettingValueValidator`。任何一步不过都不落库、不发事件。清除只校验名称与层级，不会被一个已经不合法的历史值卡住。
- **写入后发布 `SettingChangedEvent`**（注册了本地事件总线时）。事件不带值——机密设置的明文不进事件。
- **设置页用例读原始覆盖值**：各层分别给出，不给回落后的生效值，否则租户页会显示当前用户的个人偏好、一保存就写成租户默认值。只处理 `IsVisibleToClients` 的设置（不可见的读不到、写入 404）；租户上下文不下发进程级设置，写入它返回 403（`Setting:HostOnly`）；进程级设置的值放在 `TenantValue`；机密设置不下发任何值，只给 `HasSecretValue`。显示名按 `Setting:{设置名}`、分组按 `SettingGroup:{分组}` 查 `LocalizationResource`，查不到回落到定义文案；未分组归入 `DefaultGroup`。用例不查权限，改租户值的授权由端点策略决定。
- **宿主级设置经配置源进入 Options（Hosting 包）**：设过的宿主级设置作为优先级最高的配置源覆盖绑定的配置键，没设的不出现、自然回落到部署配置；机密设置在进程内解密后进配置，库里仍是密文。三处推进：宿主开始接收请求之前一次（失败只记告警，库还没迁移时不拦启动）、写入宿主级设置的事务提交后本进程立即一次、每个副本上的 `EveryInstance` 周期任务 `settings.host-refresh`。
- **整组原子生效**：推进后按 `BindOption<TOptions>` 涉及的选项类型逐个新建并校验，任何一个不合规就整组退回上一组（只记错误），成对的项（发信账号与口令）设齐之前停在上一组。与当前一组或上次被拒的一组相同时什么都不做。
- **校验不过不抛异常，也不阻断写入**：值已经落库，推进只是把它送进 Options。三处推进的失败表现依次是——启动时记告警并沿用部署配置（不拦住应用起来）、写入提交后的那次记告警且**下一轮周期刷新会再试**（短暂失败会自愈）、周期刷新记错误。运维要观察这件事，看日志里那条 `keeping the previous values`，而不是保存设置的接口返回值——那一步已经成功了。
- **被绑定设置的代码默认值是部署基线**：在 `PostDefine` 阶段逐个配置提供程序查绑定的键（跳过宿主设置配置源本身，后加入的源优先），按定义的值元数据归一（候选值取定义里的写法、布尔小写），认不出时用兜底值；机密设置没有默认值。被绑定的设置必须已定义且是进程级，否则首次访问定义时抛出。
- `Group` 只承载**分组标识**，不承载分组文案，理由与 `DisplayName` 相同：定义一次性加载并缓存，拿不到请求 culture。分组是信息架构而非控件元数据——设置多起来之后界面要按关注点分类摆放，而"哪些设置属于同一件事"只有定义方知道；放到客户端另抄一份，新增设置忘了登记就会落在界面之外，既不报错也查不出来。未分组返回 `null`，由宿主决定归处。
- **机密设置**（`IsEncrypted`）：`ISettingManager` 写入前加密，`ISettingProvider` 读出落库值时解密，用的是宿主的 Data Protection（`IDataProtectionProvider`，与租户连接串同一做法）；代码默认值不加密、原样返回。用途字符串固定并带设置名，一个设置的密文挪到别的设置名下解不开。宿主须 `AddDataProtection()` 并配置持久化、可共享的密钥环——读写同一设置表的所有进程共用它，密钥丢失等于机密设置全部丢失，要纳入备份；换密钥设施就在 Data Protection 这一层换（密钥存储与密钥加密都可替换），本组件不另设加密接口。没注册 Data Protection 时读写机密设置抛 `InvalidOperationException`（宁可失败，也不悄悄存成明文），不定义机密设置的宿主不受影响；密文解不开（密钥环未共享、密钥丢失）时读取抛 `InternalServerException`，不回落到默认值。只在读取某个机密设置的值时才解密，批量读取不受密钥环问题牵连。按「仅客户端可见」批量读取时机密设置一律不在其中，即使标了 `IsVisibleToClients`：那个标记只表示界面上有这一项可写。
- 匿名调用只回落到租户级，不查用户级。
- `ISettingProvider` 为 Scoped，一次请求内每个层级只查一次库并复用结果；`ISettingDefinitionManager` 为 Singleton。
- 设置名重复在首次访问定义时失败。读写未定义名称抛 `UndefinedSettingException`；写入未允许层级抛 `SettingScopeNotAllowedException`。
- 层级由 `SettingRecord` 的两个字段共同表达：`TenantId` 交给多租户查询过滤器隔离，`UserId` 为 `null` 即租户级。不设可独立修改的 Scope 列，避免出现自相矛盾的第二事实源。
- 唯一索引为 `(ScopeKey, Name)`；`ScopeKey` 由租户和层级派生，避免可空 `TenantId`/`UserId` 使唯一约束失效。
- 表名沿用宿主 `DbSet` 属性名，组件不写死。

## 按用户时区展示时间

时间一律以 UTC 存储，只在展示时换算——这是 .NET 官方对 `TimeProvider` 的建议，也是本框架的既定口径。「换算成谁的时区」由设置提供，因此这里定下设置层面的约定；具体怎么渲染、服务端要不要参与，属于宿主的事。

| 项 | 约定 |
| --- | --- |
| 设置名 | `Display.TimeZone` |
| 值 | **只接受 IANA 时区名**（如 `Asia/Shanghai`）。`UTC` 本身也是合法 IANA 标识 |
| 清除覆盖 | 写入 `null` 只清掉当前层，读取继续向下一层回落（用户 → 租户 → 代码默认值） |
| 最终缺值 | 三层都没有值时由消费端明确选择回退值 |
| 层级 | `SettingScopes.All`：租户给默认值，用户可各自覆盖 |

写入端必须用 `TimeZoneInfo.HasIanaId` 将值域限制为 IANA；只调用 `TryFindSystemTimeZoneById` 会同时接受浏览器不支持的 Windows 时区 ID。

设置组件本身不做时区转换：转换是 `TimeZoneInfo` 的事，「用谁的时区、日边界怎么算」是宿主的决定。项目模板给出了服务端与前端两侧的完整做法。

## 注意事项

- **设置值一律是字符串**。`GetAsync<T>` 用不变文化转换，避免同一份值在不同区域设置的节点上解析出不同结果；复杂结构自行序列化。
- **跨请求的变更下一请求可见**，请求内不可见——这是「请求内一致」的代价，也因此不需要分布式缓存、没有跨节点失效问题。
- **`ISettingStore` 只能有一个实现**。为第二个 DbContext 注册时在注册期直接拒绝：静默取最后一条会让设置写进宿主没预期的库。
- **机密设置只给"管理员要在运行期维护的第三方凭据"**，例如发信服务的口令。连接串、签名密钥这类部署期密钥仍属配置提供程序与密钥管理服务，不要落进设置表。宿主给机密设置的界面应当是只写的：不回显值，只显示「已设置」。
- **写入不经 `ISettingStore` 直接操作实体会绕过定义校验**，写出的孤儿行永远读不到。
- **只声明运行期可改的业务偏好**。连接串、密钥、认证协议属于部署期配置，留在配置提供程序与 `IOptions<T>`；搬进设置表等于给运行期一个能拆掉安全与正确性保证的开关。
- **已经是实体字段的东西不要再定义成设置**。两处都能写就有两个互相矛盾的事实源，读的人不知道该信哪个。
- **值域声明在定义上，业务规则写成校验器**。客户端的候选项约束不了脚本和历史数据；框架认识的值域（类型、区间、候选）写进定义，其余（时区、邮箱地址、开启前提）实现 `ISettingValueValidator`，不要只在界面上限制。
- **宿主级设置刷新需要调度器**：`AddHostSettings` 只登记周期任务，宿主不注册 `AddInProcessBackgroundJobs()` 之类的调度器时，其它实例上的修改只会在重启后生效。刷新周期留在部署配置而不是做成设置项：它自己做成设置就成了自举依赖。
- **要在租户请求里读进程级配置**，注入对应的 `IOptionsMonitor<T>`（经 Hosting 包绑定），不要每个请求去问设置存储——租户上下文下宿主行本来就读不到。

## 相关

- [多租户](./multi-tenancy.md)
- [后台作业](./background-jobs.md)
- [当前用户与身份信息](./security.md)
