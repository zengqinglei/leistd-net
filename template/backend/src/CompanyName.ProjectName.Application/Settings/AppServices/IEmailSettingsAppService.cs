#if (LocalIdentity)
using CompanyName.ProjectName.Application.Settings.Dtos;
using Leistd.Ddd.Application.Contracts.AppServices;

namespace CompanyName.ProjectName.Application.Settings.AppServices;

/// <summary>发信设置的业务能力：用当前生效的发信参数发一封测试邮件。</summary>
/// <remarks>设置本身的读写由设置组件提供（<c>MapSettings</c>）；这里只放组件不认识的业务动作。</remarks>
public interface IEmailSettingsAppService : IAppService
{
    /// <summary>用当前生效的发信参数发一封测试邮件（只在宿主上下文、需要设置管理权限）。</summary>
    /// <remarks>
    /// 失败时以 422 返回原因：连不上服务器 <c>AppSetting:TestEmailUnreachable</c>、认证被拒 <c>AppSetting:TestEmailAuthRejected</c>、
    /// 投递被拒 <c>AppSetting:TestEmailRejected</c>，归不了类的 <c>AppSetting:TestEmailFailed</c>；管理员据此改参数，而不是去翻服务端日志。
    /// </remarks>
    Task SendTestEmailAsync(SendTestEmailInputDto input, CancellationToken cancellationToken = default);
}
#endif
