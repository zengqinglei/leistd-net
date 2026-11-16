#if (TenantWorkspace && RemoteTokenAuth)
using Leistd.ServiceClient.Options;

namespace CompanyName.ProjectName.Infrastructure.Workspaces;

/// <summary>沿用 Identity 配置节；每条命名管道使用独立选项类型。</summary>
public sealed class IdentityWorkspacesClientOptions : ServiceClientOptions;
#endif
