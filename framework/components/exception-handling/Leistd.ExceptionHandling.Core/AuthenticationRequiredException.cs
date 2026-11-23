namespace Leistd.ExceptionHandling;

/// <summary>请求没有可用的认证：调用方从未登录，或者它的凭据已不再被接受，需要重新认证。</summary>
/// <remarks>
/// <para>错误码固定为 <see cref="ErrorCode"/>；全局异常处理按它答"未认证"，前端据此重新发起登录。
/// 已登录但没有权限不用本类型，改用带错误码的 <see cref="BusinessException"/> 并映射为"禁止访问"。</para>
/// <para>不复用 <see cref="UnauthorizedAccessException"/>：它在 .NET 中表示操作系统拒绝访问（如文件权限），
/// 把它当作需要登录会误导排查。</para>
/// </remarks>
public class AuthenticationRequiredException : BusinessException
{
    /// <summary>错误码兼本地化资源键。</summary>
    public const string ErrorCode = "Authentication:Required";

    /// <summary>构造需要重新认证的异常。</summary>
    /// <param name="innerException">导致需要重新认证的原因（如认证服务拒绝了用户凭据），只用于诊断。</param>
    public AuthenticationRequiredException(Exception? innerException = null)
        : base(ErrorCode, "Sign in again to continue.", innerException)
    {
    }
}
