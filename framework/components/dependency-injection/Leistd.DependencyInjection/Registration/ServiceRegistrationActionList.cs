using Leistd.DependencyInjection.Abstractions;

namespace Leistd.DependencyInjection.Registration;

/// <summary>
/// 保存服务描述符回调。
/// </summary>
public class ServiceRegistrationActionList : List<Action<IOnServiceRegisteredContext>>
{
}
