using System.Security.Claims;
using Leistd.Disposables;

namespace Leistd.Security.Claims;

/// <summary>
/// 支持异步流覆盖的认证主体访问器。
/// </summary>
/// <remarks>
/// 直接使用即"只认显式建立的主体"：适用于没有认证中间件的入口（后台作业、消息消费者、
/// Hub 调用），未经 <see cref="Change"/> 或 <c>IAmbientContext.Begin</c> 建立时
/// <see cref="Principal"/> 为 <see langword="null"/>。
/// Web 宿主由 <c>AddSecurity()</c> 换成读 <c>HttpContext.User</c> 的派生实现。
/// </remarks>
public class CurrentPrincipalAccessor : ICurrentPrincipalAccessor
{
    private readonly AsyncLocal<ClaimsPrincipal?> _currentPrincipal = new();

    /// <inheritdoc />
    public ClaimsPrincipal? Principal
    {
        get
        {
            var principal = _currentPrincipal.Value;
            if (principal is not null)
                return principal;

            return GetClaimsPrincipal();
        }
    }

    /// <summary>
    /// 获取底层认证源中的主体；默认没有底层来源。
    /// </summary>
    /// <returns>认证主体，没有则返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 默认返回 <see langword="null"/> 而不是强制派生：绝大多数入口没有独立于
    /// <see cref="Change"/> 的主体来源，null 也是这里唯一安全的回落——猜一个主体
    /// 会让调用方读到不属于当前作用域的身份。
    /// </remarks>
    protected virtual ClaimsPrincipal? GetClaimsPrincipal() => null;

    /// <inheritdoc />
    public IDisposable Change(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var parent = _currentPrincipal.Value;
        _currentPrincipal.Value = principal;

        return new DisposeAction(() =>
        {
            _currentPrincipal.Value = parent;
        });
    }
}
