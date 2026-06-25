# 后端开发规范模板

> .NET 10 + EF Core + DDD 架构后端项目开发规范

---

## 1. 核心技术栈

- **框架**: .NET 10+
- **ORM**: EF Core 10+
- **数据库**: PostgreSQL 15+
- **缓存**: Redis 7+
- **对象映射**: AutoMapper
- **日志**: Serilog

---

## 2. 分层架构规范

### 架构分层

```
┌─────────────────────────────────────────┐
│         API Layer (Api)                 │  ← HTTP 相关处理
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

### 各层职责

#### API Layer
- **职责**: HTTP 相关处理（HttpContext、请求响应、路由）
- **原则**: HTTP 相关信息不应传递到 Application 层
- **包含**: Controllers、Middleware、Authentication

#### Application Layer
- **职责**: 协调业务逻辑（调用领域对象、领域服务）
- **包含**: AppServices、Dtos、Mappings、EventHandlers
- **可以**: 使用 EF Core 的 `Include` 进行数据查询和聚合

#### Domain Layer
- **职责**: 核心业务逻辑（领域对象行为、领域服务、业务规则）
- **包含**: Entities、ValueObjects、DomainServices、Events
- **严格禁止**:
  - 引用 `Microsoft.EntityFrameworkCore`
  - 使用 EF Core 特性（Include、ThenInclude）

#### Infrastructure Layer
- **职责**: 数据持久化、第三方服务对接实现
- **包含**: EF Core Configurations、Repositories、ExternalServices

---

## 3. 编码规范

### 3.1 .NET 10 新特性（强制使用）

| 特性 | 使用场景 | 示例 |
|------|---------|------|
| **主构造函数** | 所有服务类 | `public class UserService(IRepository<User> repo) { }` |
| **record 类型** | 所有 DTO | `public record CreateUserInputDto { }` |
| **init 属性** | DTO 属性 | `public string Name { get; init; }` |
| **required 修饰符** | DTO 必填属性 | `public required string Name { get; init; }` |
| **文件范围 namespace** | 所有文件 | `namespace AiRelay.Application.Users;` |

### 3.2 异步编程

- **强制异步**: 所有 I/O 操作必须使用 `async/await`
- **命名约定**: 异步方法名必须以 `Async` 结尾
- **禁止阻塞**: 不得使用 `.Result` 或 `.Wait()`

### 3.3 充血模型设计

**实体设计原则**:
1. ✅ 属性使用 `private set`，封装内部状态
2. ✅ 必须有 `private` 无参构造函数（EF Core 需要）
3. ✅ 通过公共方法修改状态
4. ✅ 构造函数中进行必要的业务验证
5. ✅ 包含业务方法（如 `Enable()`, `Disable()`）

**示例**:
```csharp
namespace AiRelay.Domain.Users.Entities;

public class User : FullAuditedEntity<Guid>
{
    public string Username { get; private set; }
    public bool IsActive { get; private set; } = true;

    // EF Core 构造函数
    private User()
    {
        Username = null!;
    }

    // 业务构造函数
    public User(string username)
    {
        Id = Guid.NewGuid();
        Username = username;
        IsActive = true;
    }

    // 业务方法
    public void Enable() => IsActive = true;
    public void Disable() => IsActive = false;
}
```

---

## 4. 命名规范

### 4.1 DTO 命名规范

| DTO 类型 | 命名规范 | 示例 |
|---------|---------|------|
| 分页查询输入 | `Get{Entity}PagedInputDto` | `GetUserPagedInputDto` |
| 创建输入 | `Create{Entity}InputDto` | `CreateUserInputDto` |
| 更新输入 | `Update{Entity}InputDto` | `UpdateUserInputDto` |
| 输出 | `{Entity}OutputDto` | `UserOutputDto` |

### 4.2 变量命名规范

**方法参数**:
```csharp
// ✅ 正确：Dto 参数统一命名为 input
public async Task<UserOutputDto> CreateAsync(
    CreateUserInputDto input,
    CancellationToken cancellationToken = default)
```

**返回变量**:
```csharp
// ✅ 正确：返回对象统一命名为 result
var result = objectMapper.Map<User, UserOutputDto>(user);
return result;
```

**仓储注入命名**:
```csharp
public class UserAppService(
    IRepository<User, Guid> userRepository,
    UserDomainService userDomainService) : IAppService
{
}
```

---

## 5. 数据访问规范

### 5.1 仓储常用方法

| 方法 | 说明 |
|------|------|
| `GetByIdAsync(id)` | 根据 ID 获取单个实体 |
| `GetListAsync(predicate)` | 根据条件获取列表 |
| `GetQueryableAsync()` | 获取 IQueryable 用于复杂查询 |
| `InsertAsync(entity)` | 插入实体 |
| `UpdateAsync(entity)` | 更新实体 |
| `DeleteAsync(entity)` | 删除实体（软删除） |
| `AnyAsync(predicate)` | 判断是否存在 |

### 5.2 分页查询命名规范

**方法命名**: 必须使用 `GetPagedListAsync`

```csharp
public async Task<PagedResultDto<UserOutputDto>> GetPagedListAsync(
    GetUserPagedInputDto input,
    CancellationToken cancellationToken = default)
{
    // ...
    return new PagedResultDto<UserOutputDto>(totalCount, result);
}
```

---

## 6. 异常处理与日志

### 6.1 异常类型

| 异常类型 | HTTP 状态码 | 使用场景 |
|---------|-----------|---------|
| `BadRequestException` | 400 | 业务规则验证失败 |
| `NotFoundException` | 404 | 资源不存在 |
| `UnauthorizedException` | 401 | 未授权 |
| `ForbiddenException` | 403 | 无权限 |

### 6.2 日志记录

```csharp
// Information: 关键业务操作
logger.LogInformation("创建用户成功：{Username}", user.Username);

// Warning: 潜在问题
logger.LogWarning("用户 {UserId} 登录失败次数过多", userId);

// Error: 异常错误
logger.LogError(ex, "创建用户失败：{Username}", input.Username);
```

---

## 7. 规范检查清单

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

### ✅ 命名规范检查

- [ ] DTO 命名符合规范
- [ ] Dto 参数命名为 `input`
- [ ] 返回对象命名为 `result`
- [ ] 仓储注入命名为 `{entity}Repository`

---

*模板版本：v1.0.0*
*最后更新：2026-06-07*
*维护：coding Skill*

