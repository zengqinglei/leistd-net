# 设置

按 用户 → 租户 → 代码默认值 的顺序解析设置。业务用 `ISettingDefinitionProvider` 声明有哪些设置，框架负责定义注册、层级回落与持久化。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 运行期可改、按租户或按用户各不相同的值（界面语言、时区、显示名） | 定义设置，注入 `ISettingProvider` 读取 |
| 部署期决定的值（连接串、密钥、协议契约、事务语义） | **不要用本组件**，用 `IOptions<T>` 与配置文件 |
| 只声明设置、只读取（应用层、领域服务） | 只引用 `Leistd.Settings.Core` |
| 需要持久化设置值 | 引用 `Leistd.Settings.EntityFrameworkCore` |

凭据、信任边界、解析协议和事务隔离级别属于部署配置，不应进入运行期设置。

## 安装

```bash
# 定义、解析与写入契约
dotnet add package Leistd.Settings.Core

# EF Core 持久化
dotnet add package Leistd.Settings.EntityFrameworkCore
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
            group: "Logging").IsVisibleToClients = true;
    }
}
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

## 接口参考

| 成员 | 说明 |
| --- | --- |
| `ISettingDefinitionProvider.Define(context)` | 业务声明有哪些设置 |
| `ISettingDefinitionContext.Add(name, defaultValue?, scopes?, displayName?)` | 添加定义；名称全局唯一 |
| `ISettingDefinition` | `Name`、`DisplayName`、`DefaultValue`、`Scopes`、`Group`、`IsVisibleToClients`；`DisplayName` 与 `Group` 都原样返回不翻译，宿主要本地化就把它们当回落文案与词条键 |
| `SettingScopes` | `Tenant`、`User`、`All`、`Host`、`None`；代码默认值不在其中 |
| `ISettingDefinitionManager` | 汇总全部定义并提供查询 |
| `ISettingProvider.GetOrNullAsync(name, ct)` | 读取当前生效值 |
| `ISettingProvider.GetAsync<T>(name, ct)` | 读取并转换类型 |
| `ISettingProvider.GetAllAsync(visibleToClientsOnly?, ct)` | 读取全部生效值 |
| `ISettingManager.SetAsync(name, value, scope, userId?, ct)` | 写入；`value` 为 `null` 时清除该层级 |
| `ISettingStore` | 持久化契约；只按层级读写原始字符串，不做校验与回落 |
| `AddSettingsCore(services)` | 注册定义管理器、解析器与写入入口 |
| `AddSettingsEfCore<TDbContext>(services)` | 注册 EF Core 存储；内部调用 `AddSettingsCore()` |
| `ConfigureSettings(modelBuilder)` | 映射 `SettingRecord` 实体 |

## 实现行为

- 回落顺序为 **用户级 → 租户级 → 代码默认值**。宿主视角走租户级那一层（`TenantId` 为 `null` 的行），不额外引入「全局」层。
- 定义未允许的层级即使库里有值也不参与回落——改一次 `Scopes` 不会让历史遗留行悄悄重新生效。
- `SettingScopes.Host` 是**进程级**：整个进程只有一份值，且**不与其它层级组合**——`Host | User` 这类组合在定义阶段就被拒绝（`ArgumentException`），因为它没有一致的读取解释。给的是日志级别这类一个进程只有一个实例的东西：按租户各存一份无从生效，写进去只会让界面显示一个不起作用的值。它与宿主的租户级共用同一行（`ScopeKey` 为 `h:t`，宿主视角本就走租户层），读写都只允许发生在宿主上下文：租户上下文下查询过滤器会滤掉宿主行（专属库形态连的还是租户自己的库），因此存储实现就地抛异常，而不是静默读到空或写成租户行。
- 进程级设置**不接在回落链上**：`ISettingProvider` 在宿主上下文直接读宿主那一行，没有值才用代码默认值；租户上下文下它读不到——`GetOrNullAsync` 抛 `HostScopeUnavailableException`，`GetAllAsync` 干脆不包含它。刻意不返回代码默认值：那个值看着有效，调用方分不出「这就是当前生效的级别」和「这一层在当前上下文根本读不到」。存储侧由 `ISettingStore.CanAccessHostScope` 回答可达性，解析端据此决定要不要去读，而不是靠捕获异常判断上下文。
- **宿主作用域 ≠ 进程内状态同步。** 框架管到「宿主设置的定义、层级与持久化」为止：改完设置要让本进程真的换行为（例如把日志级别推到 logger 上）、以及让**其它实例**也跟上，是宿主项目的事——模板的做法是一个应用器（写入后就地调用）加一个周期刷新的后台任务。需要在租户请求里读进程级配置的消费者，应当读那份进程内运行期状态，而不是每个请求去问设置存储。
- `Group` 只承载**分组标识**，不承载分组文案，理由与 `DisplayName` 相同：定义一次性加载并缓存，拿不到请求 culture。分组是信息架构而非控件元数据——设置多起来之后界面要按关注点分类摆放，而"哪些设置属于同一件事"只有定义方知道；放到客户端另抄一份，新增设置忘了登记就会落在界面之外，既不报错也查不出来。未分组返回 `null`，由宿主决定归处。
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
- **不做加密设置**。密钥类值属于部署期配置，用配置提供程序与密钥管理服务，不要落进设置表。
- **写入不经 `ISettingStore` 直接操作实体会绕过定义校验**，写出的孤儿行永远读不到。
- **只声明运行期可改的业务偏好**。连接串、密钥、认证协议属于部署期配置，留在配置提供程序与 `IOptions<T>`；搬进设置表等于给运行期一个能拆掉安全与正确性保证的开关。
- **已经是实体字段的东西不要再定义成设置**。两处都能写就有两个互相矛盾的事实源，读的人不知道该信哪个。
- **设置值的值域由宿主校验**。框架只校验名称与层级，不认识业务值域；客户端的候选项约束不了脚本和历史数据，校验要放在写入端（见[按用户时区展示时间](#按用户时区展示时间)的写入校验约定）。

## 相关

- [多租户](./multi-tenancy.md)
- [当前用户与身份信息](./security.md)
