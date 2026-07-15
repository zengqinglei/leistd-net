using Leistd.Exception.AspNetCore.Handlers;
using Leistd.Exception.AspNetCore.Options;
using Leistd.Exception.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Exception.Tests;

/// <summary>
/// 处理器本地化健壮化验证：三分离键优先、状态码语义键兜底、以及本地化组件异常时保留原始 Message（P3/P4/P5#6）。
/// 通过 <see cref="BusinessExceptionHandler.TryHandleAsync"/> 端到端驱动，用捕获式 IProblemDetailsService 拿到最终 ProblemDetails。
/// </summary>
public class BusinessExceptionHandlerLocalizationTests
{
    [Fact]
    public async Task Uses_LocalizationKey_over_Message_and_fills_named_placeholders()
    {
        // 资源里配了展示键；Message 是英文诊断串，不应出现在响应里
        var localizer = new StubLocalizer(new Dictionary<string, string>
        {
            ["User:EmailAlreadyUsed"] = "邮箱 '{Email}' 已被占用",
        });

        var ex = new BadRequestException("Email already in use")
            .WithCode("02")
            .WithLocalization("User:EmailAlreadyUsed")
            .WithData("Email", "a@b.com");

        var pd = await HandleAsync(ex, localizer);

        Assert.Equal("邮箱 'a@b.com' 已被占用", pd.Detail);
        Assert.Equal(40002, pd.Extensions["code"]);
    }

    [Fact]
    public async Task Falls_back_to_status_semantic_key_when_LocalizationKey_missing_in_resources()
    {
        // 展示键在资源里查不到（漏配）→ 回落到 HTTP 状态码的通用语义键 Error:NotFound，绝不漏裸键
        var localizer = new StubLocalizer(new Dictionary<string, string>
        {
            ["Error:NotFound"] = "资源不存在",
        });

        var ex = new NotFoundException("User 42 not found").WithLocalization("User:NotThere");

        var pd = await HandleAsync(ex, localizer);

        Assert.Equal("资源不存在", pd.Detail);
    }

    [Fact]
    public async Task Falls_back_to_Message_when_neither_key_nor_generic_key_resolves()
    {
        // 键与通用键都查不到 → 回落诊断 Message，仍不漏裸键
        var localizer = new StubLocalizer(new Dictionary<string, string>());

        var ex = new BadRequestException("something went wrong").WithLocalization("Foo:Bar");

        var pd = await HandleAsync(ex, localizer);

        Assert.Equal("something went wrong", pd.Detail);
    }

    [Fact]
    public async Task Preserves_Message_when_localizer_throws()
    {
        // 本地化组件本身抛异常（坏资源等）绝不能覆盖原始业务异常 → 回落 Message，code 保持不变
        var localizer = new ThrowingLocalizer();

        var ex = new BadRequestException("diagnostic fallback").WithCode("02").WithLocalization("User:EmailAlreadyUsed");

        var pd = await HandleAsync(ex, localizer);

        Assert.Equal("diagnostic fallback", pd.Detail);
        Assert.Equal(40002, pd.Extensions["code"]);
    }

    [Fact]
    public async Task Without_localizer_returns_raw_message_unchanged()
    {
        // 未启用本地化（未注册 IStringLocalizer）时行为 = 现状：直出 Message
        var ex = new BadRequestException("plain message").WithLocalization("User:EmailAlreadyUsed");

        var pd = await HandleAsync(ex, localizer: null);

        Assert.Equal("plain message", pd.Detail);
    }

    // ---- harness ----

    private static async Task<ProblemDetails> HandleAsync(BusinessException ex, IStringLocalizer? localizer)
    {
        var services = new ServiceCollection();
        if (localizer is not null)
            services.AddSingleton(localizer);
        var provider = services.BuildServiceProvider();

        var capture = new CapturingProblemDetailsService();
        var handler = new BusinessExceptionHandler(
            Options.Create(new GlobalExceptionOptions { Enable = true }),
            new StubHostEnvironment(),
            NullLogger<BusinessExceptionHandler>.Instance,
            capture,
            provider);

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var handled = await handler.TryHandleAsync(httpContext, ex, CancellationToken.None);

        Assert.True(handled);
        Assert.NotNull(capture.Captured);
        return capture.Captured!;
    }

    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetails? Captured { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Captured = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Captured = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = ".";
        public string EnvironmentName { get; set; } = "Production";
    }

    private sealed class StubLocalizer(IReadOnlyDictionary<string, string> map) : IStringLocalizer
    {
        public LocalizedString this[string name] =>
            map.TryGetValue(name, out var value)
                ? new LocalizedString(name, value, resourceNotFound: false)
                : new LocalizedString(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            map.Select(kv => new LocalizedString(kv.Key, kv.Value, resourceNotFound: false));
    }

    private sealed class ThrowingLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => throw new InvalidOperationException("resource load failed");
        public LocalizedString this[string name, params object[] arguments] => throw new InvalidOperationException("resource load failed");
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => throw new InvalidOperationException();
    }
}
