# 后端开发规范

本文档为模板项目默认的 .NET 10、EF Core、DDD 后端开发规范。若新项目未采用该技术栈，不应把本文规则作为通用默认事实。

> **注意**: 本文档中的所有开发活动，都必须同时遵循 **[项目通用开发规范](./coding-common.md)** 中定义的 Git 工作流和提交规范。

---

## 1. 核心技术栈

- **框架**: .NET 10+
- **ORM**: EF Core 10+
- **数据库**: PostgreSQL 15+
- **缓存**: Redis 7+
- **对象映射**: Mapster（`Leistd.ObjectMapping.Mapster` + `IObjectMapper`）
- **依赖注入**: Microsoft.Extensions.DependencyInjection
- **日志**: Serilog

---

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
- **包含**: AppServices、Dtos、Mappings、EventHandlers
- **可以**: 使用 EF Core 的 `Include`、`GetQueryIncludingAsync` 进行数据查询和聚合

#### Domain Layer（{ProjectName}.Domain）
- **职责**: 核心业务逻辑（领域对象行为、领域服务、业务规则）
- **包含**: Entities、ValueObjects、DomainServices、Events、Specifications
- **接口定义**: 第三方服务接口、持久化接口（IRepository）
- **严格禁止**:
  - 引用 `Microsoft.EntityFrameworkCore`
  - 使用 EF Core 特性（Include、ThenInclude）
  - 领域服务之间相互依赖

#### Infrastructure Layer（{ProjectName}.Infrastructure）
- **职责**: 数据持久化、第三方服务对接实现
- **包含**: EF Core Configurations、Repositories、ExternalServices、Caching

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

- **禁止直接使用** `DateTime.Now` / `DateTime.UtcNow`：它们隐藏依赖、不可测、易引入时区/时钟问题。
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
- **对外的线缆时间戳明确用 UTC**。若 `IClock` 不保证 UTC 语义，此类**边界**时间戳直接用明确的 UTC 取法属**合理例外**（仅限对外序列化边界，不下沉进领域）。

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
        // CreationTime 等审计字段由框架审计拦截器在保存时自动填充，
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
            throw new BadRequestException($"用户名 '{username}' 已存在");

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
    public async Task<PagedResultDto<UserOutputDto>> GetPagedListAsync(
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

        return new PagedResultDto<UserOutputDto>(totalCount, result);
    }
}
```

### 3.6 Controller 规范

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
    public async Task<PagedResultDto<UserOutputDto>> GetPagedListAsync(
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

---

## 4. 命名规范

### 4.1 DTO 命名规范

| DTO 类型 | 命名规范 | 示例 |
|---------|---------|------|
| 分页查询输入 | `Get{Entity}PagedInputDto` | `GetUserPagedInputDto` |
| 创建输入 | `Create{Entity}InputDto` | `CreateUserInputDto` |
| 更新输入 | `Update{Entity}InputDto` | `UpdateUserInputDto` |
| 输出 | `{Entity}OutputDto` | `UserOutputDto` |

> 分页输入 DTO 继承 `PagedRequestDto`、字段约定见 [API 规范](./api.md) §6。

**分页 DTO 示例**:
```csharp
namespace {ProjectName}.Application.Users.Dtos;

/// <summary>
/// 获取用户分页列表输入 DTO
/// </summary>
public record GetUserPagedInputDto : PagedRequestDto
{
    [Display(Name = "搜索关键字")]
    [MaxLength(256, ErrorMessage = "{0}长度不能超过 {1} 个字符")]
    public string? Keyword { get; init; }

    [Display(Name = "是否启用")]
    public bool? IsActive { get; init; }
}
```

**DTO 验证规范**:
- ✅ 使用 Data Annotations 进行模型验证
- ✅ 错误消息使用占位符（`{0}不能为空`）
- ✅ 所有属性必须添加 `[Display(Name = "xxx")]`
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
- ✅ 分页查询使用 `GetPagedListAsync`（来自 `Leistd.Ddd.Infrastructure.Repositories.EfCoreRepository`）
- ✅ IQueryable 异步扩展使用 `Leistd.Ddd.Infrastructure.Repositories` 提供的方法
- ✅ 实体基类使用 `Entity<TKey>`、`FullAuditedEntity<TKey>` 等
- ✅ DTO 映射使用 Mapster（继承 `MapsterProfile` 声明映射，注册结构参考现有 Profile）

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

使用 `Leistd.Exception.Core` 提供的异常类型。**异常类型 → HTTP 状态码的完整映射以 [API 规范](./api.md) §4「异常类型映射」为单一权威来源**（含 `ConflictException`/409 等），此处不重复维护，避免不一致。

**示例**:
```csharp
// 业务规则验证失败
if (await userRepository.AnyAsync(u => u.Username == username))
    throw new BadRequestException($"用户名 '{username}' 已存在");

// 资源不存在
var user = await userRepository.GetByIdAsync(id);
if (user == null)
    throw new NotFoundException($"用户 {id} 不存在");
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
- **鉴权类**：授权策略、授权特性、身份/凭据组装。
- **配置/组装类**：强类型 Options、DI/管道扩展方法。
- **后台服务类**：见 §7.2。

已是清晰单一关注点的目录保持顶层，不强行再套壳。

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

## 附录：规范检查清单

### ✅ 分层架构检查

- [ ] HTTP 相关处理保留在 API 层
- [ ] Application 层协调业务逻辑
- [ ] Domain 层包含核心业务逻辑
- [ ] Infrastructure 层负责技术实现
- [ ] Domain 层无 EF Core 引用

### ✅ 编码规范检查

- [ ] 使用主构造函数
- [ ] 使用文件范围 namespace
- [ ] DTO 使用 record 类型
- [ ] 异步方法名以 `Async` 结尾
- [ ] 实体使用充血模型
- [ ] 时间通过 `IClock` 获取，无 `DateTime.Now/UtcNow` 直用
- [ ] 实体时间方法接收 `now` 参数（不在实体内取时间）

### ✅ 命名规范检查

- [ ] DTO 命名符合规范
- [ ] Dto 参数命名为 `input`
- [ ] 返回对象命名为 `result`
- [ ] 仓储注入命名为 `{entity}Repository`
- [ ] 领域服务命名为 `{Entity}DomainService`

### ✅ Leistd 框架使用检查

- [ ] 优先使用 Leistd 框架已有能力
- [ ] 应用服务继承自 `IAppService`
- [ ] 分页查询使用 `GetPagedListAsync`
- [ ] DTO 映射使用 Mapster（`IObjectMapper` + `MapsterProfile`）
- [ ] 使用 Leistd 提供的异步扩展方法

---

