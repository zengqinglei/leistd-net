# 技术栈模板：.NET 10 + Angular 21

> 全栈开发技术栈规范（后端 .NET 10 + 前端 Angular 21）

---

## 1. 核心技术栈

### 后端

| 组件 | 版本 | 说明 |
|------|------|------|
| **框架** | .NET 10+ | 主框架 |
| **ORM** | EF Core 10+ | 数据访问 |
| **数据库** | PostgreSQL 15+ | 主数据库 |
| **缓存** | Redis 7+ | 分布式缓存 |
| **对象映射** | AutoMapper | DTO 映射 |
| **日志** | Serilog | 结构化日志 |

### 前端

| 组件 | 版本 | 说明 |
|------|------|------|
| **框架** | Angular 21+ | 主框架 |
| **UI 组件** | PrimeNG 21+ | 组件库 |
| **CSS** | Tailwind CSS 4+ | 原子化 CSS |
| **语言** | TypeScript 5.8+ | 开发语言 |
| **状态管理** | Angular Signals | 响应式状态 |

---

## 2. 后端开发规范

### 分层架构

```
┌─────────────────────────────────────────┐
│           API Layer (Api)               │  ← HTTP 相关处理
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

**各层职责**：
- **API 层**：HTTP 相关处理（HttpContext、请求响应、路由）
- **Application 层**：协调业务逻辑（调用领域对象、领域服务）
- **Domain 层**：核心业务逻辑（领域对象行为、领域服务、业务规则）
- **Infrastructure 层**：数据持久化、第三方服务对接实现

### 编码规范

**主构造函数**（所有服务类）：
```csharp
public class UserAppService(
    IRepository<User, Guid> userRepository,
    UserDomainService userDomainService) : IAppService
{
    // 直接使用 userRepository 和 userDomainService
}
```

**DTO 使用 record 类型**：
```csharp
public record CreateUserInputDto
{
    public required string Username { get; init; }
    public required string Email { get; init; }
}
```

**文件范围 namespace**：
```csharp
namespace CompanyName.ProjectName.Application.Users;

public class UserAppService { }
```

**异步编程**：
- 所有 I/O 操作必须使用 `async/await`
- 异步方法名必须以 `Async` 结尾
- 禁止使用 `.Result` 或 `.Wait()`

### 命名规范

| 类型 | 命名规范 | 示例 |
|------|---------|------|
| **分页查询输入** | `Get{Entity}PagedInputDto` | `GetUserPagedInputDto` |
| **创建输入** | `Create{Entity}InputDto` | `CreateUserInputDto` |
| **输出** | `{Entity}OutputDto` | `UserOutputDto` |
| **应用服务** | `{Entity}AppService` | `UserAppService` |
| **领域服务** | `{Entity}DomainService` | `UserDomainService` |
| **仓储注入** | `{entity}Repository` | `userRepository` |

---

## 3. 前端开发规范

### 目录结构

```
frontend/
├── _mock/                                    # Mock 服务
│   ├── api/                                  # API Mock 处理器
│   └── data/                                 # 模拟数据源
├── src/
│   └── app/
│       ├── core/                             # 核心逻辑（非 UI）
│       │   ├── guards/                       # 路由守卫
│       │   ├── interceptors/                 # HTTP 拦截器
│       │   └── services/                     # 应用级核心服务
│       ├── features/                         # 业务功能模块
│       │   └── {module-name}/
│       │       ├── components/               # 页面级智能组件
│       │       ├── widgets/                  # 特性内可复用组件
│       │       ├── services/                 # 业务服务
│       │       └── models/                   # 数据模型
│       ├── shared/                           # 全局共享资源
│       │   ├── components/                   # 全局可复用组件
│       │   ├── directives/                   # 全局指令
│       │   └── pipes/                        # 全局管道
│       └── layout/                           # 应用布局
```

### 编码规范

**依赖注入**：
```typescript
// ✅ 正确：使用 inject()
export class UserService {
  private http = inject(HttpClient);
  private signal = signal<User[]>([]);
}

// ❌ 错误：构造函数注入
export class UserService {
  constructor(private http: HttpClient) {}
}
```

**组件命名**：
```
features/users/users.ts          # ✅ 组件文件（移除.component 后缀）
features/users/user-service.ts   # ✅ 服务文件
```

**状态管理**：
- 优先使用 Angular Signals
- 组件内部状态使用 `signal()`
- 计算状态使用 `computed()`

### 组件库使用

**PrimeNG 优先**：
- ✅ 首先在 PrimeNG 官方文档中寻找现成组件
- ✅ 尽量使用 PrimeNG v21 组件以及默认风格
- ✅ 仅在无法满足需求时才可创建自定义组件

**Tailwind CSS 使用**：
- ✅ 优先使用 Tailwind CSS v4 的原子类进行布局和微调
- ✅ 自定义样式使用 Tailwind CSS v4
- ✅ 任何自定义样式都必须与 PrimeNG 的主题风格保持一致

---

## 4. 前后端交互规范

### DTO 映射

**后端 DTO**：
```csharp
// 输入 DTO
public record CreateUserInputDto
{
    [Required(ErrorMessage = "{0}不能为空")]
    [StringLength(64, MinimumLength = 3)]
    public required string Username { get; init; }
}

// 输出 DTO
public record UserOutputDto
{
    public Guid Id { get; init; }
    public string Username { get; init; }
    public string Email { get; init; }
}
```

**前端 DTO**：
```typescript
// 输入 DTO
export interface CreateUserInput {
  username: string;
  email: string;
}

// 输出 DTO
export interface UserOutput {
  id: string;
  username: string;
  email: string;
}
```

### API 调用

**前端 Service**：
```typescript
export class UserService {
  private http = inject(HttpClient);
  private apiUrl = '/api/users';

  getPagedList(input: GetUserPagedInput): Observable<PagedResult<UserOutput>> {
    return this.http.get<PagedResult<UserOutput>>(this.apiUrl, { params: input });
  }

  create(input: CreateUserInput): Observable<UserOutput> {
    return this.http.post<UserOutput>(this.apiUrl, input);
  }
}
```

### Mock 同步

**Mock API**：
```typescript
// frontend/_mock/api/user-api.ts
export function setupUserApi(mock: MockServer) {
  mock.onGet('/api/users').reply((config) => {
    const users = getMockUsers();
    return [200, paginate(users, config.params)];
  });
}
```

**Mock 数据**：
```typescript
// frontend/_mock/data/users.ts
export function getMockUsers(): UserOutput[] {
  return [
    { id: '1', username: 'admin', email: 'admin@example.com' },
    { id: '2', username: 'user', email: 'user@example.com' },
  ];
}
```

---

## 5. 代码质量检查清单

### 后端检查

- [ ] 使用主构造函数
- [ ] 使用文件范围 namespace
- [ ] DTO 使用 record 类型
- [ ] 异步方法名以 `Async` 结尾
- [ ] 实体使用充血模型（属性 `private set`）
- [ ] HTTP 细节保留在 API 层
- [ ] Application 层无 EF Core 引用
- [ ] Domain 层无 EF Core 引用
- [ ] 使用 AutoMapper 进行 DTO 映射

### 前端检查

- [ ] 使用 `inject()` 进行依赖注入
- [ ] 优先使用 PrimeNG 组件
- [ ] 使用 Tailwind CSS 布局
- [ ] 展示型组件使用 OnPush
- [ ] 使用 Signals 管理状态
- [ ] Mock 数据与真实接口一致
- [ ] 组件遵循单一职责原则
- [ ] 文件命名符合规范

---

*最后更新：2026-06-07*
*维护：coding Skill*
*版本：v1.0.0*

