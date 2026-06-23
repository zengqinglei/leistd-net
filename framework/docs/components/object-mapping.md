# 对象映射（`object-mapping`）
> 统一的对象映射抽象 `IObjectMapper`，可在 AutoMapper 与 Mapster 两种实现间切换。

## 包

| 包 | 角色 | 何时引用 |
| --- | --- | --- |
| `Leistd.ObjectMapping.Core` | 映射抽象与扩展，定义 `IObjectMapper` | 业务代码只依赖抽象时引用 |
| `Leistd.ObjectMapping.AutoMapper` | 基于 AutoMapper 的实现 | 需要 AutoMapper 及其 `ProjectTo` 投影时引用 |
| `Leistd.ObjectMapping.Mapster` | 基于 Mapster 的实现 | 需要 Mapster（编译期生成、`Profile` 风格配置）时引用 |

## 核心抽象

`IObjectMapper`（命名空间 `Leistd.ObjectMapping.Core`）——对象映射器接口。

```csharp
TDestination Map<TSource, TDestination>(TSource source);
```
将 `source` 映射为新建的 `TDestination` 实例并返回。

```csharp
TDestination Map<TSource, TDestination>(TSource source, IDictionary<string, object> contextItems);
```
带上下文数据的映射，`contextItems` 作为映射过程中的额外参数传入。

```csharp
TDestination Map<TSource, TDestination>(TSource source, TDestination destination);
```
将 `source` 映射到已存在的 `destination` 实例（就地更新）并返回。

`ObjectMapperExtensions`（命名空间 `Leistd.ObjectMapping.Core.Extensions`）——`IObjectMapper` 扩展方法。

```csharp
List<TDestination> MapList<TSource, TDestination>(this IObjectMapper mapper, IEnumerable<TSource> sources);
```
批量映射，逐个调用 `Map` 并收集为 `List`；`mapper` 或 `sources` 为 null 时抛 `ArgumentNullException`。

## 能力实现

### `Leistd.ObjectMapping.AutoMapper`

- 注册：`IServiceCollection.AddAutoMapperObjectMapper(Action<AutoMapperOptions>? configure = null)`（命名空间 `Leistd.ObjectMapping.AutoMapper`）。
- `IMapper` 与 `IObjectMapper`（实现类 `AutoMapperObjectMapper`）均注册为 **Singleton**；`MapperConfiguration` 通过 `ConstructServicesUsing(sp.GetService)` 接入 DI 容器构造映射依赖。
- 配置选项 `AutoMapperOptions`：`Configurators`（`List<Action<IMapperConfigurationExpression>>`，添加 AutoMapper 配置委托）；`ValidateMappings`（默认 `false`，为 `true` 时启动期调用 `AssertConfigurationIsValid()` 校验并记录日志）。
- `contextItems` 写入 AutoMapper 的 `opt.Items`。
- 特有扩展（命名空间 `Leistd.ObjectMapping.AutoMapper.Extensions`，`AutoMapperExtensions`）：
  - `IObjectMapper.GetAutoMapper()` 取回底层 `IMapper`；若当前实现不是 `AutoMapperObjectMapper` 抛 `InvalidOperationException`。
  - `IQueryable<TSource>.ProjectTo<TSource, TDestination>(IObjectMapper)` 将映射下推为数据库查询（基于 AutoMapper `ProjectTo`）。

### `Leistd.ObjectMapping.Mapster`

- 注册：`IServiceCollection.AddMapsterObjectMapper(Action<MapsterOptions>? configure = null)`（命名空间 `Leistd.ObjectMapping.Mapster`）。
- `IMapper`（MapsterMapper）与 `IObjectMapper`（实现类 `MapsterObjectMapper`）均注册为 **Singleton**；`TypeAdapterConfig` 默认开启 `PreserveReference(true)`（保留引用，处理循环引用）。
- 配置选项 `MapsterOptions`：`Configurators`（`List<Action<TypeAdapterConfig>>`）；`ValidateMappings`（默认 `false`，为 `true` 时启动期调用 `config.Compile()` 预编译校验并记录日志）。
- `contextItems` 通过 `MapContextScope` + `MapContext.Current.Parameters` 传入。
- `MapsterProfile`（抽象基类，类似 AutoMapper 的 `Profile`）：重写 `ConfigureMappings()`，在其中用 `CreateMap<TSource, TDestination>()` 声明映射。
- `MapsterOptions.AddProfiles(params Assembly[] assemblies)` 扫描程序集中所有 `MapsterProfile` 子类并自动注册。

## 最小可用示例

```csharp
using Leistd.ObjectMapping.Core;
using Leistd.ObjectMapping.Mapster;
using Microsoft.Extensions.DependencyInjection;

// 1. 定义映射 Profile
public class UserProfile : MapsterProfile
{
    protected override void ConfigureMappings()
    {
        CreateMap<User, UserDto>();
    }
}

// 2. 注册（也可换成 AddAutoMapperObjectMapper）
var services = new ServiceCollection();
services.AddLogging();
services.AddMapsterObjectMapper(options =>
{
    options.AddProfiles(typeof(UserProfile).Assembly);
    options.ValidateMappings = true;
});
var provider = services.BuildServiceProvider();

// 3. 使用
var mapper = provider.GetRequiredService<IObjectMapper>();
var dto = mapper.Map<User, UserDto>(user);
```

## 依赖

无（仅依赖第三方映射库 AutoMapper / Mapster 与 Microsoft.Extensions.* 基础包，不依赖其它 Leistd 组件）。

## 备注

- AutoMapper 固定使用 14.x：源码注释说明「AutoMapper 15.0 开始收费需要配置 license，这里不升级」。
- `MapsterObjectMapper` 额外提供 `Map<TDestination>(object source)`（按源对象运行时类型映射），但该方法不在 `IObjectMapper` 接口中，需直接持有实现类才能调用。
- `MapsterProfile` 暴露的 `CreateMap` 仅声明配置，不做返回值链式约定的额外封装；进一步的成员映射规则直接使用 Mapster 原生 API。
