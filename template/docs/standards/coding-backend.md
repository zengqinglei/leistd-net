# 后端开发规范

本项目后端编码规范，同时遵循 [项目通用约定](./coding-common.md)。接口契约见 [API 规范](./api.md)，认证与权限侧别见 [认证与授权](./auth.md)。`Leistd.*` 的 API 以实际还原版本的包内文档与 XML 为准，定位方法见 [后端说明](../../backend/README.md#leistd-框架-api)。

## 1. 技术栈

.NET 10、EF Core 10、PostgreSQL、Redis（多实例部署）、Mapster（`Leistd.ObjectMapping.Mapster` + `IObjectMapper`）、Serilog。精确版本以项目文件为准。

## 2. 分层与目录

依赖方向：Domain ← Application ← Api；Infrastructure → Domain；Api 是组合根。Application 不引用 Infrastructure 与 `Microsoft.EntityFrameworkCore`。

| 层 | 职责 | 不做 |
| --- | --- | --- |
| Api | 路由、鉴权、调用应用服务、宿主组装 | 业务校验与编排、直接操作实体或仓储；HTTP 信息不传入 Application |
| Application | 编排用例：收发 DTO、调用领域对象与领域服务、查询聚合、跨聚合校验、事务边界、发布事件 | 核心业务规则 |
| Domain | 实体行为、领域服务、业务规则；定义仓储与第三方服务接口 | 引用 EF Core、DTO 转换、查询聚合 |
| Infrastructure | 持久化、实体配置、外部适配器及其 Options | 业务规则 |

目录按功能模块组织，类型目录是模块下的一级目录（如 `Settings/AppServices`、`Settings/Dtos`），不嵌进子功能目录；子功能目录（如 `Auth/Sessions`）只放不属于这些类型的协作类型。模块内少量协作类型（如 `Tenants/TenantSeeder`）可以直接放在模块根，但已有分类的类型（DTO、应用服务、事件处理器等）按分类归位。

- **Application**：`AppServices`（接口与实现）、`Dtos`、`Mappings`、`Errors`、`Events`（应用层发布、不来自实体的事件）、`EventHandlers`、`BackgroundJobs`，按需 `Constants`、`Abstractions`（由宿主实现的端口）、`Provider`（框架扩展点实现）、`Policies`。跨模块共用、又不属于任何模块的应用层约定（认证方案名、分页约定）放 `Shared/`，它同样不是兜底目录。
- **Domain**：`Entities`（实体与聚合）、`ValueObjects`（不可变值类型，含有限状态枚举）、`DomainServices`、`Events`（实体发出的事件）、`Policies`、`Errors`、`Options`（只放内层——Domain 与 Application——自身消费的配置）、`Abstractions`（端口及其输入输出模型）。不认识任何实体的领域共享能力按语义放 `Shared/`（如 `Shared/Text`），它不是兜底目录；子目录名不与常用 BCL 类型同名。
- **Infrastructure**：外部适配器自己绑定和校验客户端标识、密钥、回调地址；Application 只依赖内层端口暴露的能力。

领域服务之间只允许单向依赖，且仅用于复用另一个领域服务的**变更行为**，在类上注释原因；读取不跨领域服务调用。依赖环由 `ValidateOnBuild` 检出（见 §4）。

## 3. 编码

### 3.1 语言特性

| 特性 | 场景 |
| --- | --- |
| 主构造函数 | 服务类 |
| `record` + `init` + `required` | DTO |
| 文件范围 namespace | 所有文件 |

异步：I/O 一律 `async/await`，方法名以 `Async` 结尾（协议规定的名字除外），不用 `.Result`/`.Wait()`。

### 3.2 时间

- 业务代码注入 `Leistd.Timing.IClock`（`clock.Now`，恒为 UTC）。不就地读取 `DateTime.Now`/`UtcNow`、`DateTimeOffset.Now`/`UtcNow`、`TimeProvider.System.GetUtcNow()`：它们隐藏依赖、不可测。
- ASP.NET Core 认证、Cookie、票据等框架集成回调要求 `TimeProvider` 时可注入 `TimeProvider`。
- 实体不注入服务：时间相关方法接收 `DateTime now`，由调用方传入。
- 契约里的时间字段写明单位（秒或毫秒）。

### 3.3 实体

属性 `private set`；保留 EF Core 用的 `private` 无参构造；状态只经公共方法修改；构造函数做必要校验；Id 用 `Guid.CreateVersion7()`；创建审计字段由框架在跟踪时填充。

```csharp
public class User : FullAuditedEntity<Guid>
{
    public string Username { get; private set; }
    public string Email { get; private set; }
    public string PasswordHash { get; private set; }
    public bool IsActive { get; private set; } = true;

    private User() { Username = null!; Email = null!; PasswordHash = null!; }

    public User(string username, string email, string passwordHash)
    {
        Id = Guid.CreateVersion7();
        Username = username;
        Email = email;
        PasswordHash = passwordHash;
    }

    public void Disable() => IsActive = false;
}
```

### 3.4 领域服务

命名 `*DomainService`，不定义接口。负责单聚合规则与实体增删改的核心逻辑；不做 DTO 转换、事务管理、查询聚合。

规则判定在实体或领域服务；应用服务据其结果（如 `user.CanBeManagedBy(...)`）按用例选码抛出，自行组合实体字段做判定属于违规。

缓存、通知等副作用经本地事件在提交后由应用层 `IEventHandler<TEvent>` 执行：纯实体变更的由实体 `AddLocalEvent(...)` 发出；随用例而异的（如本人改密与管理员重置的提醒）由应用服务发布。

```csharp
public class UserDomainService(IRepository<User, Guid> userRepository, IPasswordHasher passwordHasher)
{
    public async Task<User> CreateUserAsync(
        string username, string email, string password, CancellationToken cancellationToken = default)
    {
        if (await userRepository.AnyAsync(u => u.Username == username, cancellationToken))
            throw new BusinessException(UserErrorCodes.UsernameTaken, $"Username '{username}' already exists.")
                .WithData("Username", username);

        var user = new User(username, email, passwordHasher.HashPassword(password));
        return await userRepository.InsertAsync(user, cancellationToken);
    }
}
```

### 3.5 应用服务

接口 `I*AppService : IAppService`，实现 `*AppService : BaseAppService, I*AppService`。查询用仓储的 `GetQueryableAsync` 组合条件，经 `IQueryableAsyncExecuter` 执行；DTO 投影经 `IObjectMapper`。

```csharp
public class UserAppService(
    IRepository<User, Guid> userRepository,
    IObjectMapper objectMapper,
    IQueryableAsyncExecuter asyncExecuter) : BaseAppService, IUserAppService
{
    public async Task<PagedResult<UserOutputDto>> GetPagedListAsync(
        GetUserPagedInputDto input, CancellationToken cancellationToken = default)
    {
        var query = await userRepository.GetQueryableAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(input.Keyword))
            query = query.Where(u => u.Username.Contains(input.Keyword));

        var totalCount = await asyncExecuter.CountAsync(query, cancellationToken);
        var users = await asyncExecuter.ToListAsync(
            query.OrderBy(u => u.Username).ThenBy(u => u.Id).Skip(input.Offset).Take(input.Limit),
            cancellationToken);
        return new PagedResult<UserOutputDto>(totalCount, objectMapper.Map<List<User>, List<UserOutputDto>>(users));
    }
}
```

排序字段取自各接口的白名单，末尾追加唯一键（如 `Id`）保证分页稳定。

### 3.6 事务与工作单元

- 工作单元按需引入，不是写方法的必需装饰。单次 `SaveChanges` 已在数据库隐式事务里；只有跨多次提交边界的方法才标 `[UnitOfWork]`。判据见 `unit-of-work` 组件随包文档的"何时不需要"。
- 工作单元内仓储写入延迟到冲刷或提交：唯一约束等数据库异常在那时才抛出，不在 `InsertAsync` 调用处。
- 写方法用仓储返回值构造输出，不回查数据库：`Id`、创建审计与租户值在实体进入跟踪时已落定，而工作单元内回查得不到尚未落库的行。需要回显关联数据时让写方法回传它写了什么。

### 3.7 Controller 与组件端点

框架组件已提供端点的能力（设置、权限管理、操作记录、通知、租户与租户连接）不写 Controller：在 `Api/Hosting/ComponentEndpoints.cs` 用组件的 `Map*` 给前缀与授权策略。组件不认识的业务动作才写 Controller，路由不与组件端点重叠。

- Controller 命名 `*Controller`，继承 `BaseController`（视图渲染、透传代理、机器端点等例外就近注释）；只做路由、鉴权与调用应用服务。
- 有响应体返回 `Task<TOutputDto>`（无 I/O 可同步返回 DTO），无响应体返回 `Task`（HTTP 200 空响应）；返回协议结果（`Challenge`、`SignIn`、`Redirect` 等，含 `/connect/*`）或文件时用 `IActionResult`。
- 方法名与路由以 [API 规范 §6](./api.md#6-http-方法与路由规范) 为准。

操作留痕：组件端点挂 `[OperationRecordAction]` 后，授权被拒与之后的 `BusinessException` 由 `ApiAuthorizationResultHandler`、`OperationFailureRecordingMiddleware` 兜底补记（参数校验失败不记）。应用服务在拒绝处调 `RecordFailedAsync` 时兜底按动作与目标去重跳过，因此注解里的目标（含 `TargetIdPrefix`）须与应用服务记录的逐字一致；`RecordFailedAsync` 自身不判重。

### 3.8 枚举持久化

枚举以字符串持久化：`builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32)`。数据迁移中按字符串比较（`Status = 'Active'`），对 varchar 列写整数比较会在 PostgreSQL 报 42883。

### 3.9 权限

授权策略名、权限名等跨处引用的标识符用常量（`PermissionConstant`）。权限结构与界面一一对应，两端各自遵守、不互读源码：

| 权限定义 | 对应界面 | 显示名 |
| --- | --- | --- |
| 分组（`PermissionConstant.Groups`） | 菜单分组 | 与分组标题一致 |
| 根权限 `App.{模块}` | 菜单项与页面 | 与菜单项标题一致 |
| 子权限 `App.{模块}.{动作}` | 页面操作按钮 | 常规动作统一为 Create / Edit / Delete，其余与按钮文案一致 |

- `displayName` 写英文默认文案；译文键为 `Permission:{权限名}`、`PermissionGroup:{分组名}`，英文资源保留同名键且值等于默认文案（`PermissionCatalogContractTests` 核对）。
- 只作为另一个权限前提的权限不单独定义。侧别选择见 [认证与授权](./auth.md#权限侧别与租户维度)。

### 3.10 设置与 Options

| 取值层级 | 做法 |
| --- | --- |
| 租户级、用户级 | 经应用层策略提供方读 `ISettingProvider`；替别人判断用 `GetOrNullForUserAsync`。不做成 Options |
| 宿主级、运行期可改 | 在 `Api/Configuration/HostSettingBindings` 加一行绑定，消费方注入 `IOptionsMonitor<T>`/`IOptionsSnapshot<T>`；自带配置重载的库直接绑定它读的键 |
| 部署期定死（凭据、连接、协议参数） | `IOptions<T>`；不在服务里读 `IConfiguration["键"]` |

- 宿主级设置以最高优先级配置源覆盖部署配置，写入后本进程即时生效，其他实例由周期任务跟上；组合起来让 Options 校验不过的一组整组不生效并记错误日志。
- 绑定了配置键的宿主级设置不写代码默认值，清除即回到部署配置。
- 值域声明在设置定义上（`AsBoolean()`、`AsInteger(min, max)`、`WithAllowedValues(...)`）；定义表达不了的规则写成 `Application/Settings/Validators` 下的 `ISettingValueValidator`。

## 4. 依赖注入

**注册归属**：每层的 `DependencyInjection.cs`（`AddDomainServices`、`AddApplicationServices`、`AddInfrastructureServices`/`AddPersistenceServices`、`AddApiAuthorization` 等）注册本层类型，可调用实现本层能力所需的组件注册入口。Api 自有类型按关注点注册在 `Api/Auth`、`Api/Hosting` 的扩展方法里（如 `AddMyProjectAuthentication`、`AddMyProjectWebHost`）；`Program.cs` 只组合各层、组件与这些入口并配置管道。部署基线 Options 在声明它的层或组合根绑定，宿主定向配置留在组合根；需要校验的用 `AddOptions<T>()...ValidateOnStart()`。

**生命周期**：先看状态所有权、并发安全、依赖链与实际消费作用域。

| 情形 | 生命周期 |
| --- | --- |
| 持有请求或工作单元内状态、依赖 DbContext | Scoped |
| 跨请求共享且线程安全（定义提供方、连接复用） | 可 Singleton |
| 其余（AppService、领域服务、事件处理器等无状态服务） | Transient |

Singleton 不得直接或间接捕获 Scoped；依赖作用域服务的 Transient 必须在正确作用域解析。Development 环境开启 `ValidateScopes` 与 `ValidateOnBuild`。

**注册方式**：可替换的单实现用 `TryAdd*`；多实现用 `TryAddEnumerable`；按业务键登记（周期任务名等）与命名 Options 按各入口契约；有意覆盖组件默认实现用 `Replace` 并注释原因（`Replace` 与组件入口的调用先后无关）。相同登记重复调用不得重复生效。注册测试范围见[测试规范](./testing.md)。

没有约定式自动注册：`IAppService` 只是标记，服务需显式注册；`[UnitOfWork]` 依靠代理织入，注册时使用实现类型。

## 5. 命名与 DTO

| DTO | 命名 |
| --- | --- |
| 分页查询输入 | `Get{Entity}PagedInputDto`（继承 `PageRequest`，参数见 [API 规范 §5](./api.md#5-分页规范)） |
| 创建、更新输入 | `Create{Entity}InputDto`、`Update{Entity}InputDto` |
| 输出 | `{Entity}OutputDto` |
| Client SDK 中不成对的响应 | `{Concept}Dto` |

- DTO 全部为 record；一个文件一个对外 DTO，仅被它内嵌使用的 item 类型可同文件；业务入参 DTO 放应用层模块，不放 Api。
- 入参 DTO 写成属性式（`{ get; init; }`），不用位置记录：校验错误的 `errors[].field` 按 JSON 命名策略与请求体字段同名。
- 校验只在入口 DTO 用 DataAnnotations 完成，内层信任 DTO（多入口共享的实体守卫除外）。参与字段校验消息的属性（带校验特性、消息里用到 `{0}`）写 `[Display(Name = "...")]`，每个校验特性显式写 `ErrorMessage`；两者写英文原文并作为本地化键，占位符形如 `{0} is required.`。前端按同一规则即时校验。
- 变量：DTO 参数 `input`，返回对象 `result`，`IQueryable` 为 `query`/`xxxQuery`；仓储注入 `{entity}Repository`，领域服务注入 `{entity}DomainService`。

## 6. 数据访问

- 仓储方法以 `IRepository` 为准（`GetByIdAsync`、`GetQueryableAsync`、`Insert/Update/DeleteAsync` 及 `*ManyAsync` 等）；实现 `ISoftDelete` 的删除为逻辑删除。
- Application 不使用 EF Core 扩展：`IQueryable` 经 `IQueryableAsyncExecuter`（`ToListAsync`、`CountAsync`、`FirstOrDefaultAsync`、`AnyAsync` 等）执行；关联数据用查询组合（子查询、`Join`）或分别查询，不用 `Include`。
- 业务库上下文经仓储或 `IDbContextProvider<TDbContext>` 获取，不直接构造注入：直接注入的实例按宿主库创建，分库租户下会落到宿主库（框架拒绝，表现为 500）。控制库上下文固定宿主连接，可以直接注入。
- 对象映射：实体、存储模型或框架模型到 DTO 的投影走模块 `Mappings/` 下实现 `IRegister` 的类，业务服务只注入 `IObjectMapper`；能按名称约定映射的不写配置；不调用无参 `Adapt<T>()`（它用全局配置，本项目的规则静默失效）；调用方才知道的值经 MapContext 传入；由多个来源拼装、带计算或本地化的 DTO 直接构造；不在 DTO 上写 `FromXxx` 静态方法。
- 请求外的异步工作交给 `IBackgroundTaskQueue`，跨实例互斥用 `IDistributedLock`，定期维护登记为周期任务（`AddRecurringJob`，显式选 `Cluster` 或 `EveryInstance`）；不另起线程或自造跨实例锁（只护进程内状态的 `lock` 除外）。
- 可还原的加密用 `IDataProtectionProvider`：构造时 `CreateProtector` 一次并复用，用途字符串带版本，解密只捕获 `CryptographicException`，可并列同一载荷的 Base64/JSON 解析异常。

## 7. 异常与日志

按失败语义选异常；HTTP 映射、错误码规则与示例见 [API 规范 §4](./api.md#4-异常与-http-映射)。

| 位置 | 异常 |
| --- | --- |
| Domain / Application 业务规则 | `BusinessException(code, safeMessage)` |
| Infrastructure 传输、配置、解析失败 | BCL 或专用技术异常（对外兜底 500，细节进日志） |
| 启动期与组合期（`Program.cs`、`Add*Services`、`IValidateOptions`）、DbMigrator 等一次性作业 | BCL 异常（`InvalidOperationException` 等） |
| 参数与编程契约 | BCL 异常 |

- 部署配置错误在启动期失败：`AddOptions<T>().Validate(...).ValidateOnStart()`。连接串在宿主启动前就要用，缺失时由创建 DbContext 直接抛出并指明键名。
- 组合期只为**选择注册哪种实现**（是否接 Redis、加载哪些证书）读配置，在读取处校验并报出键名；集成测试用 `UseSetting` 覆盖这类键。其余取值经 Options 派生，合法性用 `ValidateOnStart()` 判定。
- 日志用结构化消息模板，消息为英文：`logger.LogWarning("Login failed too many times for user {UserId}", userId)`。不记录密码、令牌、联系方式等敏感信息。

## 8. Api 目录

Api 文件按关注点归入少数顶层目录，命名空间跟随目录：

| 目录 | 内容 |
| --- | --- |
| `Controllers/` | 业务 Controller 与 `BaseController` |
| `Auth/` | 授权策略与处理器、认证方案组装（`*Extensions`）、会话签发 |
| `Hosting/` | 宿主组装扩展（`*Extensions`）、组件端点映射、`ExceptionMappings/` |
| `Localization/` | 本地化资源标记类型（`ApiResource`） |
| `Notifications/` | 只放依赖宿主资源的通知扩展点实现 |
| `Configuration/` | 宿主级设置绑定 |
| `Options/` | 强类型 Options |
| `Middlewares/` | 中间件 |
| `HealthChecks/` | `*HealthCheck` |
| `HostedServices/Initializer/` | 一次性启动引导 `*Initializer` |
| `Filters/` | MVC/Hub 管道过滤器（按需创建）；名字以 `Filter` 结尾的业务策略按所属功能域放 |

- 周期任务（`IRecurringJob`，`*Job`）放 Application 所属模块的 `BackgroundJobs/`；常驻消费者 `*Worker` 放所属模块的 `Workers/`。不建跨模块的顶层 `Jobs/`。
- 请求体上限沿用 Kestrel 默认，大上传端点用 `[RequestSizeLimit]`/`[RequestFormLimits]` 单独放宽；不用笼统的 `Extensions` 命名空间。
