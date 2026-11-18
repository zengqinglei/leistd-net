#if (RemoteTokenAuth)
using Leistd.ServiceClient.Options;

namespace CompanyName.ProjectName.Infrastructure.Users;

/// <summary>内置管理员查询客户端，复用 Identity 地址与机器 scope 配置。</summary>
public sealed class IdentityAdministratorsClientOptions : ServiceClientOptions;
#endif
