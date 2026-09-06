using Mapster;

namespace Leistd.ObjectMapping.Mapster.Mapping;

/// <summary>
/// 提供 Mapster 映射配置基类。
/// </summary>
public abstract class MapsterProfile
{
    /// <summary>映射配置容器，由框架在调用 <c>Configure</c> 前注入。</summary>
    protected TypeAdapterConfig Config { get; private set; } = null!;

    /// <summary>
    /// 使用指定配置注册映射。
    /// </summary>
    public void Configure(TypeAdapterConfig config)
    {
        Config = config;
        ConfigureMappings();
    }

    /// <summary>
    /// 配置映射规则（子类重写此方法）
    /// </summary>
    protected abstract void ConfigureMappings();

    /// <summary>
    /// 创建类型映射配置。
    /// </summary>
    protected TypeAdapterSetter<TSource, TDestination> CreateMap<TSource, TDestination>()
    {
        return Config.NewConfig<TSource, TDestination>();
    }
}
