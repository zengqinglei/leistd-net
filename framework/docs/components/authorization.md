# 权限授权

权限授权用于在**声明式定义权限**的基础上，于运行时判断"当前用户是否有权执行某操作"。与基于角色的粗粒度控制不同，权限授权把每个可授权的操作抽象为一个具名权限（如 `Orders.Read`），支持按用户、按角色分别授予，并可与 ASP.NET Core 的策略（Policy）管道无缝集成，让 `[Authorize(Policy = "权限名")]` 直接生效。典型场景包括：后台管理系统的按钮级/接口级权限控制、多租户场景下超级管理员绕过检查、权限授予需要持久化并支持动态增删。

Leistd 通过 `IPermissionChecker` 提供统一的权限检查入口，业务代码只需注入接口做判断；权限的"有哪些"由 `IPermissionDefinitionProvider` 声明式定义，权限的"谁被授予了"由 `IPermissionGrantStore`/`IPermissionGrantManager` 负责存取。`Leistd.Authorization.AspNetCore` 把权限检查接入 ASP.NET Core 授权策略管道，`Leistd.Authorization.EntityFrameworkCore` 提供基于 EF Core 的授予持久化实现。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 需要细粒度、按操作划分的权限控制（而非仅角色判断） | 完整引入 Core + AspNetCore（+ EntityFrameworkCore） |
| 需要 `[Authorize(Policy = "权限名")]` 这类声明式接口/控制器权限校验 | `Leistd.Authorization.AspNetCore` |
| 权限授予需要持久化到数据库，支持运行时增删 | `Leistd.Authorization.EntityFrameworkCore` |
| 仅编写业务代码（定义权限 / 检查权限），不关心存储实现 | 只引用 `Leistd.Authorization.Core` 中的接口 |

## 安装

```bash
# 抽象 + 默认检查器（权限定义、IPermissionChecker）
dotnet add package Leistd.Authorization.Core

# ASP.NET Core 策略集成（[Authorize(Policy = "权限名")] 动态生效）
dotnet add package Leistd.Authorization.AspNetCore

# EF Core 授予存储（权限授予持久化到数据库）
dotnet add package Leistd.Authorization.EntityFrameworkCore
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

三层各自的 DI 扩展方法：

```csharp
// Core：注册 IPermissionDefinitionManager 与 IPermissionChecker（默认实现）
builder.Services.AddPermissionAuthorizationCore();

// AspNetCore：接入策略管道（内部已调用 AddPermissionAuthorizationCore）
builder.Services.AddPermissionAuthorization();

// EntityFrameworkCore：基于指定 DbContext 的授予存储（内部已调用 AddPermissionAuthorizationCore）
builder.Services.AddAuthorizationEfCore<AppDbContext>();
```

- `AddPermissionAuthorizationCore`：以 **Singleton** 注册 `IPermissionDefinitionManager`（`PermissionDefinitionManager`），以 **Transient** 注册 `IPermissionChecker`（`DefaultPermissionChecker`）。
- `AddPermissionAuthorization`：在调用 `AddPermissionAuthorizationCore` 的基础上，以 **Singleton** 注册 `IAuthorizationPolicyProvider`（`PermissionPolicyProvider`），以 **Transient** 注册 `IAuthorizationHandler`（`PermissionAuthorizationHandler`）。依赖调用方已注册 `IPermissionChecker` 与 `IPermissionDefinitionManager`，并已调用 ASP.NET Core 自带的 `AddAuthorization()`。
- `AddAuthorizationEfCore<TDbContext>`：在调用 `AddPermissionAuthorizationCore` 的基础上，以 **Transient** 注册 `IPermissionGrantStore`（`EfCorePermissionGrantStore<TDbContext>`）与 `IPermissionGrantManager`（`EfCorePermissionGrantManager<TDbContext>`）。

EF Core 实体映射需在 `DbContext.OnModelCreating` 中显式应用：

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigureAuthorization();
}
```

业务代码还需自行实现并注册 `IPermissionSubjectProvider`（提供"当前用户是谁、属于哪些角色、是否超管"），`IPermissionChecker` 依赖它获取检查主体；本组件不提供默认实现。

## 使用

**第一步：定义权限**——实现 `IPermissionDefinitionProvider` 并注册到 DI（供 `PermissionDefinitionManager` 加载）：

```csharp
public class OrdersPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetOrAddGroup("Orders", "订单管理");
        group.AddPermission("Orders.Read", "查看订单");
        group.AddPermission("Orders.Write", "编辑订单");
    }
}

builder.Services.AddSingleton<IPermissionDefinitionProvider, OrdersPermissionDefinitionProvider>();
```

**第二步：检查权限**——注入 `IPermissionChecker` 在业务代码中判断：

```csharp
public class OrderService(IPermissionChecker permissionChecker)
{
    public async Task<bool> CanReadAsync()
    {
        return await permissionChecker.IsGrantedAsync("Orders.Read");
    }
}
```

**第三步：控制器/接口声明式校验**——引入 `Leistd.Authorization.AspNetCore` 后，`[Authorize(Policy = "权限名")]` 直接生效：

```csharp
// 权限名作为策略名：命中权限才放行，否则返回 403
[Authorize(Policy = "Orders.Read")]
[HttpGet("orders")]
public Task<IReadOnlyList<OrderDto>> GetOrders([FromQuery] OrderQuery query)
    => orderService.GetListAsync(query);
```

## 接口参考

`Leistd.Authorization.Core` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `IPermissionChecker` | 权限检查统一入口 |
| `IPermissionChecker.IsGrantedAsync(name, ct)` | 检查当前用户是否拥有指定权限，返回 `bool` |
| `IPermissionChecker.IsGrantedAsync(names, ct)` | 批量检查多个权限，返回 `MultiplePermissionGrantResult` |
| `MultiplePermissionGrantResult` | 批量检查结果，含 `Results` 字典、`AllGranted`（全部授予）、`AnyGranted`（至少一个授予） |
| `IPermissionDefinitionProvider` | 权限定义提供者，业务项目实现 `Define` 声明权限 |
| `IPermissionDefinitionContext` | 定义期上下文：`GetOrAddGroup`、`AddPermission`、`GetPermissionOrNull` |
| `IPermissionGroupDefinition` | 权限组：`AddPermission`、`GetPermissionOrNull` |
| `IPermissionDefinition` | 单个权限定义：`Name`、`DisplayName`、`Parent`、`Children`、`IsEnabled`、`AddChild` |
| `IPermissionDefinitionManager` | 权限定义查询：`GetOrNull(name)`、`GetAll()` |
| `IPermissionSubjectProvider` | 当前权限检查主体提供器，业务项目需自行实现 |
| `IPermissionSubjectProvider.GetCurrentSubjectAsync(ct)` | 获取当前主体，未登录/无法识别时返回 `null` |
| `PermissionSubject` | 检查主体记录：`UserId`、`RoleIds`、`IsSuperAdmin` |
| `IPermissionGrantStore` | 权限授予存储，负责查询"谁被授予了什么权限" |
| `IPermissionGrantManager` | 权限授予管理器，负责授予/撤销并读取已授予权限 |
| `PermissionGrantProviderNames` | 授予对象类型常量：`User`、`Role` |

`Leistd.Authorization.AspNetCore` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `PermissionPolicyProvider : IAuthorizationPolicyProvider` | 把权限名当作策略名，动态构建携带 `PermissionRequirement` 的策略 |
| `PermissionAuthorizationHandler` | 将 `PermissionRequirement` 委托给 `IPermissionChecker` 校验 |
| `PermissionRequirement` | 授权需求，含单个 `PermissionName` |

`Leistd.Authorization.EntityFrameworkCore` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `PermissionGrantRecord` | 权限授予持久化实体：`Id`（Guid v7）、`PermissionName`、`ProviderName`、`ProviderKey`；实现 `ICreationAuditedObject`（`CreationTime`、`CreatorId` 由审计拦截器填充） |
| `PermissionGrantRecord.ForUser(string permissionName, Guid userId)` | 构建用户授予记录的静态工厂方法（`userId` 为 `Guid`，内部转为字符串 `ProviderKey`） |
| `PermissionGrantRecord.ForRole(string permissionName, Guid roleId)` | 构建角色授予记录的静态工厂方法（`roleId` 为 `Guid`，内部转为字符串 `ProviderKey`） |
| `PermissionGrantRecordConfiguration` | `PermissionGrantRecord` 的 EF Core 实体配置（`IEntityTypeConfiguration<PermissionGrantRecord>`） |
| `EfCorePermissionGrantStore<TDbContext>` | `IPermissionGrantStore` 的 EF Core 实现 |
| `EfCorePermissionGrantManager<TDbContext>` | `IPermissionGrantManager` 的 EF Core 实现 |

## 实现行为

### PermissionPolicyProvider（动态策略生成）

- `GetPolicyAsync(policyName)` 先查 `IPermissionDefinitionManager.GetOrNull(policyName)`：命中已定义权限时，用 `AuthorizationPolicyBuilder` 附加一个 `PermissionRequirement(policyName)` 并构建策略；未命中时回退到 `DefaultAuthorizationPolicyProvider`，因此普通 `[Authorize]`、`[Authorize(Roles=...)]` 及显式注册的命名策略（如 "SuperAdmin"）不受影响。
- `GetDefaultPolicyAsync` / `GetFallbackPolicyAsync` 均直接委托给内部的 `DefaultAuthorizationPolicyProvider`。

### DefaultPermissionChecker（默认检查流程）

- `IsGrantedAsync(name, ct)`：权限名为空白直接返回 `false`；通过 `IPermissionSubjectProvider` 取不到当前主体（未登录）返回 `false`；`PermissionSubject.IsSuperAdmin` 为 `true` 时**直接返回 `true`，不查存储**；否则调用 `IPermissionGrantStore.IsGrantedToUserOrRolesAsync` 按用户 ID 与角色 ID 集合批量查询。
- `IsGrantedAsync(names, ct)`：对传入名称先按 `StringComparer.Ordinal` 去重，过滤空白后统一初始化为 `false`；主体为 `null` 时全部为 `false`；`IsSuperAdmin` 时全部为 `true`；否则一次性调用存储层批量查询回填结果。
- 两个重载都以**一次存储调用**完成多权限判断（非逐个查询），减少往返。

### EfCorePermissionGrantStore / EfCorePermissionGrantManager（EF Core 存储行为）

- `PermissionGrantRecord` 唯一性由数据库唯一索引 `(PermissionName, ProviderName, ProviderKey)` 保证；`EfCorePermissionGrantManager.GrantAsync` 授予前先 `AnyAsync` 判重，已存在则直接返回，不会插入重复记录。
- 撤销 (`RevokeAsync`) 按三元组精确匹配删除，不影响同权限名下其他用户/角色的授予记录。
- `IsGrantedToUserOrRolesAsync` 与 `GetGrantedPermissionsFor*Async` 查询均使用 `AsNoTracking()`，为只读查询优化；返回集合按 `StringComparer.Ordinal` 去重。
- `IsGrantedToAnyRoleAsync` 在角色 ID 集合为空时直接返回 `false`，不发起查询。
- `PermissionGrantRecord.PermissionName` 最大长度 256，`ProviderName` 最大长度 32，`ProviderKey` 最大长度 128，`CreatorId` 最大长度 64（均为 EF Core 实体配置中的硬编码约束）。

## 配置项 / Options

当前无配置项：三个 DI 扩展方法（`AddPermissionAuthorizationCore`、`AddPermissionAuthorization`、`AddAuthorizationEfCore<TDbContext>`）均无参数，也未暴露 Options 类。

## 注意事项

- `IPermissionSubjectProvider` 没有默认实现，必须由业务项目提供（通常基于 `ICurrentUser` 等安全组件），否则 `IPermissionChecker` 恒定返回"未授予"。
- `PermissionSubject.IsSuperAdmin` 为 `true` 时会**跳过存储层查询**直接判定为已授予，业务项目需自行保证该标志的正确来源（约定的 claim 类型见 `Leistd.Security.Claims.CustomClaimTypes.IsSuperAdmin`）。
- `AddPermissionAuthorization` 依赖调用方已执行 ASP.NET Core 原生的 `AddAuthorization()`；`PermissionPolicyProvider` 只在策略名命中已定义权限时接管，其余策略名回退到默认提供器，不会破坏既有的角色/命名策略。
- `EfCorePermissionGrantManager`/`EfCorePermissionGrantStore` 依赖调用方在 `OnModelCreating` 中执行 `modelBuilder.ConfigureAuthorization()`，否则 `PermissionGrantRecord` 不会被正确映射。
- 权限定义（`IPermissionDefinitionProvider`）在 `PermissionDefinitionManager` 构造时一次性加载并缓存于内存，运行期新增/修改权限定义需重启进程；权限**授予**（谁拥有权限）则可随时通过 `IPermissionGrantManager` 动态增删，无需重启。

## 相关

- [组件总览](./README.md)
- [依赖注入](./dependency-injection.md)
- [审计](./auditing.md)
