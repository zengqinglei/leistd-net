namespace Leistd.Redaction;

/// <summary>
/// 把敏感值换成可安全输出的形态。
/// </summary>
/// <remarks>
/// <para><b>两类场景都用它</b>：写日志（值一旦进日志就留在集中采集的存储里，保留更久、
/// 可见范围更大，还会被前端错误上报之类的旁路带走），以及对外展示（只让人认出"是我那个"，
/// 不把完整值摆出来）。</para>
/// <para><b>要不要脱敏、对谁脱敏由调用方决定。</b>同一个字段给本人看可能要真值（他要核对自己的
/// 联系方式），给运维看只要够聚合，给第三方看可能一点都不能给。本类只提供形态。</para>
/// <para><b>只提供形态，不维护数据类型目录。</b>邮箱有专门方法是因为它的 <c>'@'</c> 语义固定、
/// 而框架自己也处理邮箱（邮件组件把收件人写进投递日志）；手机号、证件号、卡号这类"保留几位"
/// 属业务判断，用 <see cref="RedactPartially"/> 传自己的参数，不在这里各加一个方法——
/// 那会让通用组件跟着业务长。</para>
/// <para><b>掩码固定为三个星号，不按长度补。</b>定长掩码（如 PCI 示例里的
/// <c>1234 56XX XXXX 1121</c>）会暴露原值长度；卡号长度本来公开所以无妨，
/// 通用形态上不这么做。确需定长时自己写。</para>
/// </remarks>
public static class TextRedactor
{
    private const string Mask = "***";

    // 本地部最多留几位：再多对识别的帮助有限，暴露面却线性上升
    private const int MaxKeptLocalChars = 3;

    /// <summary>
    /// 邮箱脱敏：保留本地部开头的几位与完整域名，例如 <c>al***@example.com</c>。
    /// </summary>
    /// <param name="address">邮箱地址；可为 <c>null</c>。</param>
    /// <returns>脱敏后的地址；<paramref name="address"/> 为空时返回空串。</returns>
    /// <remarks>
    /// <para>邮箱没有像卡号（PCI DSS 规定显示时最多前 6 后 4）那样的强制标准。域名是排障刚需——
    /// 按域名聚合才能看出某个租户或某个邮件服务商整体收不到；本地部留几位是<b>可读性与暴露面的权衡</b>：
    /// 只留一位常常认不出是谁（工单里一堆 <c>a***@</c>），留太多等于没脱敏。</para>
    /// <para>规则：从第一个<b>字母或数字</b>起最多留 3 位，<b>且不超过可用长度的一半</b>。
    /// 半数上限是硬的——只按固定位数留，<c>alice</c> 这类五位本地部会被交出去大半。
    /// 取第一个字母或数字而不是第一个字符，是因为带引号的本地部
    /// （<c>"odd@local"@example.com</c> 是合法地址）取首字符只会留下一个引号，认不出是谁，
    /// 白担一个字符的暴露面。</para>
    /// <para>按<b>最后一个</b> <c>'@'</c> 切分：本地部允许用引号包住 <c>'@'</c>，
    /// 按第一个切会把域名截错。拿不到域名（值配错了、或压根不是地址）时按整体当本地部处理，
    /// <b>不原样回落</b>——回落会让"配错的地址"成为唯一泄露原文的路径，
    /// 而那恰好是最容易被翻到的一类日志：地址配错时操作失败，排障的人正好在看它。</para>
    /// </remarks>
    public static string RedactEmail(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return string.Empty;
        }

        var value = address.AsSpan();
        var at = value.LastIndexOf('@');

        // at == 0 也算有域名：@example.com 没有本地部，但域名照样有排障价值
        var hasDomain = at >= 0 && at < value.Length - 1;
        var local = hasDomain ? value[..at] : (at == 0 ? [] : value);
        var domain = hasDomain ? value[at..] : default;

        return string.Concat(KeptLocalChars(local), Mask, domain);
    }

    /// <summary>
    /// 部分脱敏：保留开头与结尾各若干字符，中间换成 <c>***</c>，例如 <c>158***90</c>。
    /// </summary>
    /// <param name="value">要脱敏的值；可为 <c>null</c>。</param>
    /// <param name="keepStart">开头保留的字符数，不能为负。</param>
    /// <param name="keepEnd">结尾保留的字符数，不能为负。</param>
    /// <returns>脱敏后的值；<paramref name="value"/> 为空时返回空串。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="keepStart"/> 或 <paramref name="keepEnd"/> 为负。</exception>
    /// <remarks>
    /// <b>保留几位由调用方决定</b>，因为那是业务判断：手机号常保前几位与末两位以便和工单对齐，
    /// 卡号按 PCI DSS 最多前 6 后 4，证件号往下往往没有可保留的排障价值。
    /// <para><b>短值一律整体掩掉。</b>长度不足以同时保留两端时返回 <c>***</c>，而不是把原值放过去：
    /// 否则参数配得稍宽一点，短值就会整条泄露，而且不报错。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// TextRedactor.RedactPartially(phone, keepStart: 3, keepEnd: 2);   // 158***90
    /// TextRedactor.RedactPartially(pan, keepStart: 6, keepEnd: 4);     // 123456***1121
    /// </code>
    /// </example>
    public static string RedactPartially(string? value, int keepStart, int keepEnd)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepStart);
        ArgumentOutOfRangeException.ThrowIfNegative(keepEnd);

        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= keepStart + keepEnd
            ? Mask
            : string.Concat(value.AsSpan(0, keepStart), Mask, value.AsSpan(value.Length - keepEnd));
    }

    // 本地部保留的部分：从第一个字母或数字起，最多留 MaxKeptLocalChars 位，
    // 且永不超过可用长度的一半——管理员要认得出是谁，但短本地部不能因此几乎全暴露。
    // 半数这条上限是硬的：只按固定位数留，alice 这类五位本地部会被交出去大半。
    private static ReadOnlySpan<char> KeptLocalChars(ReadOnlySpan<char> local)
    {
        var start = -1;
        for (var i = 0; i < local.Length; i++)
        {
            if (char.IsLetterOrDigit(local[i]))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return default;
        }

        var available = local.Length - start;
        var kept = Math.Min(MaxKeptLocalChars, available / 2);
        return kept == 0 ? default : local.Slice(start, kept);
    }
}
