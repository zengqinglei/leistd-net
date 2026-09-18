namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 断言错误码时用：细分错误码同时是本地化词条键，只在含本地化的形态下随响应下发；
/// 不含本地化时响应里是按状态码的通用码（如 <c>Error:Unauthorized</c>）。
/// </summary>
/// <remarks>
/// 界面要据以分支的错误码（如 <c>Auth:TwoFactorSetupRequired</c>）在所有形态下都下发，不经这里。
/// 用例里真正要守的行为（锁定期间正确密码也被拒、解锁后能登录）不靠错误码，两种形态下都照样断言。
/// </remarks>
internal static class ExpectedErrorCode
{
    /// <summary>本形态下应当收到的错误码。</summary>
    /// <param name="code">细分错误码。</param>
    /// <param name="generic">不含本地化时的通用码。</param>
    public static string Of(string code, string generic)
    {
#if (IncludeLocalization)
        _ = generic;
        return code;
#else
        _ = code;
        return generic;
#endif
    }
}
