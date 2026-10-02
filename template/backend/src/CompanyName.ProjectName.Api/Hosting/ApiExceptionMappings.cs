using CompanyName.ProjectName.Api.Hosting.ExceptionMappings;
using Leistd.ExceptionHandling.Options;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>组合本项目业务模块拥有的非默认 HTTP 错误映射。</summary>
/// <remarks>
/// 只列业务模块。框架组件的默认状态由各组件在自己的 <c>AddXxx</c> 里登记，宿主不必逐个调用——
/// 需要改其中某一条时在这里用 <see cref="GlobalExceptionOptions.MapCode"/> 覆盖即可，与调用顺序无关。
/// </remarks>
internal static class ApiExceptionMappings
{
    public static void Configure(GlobalExceptionOptions options)
    {
#if (LocalIdentity)
        AuthExceptionMappings.Configure(options);
        TenantExceptionMappings.Configure(options);
#endif
#if (OpenIddictServer)
        OpenApplicationExceptionMappings.Configure(options);
#endif
        UserExceptionMappings.Configure(options);
        RoleExceptionMappings.Configure(options);
        AppSettingExceptionMappings.Configure(options);
    }

    internal static void Map(GlobalExceptionOptions options, int statusCode, params string[] codes)
    {
        foreach (var code in codes)
            options.MapCode(code, statusCode);
    }
}
