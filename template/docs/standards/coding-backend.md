# 后端开发规范

本文档为模板项目默认的 .NET 10、EF Core、DDD 后端开发规范。若新项目未采用该技术栈，不应把本文规则作为通用默认事实。

同时遵循 [项目通用开发规范](./coding-common.md)。

## 1. 核心技术栈

- **框架**: .NET 10+
- **ORM**: EF Core 10+
- **数据库**: PostgreSQL 15+
- **缓存**: Redis 7+
- **对象映射**: Mapster（`Leistd.ObjectMapping.Mapster` + `IObjectMapper`）
- **依赖注入**: Microsoft.Extensions.DependencyInjection
- **日志**: Serilog

## 2. 分层架构规范

项目采用 DDD（领域驱动设计）分层架构：

```
┌─────────────────────────────────────────┐
│         API Layer ({ProjectName}.Api)   │  ← HTTP 相关处理
├─────────────────────────────────────────┤
│   Application Layer (Application)       │  ← 业务流程编排
├─────────────────────────────────────────┤
│      Domain Layer (Domain)              │  ← 核心业务逻辑
└─────────────────────────────────────────┘
         ↑
         │ 依赖
         │
┌─────────────────────────────────────────┐
│  Infrastructure Layer (Infrastructure)  │  ← 技术实现
└─────────────────────────────────────────┘
```

### 2.1 各层职责

#### API Layer（{ProjectName}.Api）
- **职责**: HTTP 相关处理（HttpContext、请求响应、路由）
- **原则**: HTTP 相关信息不应传递到 Application 层，保障 Application 层可用于多种架构（BS/CS）
- **包含**: Controllers、Middleware、Authentication

#### Application Layer（{ProjectName}.Application）
- **职责**: 协调业务逻辑（调用领域对象行为、领域服务、发布/订阅事件）
- **包含**: AppServices、Dtos、Mappings、Events、EventHandlers，以及按需的 Constants（名字常量）、Abstractions（由宿主实现的端口）、
  Provider（框架扩展点的实现，如定义提供程序、权限主体提供程序，与它们用到的名字常量）、Policies（策略及其提供程序）
- **目录归类**: 按类型分的目录都是功能模块下的一级目录（如 `Settings/AppServices`、`Settings/Dtos`），不嵌进子功能目录；子功能目录（如 `Auth/Sessions`、`Settings/Hosting`）只放不属于这些类型的协作类型。类名以 `Event` 结尾的放模块的 `Events/`（这里只放由应用层发布、不来自实体的事件），以 `EventHandler` 结尾的放 `EventHandlers/`（如 `Settings/Events`、`Settings/EventHandlers`、`Auth/EventHandlers`）。
- **可以**: 使用 EF Core 的 `Include`、`GetQueryIncludingAsync` 进行数据查询和聚合

#### Domain Layer（{ProjectName}.Domain）
- **职责**: 核心业务逻辑（领域对象行为、领域服务、业务规则）
- **包含**: Entities、ValueObjects、DomainServices、Events、Specifications
- **目录归类**: 实体发出的事件放模块的 `Events/`（如 `Auth/Events`），处理器在应用层；以 `Policy` 结尾的规则类型放模块的 `Policies/`（如 `Users/Policies`）；
  认得实体、按实体做判定的无状态服务是领域服务，归 `DomainServices/` 并以 `DomainService` 结尾；不认识任何实体的领域共享能力按语义放 `Shared/`（如 `Shared/Text`、`Shared/Security/OneTimeCodes`），
  `Shared` 是领域层共享内核而非无法归类代码的兜底目录；子目录名不得与常用 BCL 类型同名（例如 `Shared/Encoding` 会遮住 `System.Text.Encoding`）。`Entities` 只放实体和聚合；`ValueObjects` 放领域内部的不可变值类型（包括有限状态枚举）；只有在多个领域模块共用且没有明确所有者的枚举才放 `Shared/Enums`。`Options` 只放内层本身消费的配置绑定类型；第三方适配器的客户端标识、密钥和回调地址归 Infrastructure。端口的输入/输出模型按所属接口放在 `Abstractions`，不因为使用 `record` 就归为值对象。
- **接口定义**: 第三方服务接口、持久化接口（IRepository）
- **严格禁止**:
  - 引用 `Microsoft.EntityFrameworkCore`
  - 使用 EF Core 特性（Include、ThenInclude）
  - 领域服务之间形成依赖**环**
- **领域服务之间的单向依赖**：仅用于复用另一个领域服务的**变更行为**（例如"首次外部登录分配默认角色"这类领域策略——把它挪到应用层编排会让规则漂出领域），并在类上就近注释说明为什么这段不属于应用层。**读取不要跨领域服务调用**，由调用方自己取。
  - 环由 DI 保障，不设静态闸门：构造注入的环会被 Microsoft DI 检出并响亮失败（`ValidateOnBuild` 时在容器构建期，否则首次解析时抛 `A circular dependency was detected`）。集成测试的宿主以 `Development` 环境启动，而 `Program.cs` 在该环境开 `ValidateOnBuild`/`ValidateScopes`——真实组合根的可解析性因此已在 CI 里被校验；而仓库禁止服务定位器，不存在绕过构造注入的隐藏环。

#### Infrastructure Layer（{ProjectName}.Infrastructure）
- **职责**: 数据持久化、第三方服务对接实现
- **包含**: EF Core Configurations、Repositories、ExternalServices、Caching，以及外部适配器专属的 Options
- **配置边界**: 外部适配器自己绑定和校验配置，Application 只依赖内层端口暴露的能力与可用状态，不直接读取 ClientId、ClientSecret、RedirectUri 等适配器细节

---

## 3. 编码规范

### 3.1 .NET 10 新特性（强制使用）

| 特性 | 使用场景 | 示例 |
|------|---------|------|
| **主构造函数** | 所有服务类 | `public class UserService(IRepository<User> repo) { }` |
| **record 类型** | 所有 DTO | `public record CreateUserInputDto { }` |
| **init 属性** | DTO 属性 | `public string Name { get; init; }` |
| **required 修饰符** | DTO 必填属性 | `public required string Name { get; init; }` |
| **文件范围 namespace** | 所有文件 | `namespace {ProjectName}.Application.Users;` |

**示例**:
```csharp
namespace {ProjectName}.Application.Users;

/// <summary>
/// 用户应用服务
/// </summary>
public class UserAppService(
    IRepository<User, Guid> userRepository,
    UserDomainService userDomainService,
    IObjectMapper objectMapper) : IAppService
{
    public async Task<UserOutputDto> CreateAsync(
        CreateUserInputDto input,
        CancellationToken cancellationToken = default)
    {
        var user = await userDomainService.CreateUserAsync(
            input.Username, input.Email, cancellationToken);

        var result = objectMapper.Map<User, UserOutputDto>(user);
        return result;
    }
}
```

### 3.2 异步编程

- **强制异步**: 所有 I/O 操作必须使用 `async/await`
- **命名约定**: 异步方法名必须以 `Async` 结尾
- **禁止阻塞**: 不得使用 `.Result` 或 `.Wait()`

### 3.2.1 时间获取（IClock）

- **禁止就地读取当前时间**：`DateTime.Now` / `DateTime.UtcNow` / `DateTimeOffset.Now` / `DateTimeOffset.UtcNow` / `TimeProvider.System.GetUtcNow()` 一律不用。它们隐藏依赖、不可测、易引入时区/时钟问题；只点名 `DateTime` 挡不住 `DateTimeOffset.UtcNow`——两者问题相同。
  - **把 `TimeProvider.System` 当默认时间源传进来不在此列**：`TimeProvider? timeProvider = null` + `timeProvider ?? TimeProvider.System` 是 .NET 官方的可测时钟形态，接缝在构造签名上，测试用 `FakeTimeProvider` 覆盖。框架组件用这一形态（业务项目注入 `IClock` 即可）。
- **统一通过** `Leistd.Timing.IClock` 获取当前时间（`clock.Now`）。`IClock` 由框架注册，直接注入即可。
- **领域对象（实体）保持 POCO，不注入服务**：实体的时间赋值方法应接收 `DateTime now` 参数，由调用方（领域服务/应用服务）注入 `IClock` 后传入。

```csharp
// ✅ 领域服务/应用服务：注入 IClock
public class UserDomainService(
    IRepository<User, Guid> userRepository,
    IClock clock)
{
    public async Task RecordLoginAsync(User user, string? ip)
    {
        user.RecordLoginSuccess(clock.Now, ip); // 把 now 传给实体
        await userRepository.UpdateAsync(user);
    }
}

// ✅ 实体：接收 now 参数，不注入服务
public void RecordLoginSuccess(DateTime now, string? ip = null)
{
    LastLoginTime = now;
    LastLoginIp = ip;
}

// ❌ 错误：实体内部直接取时间
public void RecordLoginSuccess(string? ip = null)
{
    LastLoginTime = DateTime.UtcNow; // 隐藏依赖、不可测
}
```

补充约定：

- **契约里的时间字段写明单位**，避免消费方误判量级（普通事件用秒、需亚秒精度的用毫秒），字段名或文档标注单位。
- **对外的线缆时间戳同样注入 `IClock`**。`IClock` 已承诺一律 UTC（且刻意不提供切换开关），因此不存在「边界可以用另一种取时间方式」的例外——序列化成 `DateTimeOffset` 时由 UTC 的 `DateTime` 隐式转换得到 `+00:00`。

### 3.3 充血模型设计

**实体设计原则**:
1. ✅ 属性使用 `private set`，封装内部状态
2. ✅ 必须有 `private` 无参构造函数（EF Core 需要）
3. ✅ 通过公共方法修改状态
4. ✅ 构造函数中进行必要的业务验证
5. ✅ 包含业务方法（如 `Enable()`, `Disable()`）

**示例**:
```csharp
namespace {ProjectName}.Domain.Users.Entities;

public class User : FullAuditedEntity<Guid>
{
    public string Username { get; private set; }
    public string Email { get; private set; }
    public bool IsActive { get; private set; } = true;

    // EF Core 构造函数
    private User()
    {
        Username = null!;
        Email = null!;
    }

    // 业务构造函数
    public User(string username, string email)
    {
        Id = Guid.NewGuid();
        Username = username;
        Email = email;
        // CreationTime 等创建审计字段在实体进入变更跟踪时由框架自动填充，
        // 不在实体内手写；如需业务时间字段，方法应接收 DateTime now 参数（见 §3.2.1）
    }

    // 业务方法
    public void Enable() => IsActive = true;
    public void Disable() => IsActive = false;
}
```

### 3.4 领域服务规范

**命名**: `*DomainService`（无需定义接口）
**注入命名**: `userDomainService`（camelCase）

**职责范围**:
- ✅ 跨实体的业务逻辑
- ✅ 复杂业务规则验证
- ✅ 实体的创建、更新、删除的核心逻辑
- ❌ DTO 转换
- ❌ 事务管理
- ❌ 数据查询和聚合
- ❌ 缓存、通知等副作用：由实体 `AddLocalEvent(...)` 发出本地事件，事务提交后由事件处理器处理，领域服务与调用方都不必各自记得去做；处理器实现 `IEventHandler<TEvent>` 并在应用层 `DependencyInjection` 显式注册

**示例**:
```csharp
namespace {ProjectName}.Domain.Users.DomainServices;

public class UserDomainService(
    IRepository<User, Guid> userRepository,
    IPasswordHasher passwordHasher)
{
    public async Task<User> CreateUserAsync(
        string username,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        // 唯一性校验
        if (await userRepository.AnyAsync(u => u.Username == username, cancellationToken))
            throw new BusinessException("User:UsernameTaken", $"Username '{username}' already exists.")
                .WithData("Username", username);

        // 密码哈希
        var passwordHash = passwordHasher.HashPassword(password);

        // 创建用户
        var user = new User(username, email, passwordHash);
        await userRepository.InsertAsync(user, cancellationToken: cancellationToken);

        return user;
    }
}
```

### 3.5 应用服务规范

**命名**: 接口 `I*AppService`，实现 `*AppService`
**基类**: 继承 `IAppService`
**注入命名**:
- 仓储：`userRepository`（camelCase）
- 领域服务：`userDomainService`（camelCase）

**职责**:
- ✅ 接收/返回 DTO
- ✅ 调用领域服务和仓储
- ✅ 事务管理（通过 UnitOfWork）
- ✅ DTO 映射（使用 Mapster `IObjectMapper`）
- ✅ 数据查询和聚合
- ❌ 核心业务规则（应在领域层）

**示例**:
```csharp
namespace {ProjectName}.Application.Users.AppServices;

public class UserAppService(
    IRepository<User, Guid> userRepository,
    UserDomainService userDomainService,
    IObjectMapper objectMapper) : IAppService
{
    public async Task<PagedResult<UserOutputDto>> GetPagedListAsync(
        GetUserPagedInputDto input,
        CancellationToken cancellationToken = default)
    {
        var query = await userRepository.GetQueryableAsync(cancellationToken);

        // 应用过滤条件
        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            query = query.Where(u => u.Username.Contains(input.Keyword));
        }

        // 获取总数
        var totalCount = await query.CountAsync(cancellationToken);

        // 分页查询
        var users = await query
            .OrderByDescending(u => u.CreationTime)
            .Skip(input.Offset)
            .Take(input.Limit)
            .ToListAsync(cancellationToken);

        // 映射
        var result = objectMapper.Map<List<User>, List<UserOutputDto>>(users);

        return new PagedResult<UserOutputDto>(totalCount, result);
    }
}
```

### 3.6 Controller 规范

框架组件已提供端点的能力（设置、权限管理、操作记录、通知、租户与租户连接）不再写 Controller：在 `Api/Hosting/ComponentEndpoints.cs` 里用组件的 `Map*` 给前缀与授权策略，
个别端点要追加元数据（两步验证放行、授权被拒留痕）按组件公开的端点名定位；组件不认识的业务动作（发信测试、模拟登录）才写 Controller，路由与组件端点不重叠。

**命名**: `*Controller`
**基类**: 继承 `BaseController`
**返回值**:
- 有响应体的普通业务接口直接返回具体 DTO，即 `Task<TOutputDto>`，不使用 `ActionResult<T>` 包装。
- 无返回对象的普通业务接口使用无泛型 `Task`；ASP.NET Core 将成功结果写为 HTTP 200 空响应体。
- 同一方法确需返回 `Redirect`、`Forbid`、`SignIn` 等多种 MVC 或协议结果时使用 `IActionResult`。

**方法命名与路由**：以 [API 规范](./api.md) §7 的操作→方法名→路由映射表为**单一权威**（分页 `GetPagedListAsync`、查询 `GetAsync`、创建 `CreateAsync`、更新 `UpdateAsync`/`PatchAsync`、删除 `DeleteAsync`，均 `/api/v1/{resource}` 前缀），此处不重复表格。

**示例**:
```csharp
namespace {ProjectName}.Api.Controllers;

[Authorize]
public class UserController(IUserAppService userAppService) : BaseController
{
    [HttpGet]
    public async Task<PagedResult<UserOutputDto>> GetPagedListAsync(
        [FromQuery] GetUserPagedInputDto input,
        CancellationToken cancellationToken)
    {
        return await userAppService.GetPagedListAsync(input, cancellationToken);
    }

    [HttpPost]
    public async Task<UserOutputDto> CreateAsync(
        [FromBody] CreateUserInputDto input,
        CancellationToken cancellationToken)
    {
        return await userAppService.CreateAsync(input, cancellationToken);
    }

    [HttpDelete("{id}")]
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await userAppService.DeleteAsync(id, cancellationToken);
    }
}
```

### 3.7 枚举持久化用字符串

枚举**一律以字符串持久化**（可读、可演进、不怕重排序），不存序数值。这是 [通用编码规范 §5.7](./coding-common.md) 「枚举边界转换」在 EF Core 的落地：

```csharp
// ✅ EF 配置：枚举转字符串列
builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
```

- 数据迁移里做数据转换时**按字符串比较**，不要对字符串列用整数比较（如对 varchar 列写 `Status = 3` 会在 PostgreSQL 报 42883 崩溃；应用 `Status = 'Active'` 或 `::text` 比较）。

### 3.8 授权/策略标识符用常量

- 授权策略名、权限名等**跨处引用的标识符用常量**，禁裸魔法串在多处各写——否则单侧改动漂移会致授权静默失配，且无编译报错。
- 参见 [API 规范](./api.md) 的权限命名约定。

#### 3.8.1 权限定义与界面一一对应

管理员在权限配置里看到的结构，应与用户在界面上看到的结构一致。这是前后端各自遵守的命名约定，不是代码依赖：两端独立演进、各自测试，不互相读取源码。

| 权限定义 | 对应界面 | 显示名 |
| --- | --- | --- |
| 分组（`PermissionConstant.Groups`） | 管理平台的菜单分组 | 与菜单分组标题一致 |
| 根权限（`App.{模块}`） | 菜单项与页面 | 与菜单项标题一致 |
| 子权限（`App.{模块}.{动作}`） | 页面上的操作按钮 | 常规动作统一为 Create / Edit / Delete（新建 / 编辑 / 删除），其余与按钮文案一致 |

- 定义里的 `displayName` 写**英文默认文案**，不写词条键：不启用多语言时界面直接展示它。译文按约定键 `Permission:{权限名}`、`PermissionGroup:{分组名}` 写在资源里；英文资源保留同名键（各语言键集合一致），其值必须等于默认文案。
- 权限是授权定义，可以没有菜单入口（只经接口使用）。但一个权限若只是另一个权限的前提（例如权限树只为授予而读，由「配置权限」守着即可），不单独定义。
- 后端的 `PermissionCatalogContractTests` 只核对后端自己：默认显示名可读、常规动作措辞统一、词条齐全且英文与默认文案一致。新增模块时权限定义、资源与前端菜单按本表各自改齐。

### 3.9 运行期可改的配置：设置与 Options 的分工

按取值的层级选路，不要为某个配置另写一套"提供方 + 快照 + 应用器"：

| 层级 | 做法 |
| --- | --- |
| 租户级、用户级 | 消费方经应用层的策略提供方读 `ISettingProvider`；不做成 Options——`IConfiguration` 整个进程一份，没有租户维度。替别人判断（如按收件人偏好）用 `GetOrNullForUserAsync` |
| 宿主级 | 在 `Api/Configuration/HostSettingBindings` 加一行绑定（`Bind` / `BindOption<T>`），消费方注入 `IOptionsMonitor<T>` / `IOptionsSnapshot<T>`（不要 `IOptions<T>`，它启动后不再读新值）；本身订阅配置重载的库（如 Serilog 的 `MinimumLevel`）直接绑定到它读的键 |

- 宿主级设置由设置组件经优先级最高的配置源覆盖部署配置：宿主开始接收请求之前先推入一次，写入提交后本进程随即生效，其它实例由周期任务跟上（`Leistd:Settings:Hosting:RefreshInterval`）；没设的项自然回落到配置文件与环境变量。
- 逐项合规、组合起来让 Options 校验不过的一组（如只设了发信账号、还没设口令）整组不生效，沿用上一组并记错误日志，消费方不会在改正之前每次取值都抛异常。
- 绑定了配置键的宿主级设置不写代码默认值：组件取部署基线作默认值，清除覆盖值即回到部署配置，「重置」不会回到一个写死的值。
- 值域声明在设置定义上（`AsBoolean()`、`AsInteger(min, max)`、`WithAllowedValues(...)`），写入端据此校验、设置页据此渲染控件；定义表达不了的规则（时区、发件地址、开启前提）写成 `Application/Settings/Validators` 下的 `ISettingValueValidator`。
- 部署期就定死的配置（凭据、连接、协议参数）照常用 `IOptions<T>`，也不要在服务里直接读 `IConfiguration["键"]`——已有 Options 类型就用它。

---

## 4. 命名规范

### 4.1 DTO 命名规范

| DTO 类型 | 命名规范 | 示例 |
|---------|---------|------|
| 分页查询输入 | `Get{Entity}PagedInputDto` | `GetUserPagedInputDto` |
| 创建输入 | `Create{Entity}InputDto` | `CreateUserInputDto` |
| 更新输入 | `Update{Entity}InputDto` | `UpdateUserInputDto` |
| 输出 | `{Entity}OutputDto` | `UserOutputDto` |
| SDK 单形态响应 | `{Concept}Dto` | `ServiceInfoDto`、`WhoAmIDto` |

> 分页输入 DTO 继承 `PageRequest`、字段约定见 [API 规范](./api.md) §6。
>
> `Client` SDK（`{ProjectName}.Client`）里没有请求/响应成对关系的单形态响应用 `{Concept}Dto`，不强套 `OutputDto`——Input/Output 后缀的作用是区分成对的请求与响应类型。

**分页 DTO 示例**:
```csharp
namespace {ProjectName}.Application.Users.Dtos;

/// <summary>
/// 获取用户分页列表输入 DTO
/// </summary>
public record GetUserPagedInputDto : PageRequest
{
    [Display(Name = "Search keyword")]
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Keyword { get; init; }

    [Display(Name = "Active status")]
    public bool? IsActive { get; init; }
}
```

**DTO 验证规范**:
- ✅ 使用 Data Annotations 进行模型验证
- ✅ `Display(Name)` 与 `ErrorMessage` 写**英文原文**，它们同时是本地化资源键：中文由 `Api/Resources/zh-CN.json` 的同名词条提供。直接写中文会绕过本地化，英文界面也显示中文
- ✅ 错误消息使用占位符（`{0} is required.`），优先复用资源里已有的模板
- ✅ 每个校验特性都显式写 `ErrorMessage`：不写时落成 .NET 内置英文（"The Name field is required."），它不是资源键，中文界面照样是英文
- ✅ 所有属性必须添加 `[Display(Name = "xxx")]`
- ✅ 入参 DTO 写成属性式（`{ get; init; }`），不用位置记录：校验失败返回的 `errors[].field` 按 JSON 命名策略与请求体字段同名，位置记录的键取自构造参数，不在换算范围内
- ✅ 前端表单按同一组规则做即时校验（长度、格式），服务端校验是兜底而不是用户第一次得知规则的地方
- ✅ 必填属性使用 `required` 修饰符
- ✅ 字段验证只在入口 DTO 做，内层信任（见 [通用编码规范 §5.2](./coding-common.md)）

**DTO 文件组织**：**一个用途一个 DTO 文件**；仅作为某父 DTO 内嵌成员的 item 类型可留在父文件中。业务入参 DTO 放应用层对应模块，**不落在表现层**（Api 层）。

### 4.2 变量与注入命名规范

- DTO 参数统一命名 `input`；返回对象统一命名 `result`；`IQueryable` 变量命名 `query` / `xxxQuery`。
- 仓储注入命名 `{entity}Repository`（如 `userRepository`）；领域服务命名 `{Entity}DomainService`。

---

## 5. 数据访问规范

### 5.1 Leistd 框架能力优先

**优先使用 Leistd 框架已有能力**:
- ✅ 分页查询使用 `GetPagedListAsync`（来自 `Leistd.Ddd.Infrastructure.Persistence.Repositories.EfCoreRepository`）
- ✅ IQueryable 异步扩展使用 `Leistd.Ddd.Infrastructure.Persistence.Repositories` 提供的方法
- ✅ 实体基类使用 `Entity<TKey>`、`FullAuditedEntity<TKey>` 等
- ✅ 业务库上下文经仓储或 `IDbContextProvider<TDbContext>` 取，**不直接构造注入**：直接注入的实例在对象激活时就按宿主库创建，
  分库租户下读写会落到宿主库（框架在同一作用域再按租户取上下文时会拒绝，表现为 500）。控制库上下文固定在宿主连接、不参与租户路由，可以直接注入
- ✅ DTO 映射使用 Mapster（继承 `MapsterProfile` 声明映射，注册结构参考现有 Profile）：实体、存储模型或框架模型到 DTO 的**投影**一律走模块 `Mappings/` 下的 Profile，
  调用方才知道的值（当前时刻、当前会话、读者身份）经 MapContext 传入；由多个来源**拼装**、带计算或本地化的结果 DTO 直接构造。不在 DTO 上写 `FromXxx` 之类的映射静态方法
- ✅ 请求外的异步活（发邮件等）交给 `IBackgroundTaskQueue`（后台作业组件，入队时的租户、主体与链路随工作项带到执行时），并发互斥用 `IDistributedLock`，不另起线程或自造锁；定期的维护活登记为周期任务（`AddRecurringJob`，显式选 `Cluster` 或 `EveryInstance`）
- ✅ 可还原的加密直接用 `IDataProtectionProvider`：构造时 `CreateProtector` 一次并复用，用途字符串固定带版本，解密只捕获 `CryptographicException`；不另立加密接口

### 5.2 仓储常用方法

| 方法 | 说明 |
|------|------|
| `GetByIdAsync(id)` | 根据 ID 获取单个实体 |
| `GetListAsync(predicate)` | 根据条件获取列表 |
| `GetQueryableAsync()` | 获取 IQueryable 用于复杂查询 |
| `GetQueryIncludingAsync(...)` | 获取带 Include 的 IQueryable |
| `InsertAsync(entity)` | 插入实体 |
| `UpdateAsync(entity)` | 更新实体 |
| `DeleteAsync(entity)` | 删除实体（软删除） |
| `AnyAsync(predicate)` | 判断是否存在 |

### 5.3 应用层使用 EF Core 规范

**规范**:
- ✅ 可以使用 `GetQueryableAsync` + `Include` 加载导航属性
- ✅ 可以使用 `GetQueryIncludingAsync` 扩展方法
- ✅ 使用 `using Leistd.Ddd.Infrastructure.Repositories;` 引入异步扩展
- ❌ 禁止在 Application/Domain 层引入 `using Microsoft.EntityFrameworkCore;`

**示例**:
```csharp
using Leistd.Ddd.Infrastructure.Repositories;  // 引入扩展方法

var query = await apiKeyRepository.GetQueryIncludingAsync(
    k => k.Bindings,
    k => k.Bindings.Select(b => b.ProviderGroup));

var apiKeys = await query
    .Where(k => k.UserId == userId)
    .ToListAsync(cancellationToken);
```

### 5.4 分页查询命名

分页查询方法命名 / 路由 / 分页参数 / 输入 DTO 命名，以 [API 规范](./api.md) §6/§7 为单一权威（必须 `GetPagedListAsync`、`/api/v1/{resource}`、`offset/limit`、`Get{Entity}PagedInputDto`）；完整应用服务示例见 §3.5。

### 5.5 避免重复验证

验证只在入口 DTO 做、内层信任（含"第二条调用路径"例外），以 [通用编码规范](./coding-common.md) §5.2 为准，此处不重复展开。

---

## 6. 异常处理与日志

### 6.1 异常类型

优先使用 .NET 内置异常；仅可预期、用户可恢复的业务规则失败使用 `BusinessException`。HTTP 映射以 [API 规范](./api.md) §4 为单一权威来源。

#### 用哪一类：看失败语义，不看是否位于请求路径

| 位置 | 用什么 | 为什么 |
| --- | --- | --- |
| **Domain / Application 业务规则** | `BusinessException(code, safeMessage)` | 错误码是机器契约和本地化键；默认 400，API 组合根可按码映射 |
| **Infrastructure 传输/配置/解析失败** | BCL 或专用技术异常 | 未显式映射时对外安全兜底为 500，细节进日志 |
| **启动期 / 组合期**（`Program.cs`、`Add*Services`、`IValidateOptions`） | BCL 异常（`InvalidOperationException` 等） | 没有 HTTP 响应也没有终端用户，进程就该起不来 |
| **参数与编程契约**（`ArgumentException`、重复 key、不该发生的状态） | BCL 异常 | 是缺陷不是业务失败，不该被翻译成状态码 |
| **一次性作业**（DbMigrator 之类控制台入口） | BCL 异常 | 同启动期；同一能力若同时有请求入口，由请求入口转换为 `Leistd.ExceptionHandling.Core` 异常 |

不要为 BCL 异常增加 `WithCode`：如果失败确实是业务契约，直接构造 `BusinessException`；如果是技术故障，保留原类型和异常链。

#### 配置错误在启动期失败，不要留到运行期

缺连接串、格式写错的 `DomainFormat` 这类**部署配置错误**，若留到运行期，表现是每个请求失败一次、而进程"健康"地跑着。用 `AddOptions<T>().Validate(...).ValidateOnStart()`。

> **组合期不能直接读配置来做判断。**`Add*Services(configuration)` 拿到的配置还不是最终值——集成测试通过 `WebApplicationFactory` 追加的覆盖此刻尚未合入，直接判断会误伤测试。要用 `.Configure<IConfiguration>((o, c) => ...)` 从 DI 取，让求值发生在配置定案之后。

**示例**:
```csharp
// 业务规则验证失败
if (await userRepository.AnyAsync(u => u.Username == username))
    throw new BusinessException("User:UsernameTaken", $"Username '{username}' already exists.")
        .WithData("Username", username);

// 资源不存在
var user = await userRepository.GetByIdAsync(id);
if (user == null)
    throw new BusinessException("User:NotFound", $"User {id} not found.")
        .WithData("Id", id);
```

### 6.2 日志记录

**使用场景**:
```csharp
// Information: 关键业务操作
logger.LogInformation("创建用户成功: {Username}", user.Username);

// Warning: 潜在问题
logger.LogWarning("用户 {UserId} 登录失败次数过多", userId);

// Error: 异常错误
logger.LogError(ex, "创建用户失败: {Username}", input.Username);
```

**最佳实践**:
- ✅ 使用结构化日志（消息模板 + 参数）
- ✅ 记录关键业务操作
- ❌ 不记录敏感信息（密码、Token）

---

## 7. 表现层（API）目录组织

表现层文件**按功能域归类收纳**，避免随业务增长在 Api 根目录平铺散乱；新增代码按分类归位。命名空间跟随目录层级。

### 7.1 按功能域分类

把表现层文件按关注点归入少数几个顶层分类，每类下再按模块收纳。通用分类思路（按项目实际有的才建，没有的不强建）：

- **实时/长连类**：WebSocket 端点、推送 Hub、连接注册表 → 一个"实时通信"大类。
- **网关/代理类**：反向代理转发、网关中间件。
- **鉴权类**：授权策略、授权特性、身份/凭据组装 → `Auth/`。
- **配置/组装类**：强类型 Options → `Options/`；宿主组装用的扩展方法（`*Extensions`）→ `Hosting/`。命名空间用描述性名称，
  不用笼统的 `Extensions`（微软《框架设计准则》：避免给专放扩展方法的命名空间起 "Extensions" 这类泛名），
  也不把非扩展类放进去。
- **健康检查类**：`IHealthCheck` 实现 → `HealthChecks/`，类名 `*HealthCheck`；启动期锁存的就绪标志与检查放在同一个类里（官方示例同一写法）。
- **后台服务类**：见 §7.2。
- **过滤器类**：类名以 `Filter` 结尾的（MVC/Hub 过滤器、通知投递过滤器等）统一放顶层 `Filters/`，不按所属功能域分散收纳。

已是清晰单一关注点的目录保持顶层，不强行再套壳。

请求体上限沿用 Kestrel 默认（约 30 MB），不在全局放宽；需要更大上传的端点用 `[RequestSizeLimit]` / `[RequestFormLimits]` 单独放宽（微软文件上传文档的建议：全局放宽扩大了拒绝服务的攻击面）。

### 7.2 后台服务按运行形态三分 + 后缀统一

后台服务（`IHostedService`/`BackgroundService`）按**运行形态**分类，类名后缀与目录对齐：

- **一次性启动引导** → `Initializer/`，类名 `*Initializer`。
- **常驻消费者**（持有队列/长循环消费）→ `Workers/`，类名 `*Worker`。
- **周期任务**（定时触发跑一轮）→ `BackgroundJobs/`，类名 `*Job`。

> 机制上"周期编排器"与"周期 Job"无区别（都是定时器+循环+每 tick 干活），故**统一 `*Job` 后缀**，不混用 `*Orchestrator`/`*Service`。

### 7.3 表现层职责红线（呼应 §2.1）

- **Controller/端点只做路由 + 鉴权 + 调应用服务**，不含业务校验/编排/直接操作实体或仓储；参数解析、越权守卫编排、多步领域调用下沉应用服务。
- **业务入参 DTO 不落表现层**，放应用层对应模块（见 §4.1）。
- **授权策略名等标识符用常量**，禁裸魔法串多处各写（见 §3.8）。
- **Controller 统一继承 `BaseController`**（见 §3.6）；确有特殊性的（视图渲染、纯透传代理、机器对机器端点）可用不同基类，属合理例外，就近注释说明。
- **以下留在表现层属合理边界、不算越层**（避免过度下沉）：后台服务取请求作用域服务推进工单状态机（调度编排）、连接/端点准入的存在性探针（等价鉴权）、后台 Worker 直写技术性日志实体（fire-and-forget 技术数据）、下发外部的 UTC 线缆时间戳直接用明确 UTC 取法（见 §3.2.1）。

---
