using System.Security.Cryptography;
using System.Text;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Users.Options;
using Leistd.Ddd.Application.AppService;
using Leistd.Lock.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

public class CaptchaAppService(
    IDistributedCache distributedCache,
    IDistributedLock distributedLock,
    IOptions<UserRegistrationOptions> options) : BaseAppService, ICaptchaAppService
{
    private const string CaptchaLetters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    private const string CaptchaDigits = "23456789";
    private const string CaptchaCharacters = CaptchaLetters + CaptchaDigits;
    private readonly UserRegistrationOptions _options = options.Value;
    private static readonly string[] BgColors = ["#f0fdf4", "#f8fafc", "#fffbeb", "#fef2f2", "#f0f9ff"];

    public async Task<CaptchaOutputDto> GenerateCaptchaAsync(CancellationToken cancellationToken = default)
    {
        var code = GenerateCode(4);
        var token = Guid.NewGuid().ToString("N");

        var bg = BgColors[RandomNumberGenerator.GetInt32(BgColors.Length)];
        var lineY = RandomNumberGenerator.GetInt32(5, 35);
        var angle = RandomNumberGenerator.GetInt32(-15, 15);

        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"130\" height=\"44\"><rect width=\"100%\" height=\"100%\" fill=\"{bg}\"/><line x1=\"0\" y1=\"{lineY}\" x2=\"130\" y2=\"{44 - lineY}\" stroke=\"#94a3b8\" stroke-width=\"2\" opacity=\"0.6\"/><text x=\"50%\" y=\"50%\" font-size=\"24\" font-family=\"monospace\" fill=\"#0f172a\" font-weight=\"bold\" font-style=\"italic\" textLength=\"88\" lengthAdjust=\"spacingAndGlyphs\" dominant-baseline=\"central\" text-anchor=\"middle\" transform=\"rotate({angle}, 65, 22)\">{code}</text></svg>";

        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        var imageBase64 = $"data:image/svg+xml;base64,{base64}";

        // 3. 保存到缓存
        var cacheKey = GetCacheKey(token);
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CaptchaExpiryMinutes)
        };
        await distributedCache.SetStringAsync(cacheKey, code, cacheOptions, cancellationToken);

        return new CaptchaOutputDto
        {
            CaptchaToken = token,
            CaptchaImageBase64 = imageBase64
        };
    }

    /// <remarks>
    /// 读取、消费与比较必须在同一个临界区里。<c>IDistributedCache</c> 没有原子的
    /// get-and-delete，"读出来再删掉"之间存在窗口：同一个 token 并发提交时两个请求
    /// 都会读到验证码、都判定通过，于是"一次性挑战"只是名义上的一次——
    /// 解一次验证码就能并发提交任意多次注册，正是验证码要挡的那件事。
    /// 锁形态与 <see cref="EmailVerificationAppService"/> 一致：模板已装分布式锁，
    /// 不为此另建验证码专用存储或 Redis 脚本。
    /// </remarks>
    public async Task<bool> ValidateCaptchaAsync(string token, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(code))
            return false;

        var cacheKey = GetCacheKey(token);
        await using var captchaLock = await distributedLock.LockAsync(GetLockKey(token), cancellationToken);
        using var lockScope = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            captchaLock.LockLost);
        var operationToken = lockScope.Token;

        var cachedCode = await distributedCache.GetStringAsync(cacheKey, operationToken);

        if (string.IsNullOrEmpty(cachedCode))
            return false;

        // 验证码只能使用一次：无论比对结果如何，token 存在就消费掉——
        // 猜错一次即失效，否则同一个 token 可以被反复试。
        await distributedCache.RemoveAsync(cacheKey, operationToken);

        return cachedCode.Equals(code, StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateCode(int length)
    {
        var chars = new char[length];
        chars[0] = CaptchaLetters[RandomNumberGenerator.GetInt32(CaptchaLetters.Length)];
        chars[1] = CaptchaDigits[RandomNumberGenerator.GetInt32(CaptchaDigits.Length)];

        for (var i = 2; i < chars.Length; i++)
        {
            chars[i] = CaptchaCharacters[RandomNumberGenerator.GetInt32(CaptchaCharacters.Length)];
        }

        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static string GetCacheKey(string token) => $"MyProject:Captcha:{token}";

    private static string GetLockKey(string token) => $"MyProject:Captcha:lock:{token}";
}
