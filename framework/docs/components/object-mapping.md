# 对象映射

在分层架构中，实体（Entity）、领域模型、DTO、视图模型之间需要频繁地相互转换。手写赋值代码冗长、易漏字段、难维护。对象映射组件把「把 A 类型的字段拷贝到 B 类型」这件事统一抽象成一个服务，业务代码只依赖 `IObjectMapper` 接口调用 `Map`，而具体用 AutoMapper 还是 Mapster 完成转换，由 DI 注册决定。

典型场景：应用服务把领域实体转换为返回给前端的 DTO、把入参 DTO 物化为待持久化的实体、批量列表转换、以及在仓储查询中将映射下推到数据库（投影）。Leistd 通过 `IObjectMapper` 屏蔽底层映射库——切换实现只需更换注册方法，不改业务调用代码。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 需要成熟的 `Profile` 配置体系、`ProjectTo` 查询投影（EF Core 下推数据库） | `Leistd.ObjectMapping.AutoMapper` |
| 追求高性能、零配置约定映射、运行时编译 | `Leistd.ObjectMapping.Mapster` |
| 仅在领域/应用层依赖映射抽象编写业务代码 | 只引用 `Leistd.ObjectMapping.Core` |

> 两个实现都注册到同一个 `IObjectMapper`，业务代码无需感知差异。`ProjectTo` 查询投影是 AutoMapper 实现独有的能力（见[实现行为](#实现行为)）。

## 安装

```bash
# 抽象（业务代码引用；实现包已传递引用，通常无需单独添加）
dotnet add package Leistd.ObjectMapping.Core

# 二选一：AutoMapper 或 Mapster
dotnet add package Leistd.ObjectMapping.AutoMapper
dotnet add package Leistd.ObjectMapping.Mapster
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

在 `Program.cs` 注册其中一种实现，两者都将实现绑定到 `IObjectMapper`（Singleton），并各自注册底层映射库的 `IMapper`：

```csharp
// AutoMapper —— 在 Configurators 中添加 Profile 或内联映射配置
builder.Services.AddAutoMapperObjectMapper(options =>
{
    options.Configurators.Add(cfg => cfg.AddProfile<OrderProfile>());
    options.ValidateMappings = true; // 启动时校验配置完整性
});

// 或：Mapster —— 可从程序集扫描 MapsterProfile
builder.Services.AddMapsterObjectMapper(options =>
{
    options.AddProfiles(typeof(OrderMapsterProfile).Assembly);
    options.ValidateMappings = true; // 启动时 Compile 校验
});
```

`configure` 参数可选，省略时使用默认配置（无映射规则）。

## 使用

注入 `IObjectMapper`，调用 `Map` 完成转换：

```csharp
public class OrderAppService(IObjectMapper mapper)
{
    // 创建新实例：实体 -> DTO
    public OrderDto ToDto(Order order)
        => mapper.Map<Order, OrderDto>(order);

    // 映射到现有实例：用 DTO 更新已加载的实体（返回同一 destination）
    public void Apply(UpdateOrderDto dto, Order order)
        => mapper.Map(dto, order);

    // 批量映射（Core 扩展方法）
    public List<OrderDto> ToDtos(IEnumerable<Order> orders)
        => mapper.MapList<Order, OrderDto>(orders);
}
```

批量映射 `MapList` 是 `Leistd.ObjectMapping.Core.Extensions` 命名空间下的扩展方法，需 `using Leistd.ObjectMapping.Core.Extensions;`。

AutoMapper 实现下可将映射下推到数据库查询（投影），仅查询目标字段：

```csharp
using Leistd.ObjectMapping.AutoMapper.Extensions;

IQueryable<OrderDto> query = dbContext.Orders
    .Where(o => o.IsPaid)
    .ProjectTo<Order, OrderDto>(mapper);
```

## 接口参考

`Leistd.ObjectMapping.Core` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `IObjectMapper` | 对象映射器统一接口 |
| `IObjectMapper.Map<TSource, TDestination>(source)` | 映射并创建新的目标实例 |
| `IObjectMapper.Map<TSource, TDestination>(source, contextItems)` | 带上下文数据（`IDictionary<string, object>`）的映射，供自定义解析器读取 |
| `IObjectMapper.Map<TSource, TDestination>(source, destination)` | 映射到现有目标实例（就地更新），返回该实例 |
| `ObjectMapperExtensions.MapList<TSource, TDestination>(sources)` | 扩展方法，批量映射为 `List<TDestination>`；`mapper`/`sources` 为 null 抛 `ArgumentNullException` |

`Leistd.ObjectMapping.AutoMapper.Extensions` 命名空间（AutoMapper 专有）：

| 成员 | 说明 |
| --- | --- |
| `GetAutoMapper(this IObjectMapper)` | 取出底层 AutoMapper `IMapper`；实现非 `AutoMapperObjectMapper` 时抛 `InvalidOperationException` |
| `ProjectTo<TSource, TDestination>(this IQueryable, mapper)` | LINQ 查询投影，将映射下推到 `IQueryable` 提供方（如 EF Core）；参数为 null 抛 `ArgumentNullException` |

## 实现行为

### Leistd.ObjectMapping.AutoMapper

- 以 Singleton 注册 `IMapper`：用 `MapperConfiguration` 构建，`ConstructServicesUsing(sp.GetService)` 使自定义解析器/转换器可从 DI 解析。
- `AutoMapperOptions.Configurators` 中的每个委托接收 `IMapperConfigurationExpression`，用于 `AddProfile`、`CreateMap` 等。
- `ValidateMappings = true` 时在构建阶段调用 `AssertConfigurationIsValid()` 校验所有映射，配置不完整即抛异常并记录日志。
- `AutoMapperObjectMapper.GetMapper()` 暴露底层 `IMapper`，是 `ProjectTo` / `GetAutoMapper` 的基础。

> 注：csproj 注释说明 AutoMapper 自 15.0 起需配置 license，故此处刻意不升级到收费版本。

### Leistd.ObjectMapping.Mapster

- 以 Singleton 注册 `IMapper`（`new Mapper(config)`）：基础配置 `config.Default.PreserveReference(true)` 启用循环引用保护。
- `MapsterOptions.Configurators` 中的每个委托接收 `TypeAdapterConfig`；`ValidateMappings = true` 时在启动阶段调用 `config.Compile()` 提前编译并校验。
- 提供 `MapsterProfile` 抽象基类（类似 AutoMapper 的 `Profile`）：子类重写 `ConfigureMappings()`，用 `CreateMap<TSource, TDestination>()` 声明映射。
- `MapsterOptions.AddProfiles(params Assembly[])` 扫描程序集中所有非抽象的 `MapsterProfile` 子类并自动注册。
- 带上下文的 `Map(source, contextItems)` 通过 `MapContextScope` 将上下文写入 `MapContext.Current.Parameters`。
- 额外提供 `MapsterObjectMapper.Map<TDestination>(object source)`（按运行时类型映射），不属于 `IObjectMapper` 接口，需引用具体类型才能调用。

## 配置项 / Options

### AutoMapperOptions

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `Configurators` | 空列表 | `List<Action<IMapperConfigurationExpression>>`，注册映射配置委托 |
| `ValidateMappings` | `false` | 启动时是否调用 `AssertConfigurationIsValid()` 校验 |

### MapsterOptions

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `Configurators` | 空列表 | `List<Action<TypeAdapterConfig>>`，注册映射配置委托 |
| `ValidateMappings` | `false` | 启动时是否调用 `Compile()` 编译校验 |

## 注意事项

- 业务代码应只依赖 `Leistd.ObjectMapping.Core` 的 `IObjectMapper`，便于在两种实现间切换。
- `ProjectTo` / `GetAutoMapper` 是 AutoMapper 实现专有能力；若当前注册的是 Mapster，调用 `GetAutoMapper` 会抛 `InvalidOperationException`。
- `ValidateMappings` 默认关闭；建议在开发/测试环境开启，尽早暴露未配置的映射，避免运行时才发现缺字段。
- AutoMapper 刻意停留在 15.0 之前版本以规避商业 license 要求（见 csproj 注释）。

## 相关

- [组件总览](./README.md)
- [依赖注入](./dependency-injection.md)
