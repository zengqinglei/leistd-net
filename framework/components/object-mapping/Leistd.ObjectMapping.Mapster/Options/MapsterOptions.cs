using Mapster;

namespace Leistd.ObjectMapping.Mapster.Options;

/// <summary>
/// 配置 Mapster 对象映射。
/// </summary>
public class MapsterOptions
{
    /// <summary>
    /// 获取映射配置操作列表。
    /// </summary>
    public List<Action<TypeAdapterConfig>> Configurators { get; } = [];

    /// <summary>
    /// 获取或设置是否验证映射配置。
    /// </summary>
    public bool ValidateMappings { get; set; }
}
