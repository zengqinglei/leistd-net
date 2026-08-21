# 审计

审计用于自动记录实体的创建人/创建时间、最后修改人/修改时间，以及删除人/删除时间（软删除），免去在每个应用服务里手写这些样板代码。典型场景包括：合规审计（谁在何时创建/修改/删除了这条数据）、软删除（业务上不允许物理删除，但需要标记"已删除"并保留数据）、审计字段的统一自动填充。

Leistd 通过一组标记接口（`ICreationAuditedObject` / `IModificationAuditedObject` / `IDeletionAuditedObject` 等）表达实体"具备哪些审计能力"，业务代码只需让实体实现相应接口；真正的字段填充由 `IAuditPropertySetter` 完成，业务代码无需手动赋值。

### 填充时机：创建在"进入跟踪时"，修改与删除在"保存时"

| 状态 | 时机 | 由谁调用 |
| --- | --- | --- |
| `Added`（`CreationTime` / `CreatorId`） | 实体**进入变更跟踪**时 | `BaseDbContext`（`ChangeTracker.Tracked` + `StateChanged`） |
| `Modified` / `Deleted` | `SavingChanges`（保存前） | `AuditSaveChangesInterceptor` |

这条分界线不是实现细节，两侧都有硬约束：

- **创建审计必须早于保存。** 仓储在工作单元内不立即保存，新增与保存之间可以跨越 `ICurrentPrincipalAccessor.Change` 的边界（服务间调用的主体切换、后台任务的模拟主体）。若在保存时刻取当前用户，`CreatorId` 会静默落成外层主体。而且在保存前实体的 `CreatorId` 一直是 null——保存前读它的代码（领域事件处理器、业务校验、导出）看到的都是空。
- **修改与删除必须留在保存时。** `Modified` / `Deleted` 是状态迁移的**结果**，跟踪事件在实体首次进入跟踪时就已触发完毕，抓不到"后来被改了"。

覆盖时的三层护栏（缺一层就会污染查询出来的数据）：`FromQuery` 的实体不碰、状态必须是 `Added`、值已有则不动（种子/导入/迁移显式赋过的值保留）。

> 这与 `TenantId` 的落值时机是同一条规则、同一个钩子，见[多租户组件](./multi-tenancy.md)。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 实体需要记录创建人/创建时间 | 实现 `ICreationAuditedObject`（或至少 `IHasCreationTime`） |
| 实体需要记录最后修改人/修改时间 | 实现 `IModificationAuditedObject`（或至少 `IHasModificationTime`） |
| 实体需要软删除（标记删除而非物理删除）并记录删除人/删除时间 | 实现 `IDeletionAuditedObject`（内含 `ISoftDelete`） |
| 实体需要以上全部审计能力 | 实现 `IFullAuditedObject` |
| 仅编写业务代码（定义实体、消费审计字段），不关心填充逻辑 | 只引用 `Leistd.Auditing.Core` 中的接口 |
| 使用 EF Core 作为持久化层，需要自动填充审计字段 | 引用 `Leistd.Auditing.EntityFrameworkCore`，注册 `AuditSaveChangesInterceptor` |

> 审计字段的自动填充依赖 EF Core；若不使用 EF Core，只能引用 `Leistd.Auditing.Core` 的接口自行实现填充逻辑。
>
> **创建审计要求 DbContext 继承 `BaseDbContext`**（`Leistd.Ddd.Infrastructure`）——它才是创建审计的落点。只挂拦截器而不继承 `BaseDbContext` 的 DbContext 只有修改与删除审计，创建审计不会填充。

## 安装

```bash
# 抽象（审计标记接口 + IAuditPropertySetter 定义）
dotnet add package Leistd.Auditing.Core

# EF Core 实现（AuditPropertySetter + AuditSaveChangesInterceptor）
dotnet add package Leistd.Auditing.EntityFrameworkCore
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

在 `Program.cs`（或组合根）注册审计能力：

```csharp
builder.Services.AddAuditingCore();      // 当前为空实现，仅占位保留扩展点
builder.Services.AddAuditingEfCore();    // 注册 IAuditPropertySetter 与 AuditSaveChangesInterceptor
```

`AddAuditingEfCore` 以 **Transient** 注册 `IAuditPropertySetter`（实现为 `AuditPropertySetter`）与 `AuditSaveChangesInterceptor`。它依赖容器中已注册的 `Leistd.Timing.IClock`（提供审计时间）与 `Leistd.Security.Users.ICurrentUser`（提供当前用户 ID），需确保这两者已注册。

`AddAuditingEfCore` **不会**自动把拦截器挂到 DbContext 上，调用方需要在配置 `DbContextOptionsBuilder` 时显式调用 `AddInterceptors`，传入已注册的 `AuditSaveChangesInterceptor` 实例：

```csharp
// 在 DbContext 注册处（options 为 DbContextOptionsBuilder）：
options.AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
```

创建审计不走拦截器，由 `BaseDbContext` 从容器解析 `IAuditPropertySetter`——因此**必须把 `IServiceProvider` 传给 `BaseDbContext` 的构造函数**（模板生成的 DbContext 已经这样写）。传 null 或用单参构造时，创建审计静默不填充。

## 使用

实体按需实现审计接口，字段会自动填充（创建审计在实体进入跟踪时，修改/删除在保存时），无需手动赋值。审计字段的 setter 用 `private set`（仅由框架经 EF Core 写入，业务代码不手动赋值）：

```csharp
public class Order : IFullAuditedObject
{
    public Guid Id { get; set; }

    // —— 创建审计 ——
    public DateTime CreationTime { get; private set; }
    public string? CreatorId { get; private set; }

    // —— 修改审计 ——
    public DateTime? LastModificationTime { get; private set; }
    public string? LastModifierId { get; private set; }

    // —— 删除审计（软删除）——
    public bool IsDeleted { get; private set; }
    public DateTime? DeletionTime { get; private set; }
    public string? DeleterId { get; private set; }
}
```

字段在保存时由 `AuditSaveChangesInterceptor` 自动填充——只要 `DbContext` 挂了该拦截器（见上方"配置 Provider"），无论如何写入都生效：

```csharp
// dbContext 为挂载了 AuditSaveChangesInterceptor 的 DbContext
dbContext.Set<Order>().Add(order);
await dbContext.SaveChangesAsync();
// order.CreationTime / order.CreatorId 已由拦截器自动填充

dbContext.Set<Order>().Remove(order);
await dbContext.SaveChangesAsync();
// 因 Order 实现 ISoftDelete：不会物理删除，
// 而是 IsDeleted=true 且写入 DeletionTime / DeleterId（详见"实现行为"）
```

> 在采用 [DDD 四层基座](../ddd-struct/ddd-struct.md) 的项目里，实体通常继承 `FullAuditedEntity<TKey>`（已内置这些字段）、并经仓储读写而非直接操作 `DbContext`；本组件本身不依赖 ddd-struct，上面直接实现接口 + 直用 `DbContext` 是其最小自包含用法。

## 接口参考

`Leistd.Auditing`（`Leistd.Auditing.Core` 包）命名空间：

| 成员 | 说明 |
| --- | --- |
| `IHasCreationTime` | 含 `CreationTime`（创建时间） |
| `ICreationAuditedObject : IHasCreationTime` | 额外含 `CreatorId`（创建者 ID） |
| `IHasModificationTime` | 含 `LastModificationTime`（可空，最后修改时间） |
| `IModificationAuditedObject : IHasModificationTime` | 额外含 `LastModifierId`（最后修改者 ID） |
| `IHasDeletionTime` | 含 `DeletionTime`（可空，删除时间） |
| `ISoftDelete` | 含 `IsDeleted`（是否已删除） |
| `IDeletionAuditedObject : IHasDeletionTime, ISoftDelete` | 额外含 `DeleterId`（删除者 ID） |
| `IFullAuditedObject` | 聚合 `ICreationAuditedObject` + `IModificationAuditedObject` + `IDeletionAuditedObject` 的完整审计对象 |
| `IAuditPropertySetter` | 审计属性设置器接口，含 `SetCreationProperties` / `SetModificationProperties` / `SetDeletionProperties`（均以 `object entityEntry` 作为参数，避免核心包依赖 EF Core） |

`Leistd.Auditing.EntityFrameworkCore` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `AuditPropertySetter : IAuditPropertySetter` | 基于 EF Core `EntityEntry` API 的默认实现，依赖 `IClock` 与 `ICurrentUser` |
| `AuditSaveChangesInterceptor` | `SaveChangesInterceptor` 实现，在 `SavingChanges`/`SavingChangesAsync` 中遍历 `ChangeTracker.Entries()` 并按实体状态自动调用 `IAuditPropertySetter` |

## 实现行为

### Leistd.Auditing.EntityFrameworkCore

`AuditSaveChangesInterceptor` 在保存前按 `EntityEntry.State` 决定动作：

| `EntityState` | 行为 |
| --- | --- |
| `Added` | 调用 `SetCreationProperties`（设置 `CreationTime` / `CreatorId`） |
| `Modified` | 若实体是 `ISoftDelete` 且 `IsDeleted == true`（本次改动只是软删除标记），**跳过**修改审计，避免与删除审计重复设置；否则调用 `SetModificationProperties`（设置 `LastModificationTime` / `LastModifierId`） |
| `Deleted` | 若实体实现 `ISoftDelete`：将 `entry.State` 由 `Deleted` **改写为 `Modified`**（物理删除转为逻辑删除），并调用 `SetDeletionProperties`（设置 `IsDeleted=true` / `DeletionTime` / `DeleterId`）；未实现 `ISoftDelete` 的实体按 EF Core 默认行为物理删除，不做任何审计处理 |

`AuditPropertySetter` 对每个字段均做**幂等判断，已设置则跳过**，避免覆盖已有值或重复赋值：

- `SetCreationTime`：仅当 `CreationTime == default` 时才写入 `clock.Normalize(clock.Now)`。
- `SetCreatorId`：仅当 `ICurrentUser.Id` 有值、且当前 `CreatorId` 为空时才写入。
- `SetLastModificationTime`：只要实体实现 `IHasModificationTime` 就**无条件**覆盖为 `clock.Normalize(clock.Now)`，不依赖 `ICurrentUser`、无幂等跳过（每次修改都刷新为最新时间）。
- `SetLastModifierId`：仅当 `ICurrentUser.Id` 有值、且实体实现 `IModificationAuditedObject` 时才写入，每次覆盖为最新修改者（无幂等跳过）。
- `SetIsDeleted`：仅当 `IsDeleted` 尚为 `false` 时才置为 `true`。
- `SetDeletionTime`：仅当 `DeletionTime` 尚无值时才写入。
- `SetDeleterId`：仅当 `ICurrentUser.Id` 有值、且当前 `DeleterId` 为空时才写入。

`ICurrentUser.Id` 未登录（无值）时，`CreatorId` / `LastModifierId` / `DeleterId` 均不会被设置，仅时间类字段照常填充。

`AuditPropertySetter` 的各 `SetXxx` 保护方法均为 `virtual`，可通过继承重写自定义单个字段的设置逻辑。

## 配置项 / Options

当前无配置项：`AddAuditingCore` 与 `AddAuditingEfCore` 均无参数，也未暴露 Options 类。

## 注意事项

- 软删除是**状态翻转**（`IsDeleted=true` + 写入删除审计字段），不是物理删除：`Deleted` 状态的实体只要实现 `ISoftDelete`，就会被拦截器改写为 `Modified` 并保留在数据库中；未实现 `ISoftDelete` 的实体仍会被物理删除。
- `AddAuditingEfCore` 不会自动把 `AuditSaveChangesInterceptor` 挂载到 DbContext，必须在 `DbContextOptionsBuilder.AddInterceptors(...)` 中显式添加，否则审计字段不会被填充。
- `AddAuditingEfCore` 依赖 `Leistd.Timing.IClock` 与 `Leistd.Security.Users.ICurrentUser` 已在容器中注册；未注册会在解析 `AuditPropertySetter` 时失败。
- 各字段的自动填充只在**实体实现对应接口**时生效；未实现相应接口（如只实现 `IHasCreationTime` 而未实现 `ICreationAuditedObject`）则只会填充该接口覆盖的字段。
- 创建/删除相关字段（`CreationTime`/`CreatorId`/`DeletionTime`/`DeleterId`）已设置后不会被覆盖；修改相关字段（`LastModificationTime`/`LastModifierId`）每次修改都会被覆盖为最新值。

## 相关

- [组件总览](./README.md)
- [当前用户与身份信息](./security.md)
- [权限授权](./authorization.md)（其 EF Core 存储引用审计基类型）
- [DDD 四层基座](../ddd-struct/ddd-struct.md)（`Leistd.Ddd.Infrastructure` 集成审计拦截器）
