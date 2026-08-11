namespace Leistd.ServiceClient.Handlers;

/// <summary>
/// 直通处理器：宿主未注册某项可选能力（链路追踪、当前用户）时占位，保持管道结构稳定。
/// </summary>
internal sealed class PassthroughDelegatingHandler : DelegatingHandler;
