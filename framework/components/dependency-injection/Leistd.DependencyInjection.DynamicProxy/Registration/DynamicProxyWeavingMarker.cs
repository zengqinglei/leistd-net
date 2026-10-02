namespace Leistd.DependencyInjection.DynamicProxy.Registration;

/// <summary>
/// 表示宿主已接入 <see cref="DynamicProxyServiceRegistrationCallbackFactory"/>，拦截器会被织入。
/// </summary>
/// <remarks>
/// 漏接工厂时注册回调与校验都不执行，依赖织入的组件照常启动却静默失效；
/// 这类组件在启动时解析本标记，缺失即失败。
/// </remarks>
public sealed class DynamicProxyWeavingMarker;
