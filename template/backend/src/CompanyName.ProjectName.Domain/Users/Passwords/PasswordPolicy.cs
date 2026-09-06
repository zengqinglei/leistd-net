#if (LocalIdentity)
using Leistd.ExceptionHandling;

namespace CompanyName.ProjectName.Domain.Users.Passwords;

/// <summary>
/// 服务端权威密码策略
/// </summary>
/// <remarks>
/// <para><b>唯一权威。</b>DTO 的 DataAnnotations 与前端表单只负责快速反馈，不承担安全不变量——
/// 直接调 API 能绕开它们，内部调用（种子、租户初始化、bootstrap）连 DTO 都不经过。
/// <b>不要在各入口另写一套规则</b>：多套规则并存时，最宽的那一套就是系统的真实下限，
/// 而"哪一套最宽"没人会持续核对。新增创建口令的入口一律调用本类。</para>
/// <para><b>选长度而不选复杂度正则。</b>复杂度规则把用户推向 <c>Passw0rd!</c> 这类可预测形态，
/// 却挡不住它；而长度直接增加搜索空间。因此这里的规则是：足够长、允许长口令、
/// 拒绝已知弱密码与项目自带的示例值。</para>
/// <para>上限存在只为防御拒绝服务（哈希开销随输入增长），不是安全要求，所以设得很宽。</para>
/// </remarks>
public static class PasswordPolicy
{
    /// <summary>最小长度</summary>
    public const int MinimumLength = 12;

    /// <summary>最大长度。仅为防御哈希开销型拒绝服务，不是安全要求</summary>
    public const int MaximumLength = 256;

    /// <summary>
    /// 曾作为默认值/示例发布过的口令，以及最常见的弱口令
    /// </summary>
    /// <remarks>
    /// 发布过的示例值必须拒绝：它们已进入公开仓库历史，等同于已泄漏。
    /// 这里刻意只列极少数——完整的泄漏口令库属于业务项目按需接入的能力
    /// （如 Have I Been Pwned 的 k-anonymity 接口），不该塞进模板。
    /// </remarks>
    public static readonly string[] Rejected =
    [
        "Admin@123456", "admin", "password", "Password1!", "P@ssw0rd", "123456789012"
    ];

    /// <summary>密码是否满足策略</summary>
    public static bool IsAcceptable(string? password) => Describe(password) is null;

    /// <summary>
    /// 校验密码，不通过则抛 <see cref="BadRequestException"/>（400）
    /// </summary>
    /// <param name="password">待校验口令</param>
    /// <param name="subject">用于错误消息的主体描述，如 <c>"DefaultAdmin:Password"</c></param>
    public static void Ensure(string? password, string subject)
    {
        var problem = Describe(password);
        if (problem is not null)
        {
            throw new BadRequestException($"{subject} {problem}");
        }
    }

    /// <summary>返回不满足策略的原因；满足则返回 <see langword="null"/></summary>
    private static string? Describe(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "is required.";
        }

        if (password.Length < MinimumLength)
        {
            return $"must be at least {MinimumLength} characters long.";
        }

        if (password.Length > MaximumLength)
        {
            return $"must be at most {MaximumLength} characters long.";
        }

        if (Rejected.Contains(password, StringComparer.OrdinalIgnoreCase))
        {
            return "is a well-known or previously published value and cannot be used.";
        }

        return null;
    }
}
#endif
