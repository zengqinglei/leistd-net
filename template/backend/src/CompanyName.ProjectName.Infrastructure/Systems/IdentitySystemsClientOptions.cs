#if (RemoteTokenAuth)
using Leistd.ServiceClient.Options;

namespace CompanyName.ProjectName.Infrastructure.Systems;

/// <summary>系统目录命名客户端，复用 Identity 地址与机器 scope 配置。</summary>
public sealed class IdentitySystemsClientOptions : ServiceClientOptions;
#endif
