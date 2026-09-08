using Microsoft.Extensions.DependencyInjection;

namespace Leistd.DependencyInjection.Registration;

/// <summary>
/// 保存服务集合校验器。
/// </summary>
/// <remarks>
/// 校验器先于描述符回调执行，并可检查完整 <see cref="IServiceCollection"/>。
/// </remarks>
public class ServiceRegistrationValidatorList : List<Action<IServiceCollection>>
{
}
