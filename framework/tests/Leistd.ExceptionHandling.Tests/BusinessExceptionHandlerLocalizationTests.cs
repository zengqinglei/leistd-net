using Leistd.ExceptionHandling.AspNetCore.Handlers;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Leistd.ExceptionHandling.Tests;

/// <summary>
/// 错误码兼词条键的解析链：按 <c>Code</c> 查词条 → 状态码通用码 → 兜底。
/// 通过 <see cref="BusinessExceptionHandler.TryHandleAsync"/> 端到端驱动，用捕获式 IProblemDetailsService 拿到最终 ProblemDetails。
/// </summary>
public class BusinessExceptionHandlerLocalizationTests
{
    [Fact]
    public async Task Uses_code_as_the_resource_key_and_fills_named_placeholders()
    {
        // 资源里配了该错误码；Message 是英文诊断串，不应出现在响应里
        var localizer = new StubLocalizer(new Dictionary<string, string>
        {
            ["User:EmailAlreadyUsed"] = "邮箱 '{Email}' 已被占用",
        });

        var ex = new BadRequestException("Email already in use")
            .WithCode("User:EmailAlreadyUsed")
            .WithData("Email", "a@b.com");

        var pd = await HandleAsync(ex, localizer);

        Assert.Equal("邮箱 'a@b.com' 已被占用", pd.Detail);
        Assert.Equal("User:EmailAlreadyUsed", pd.Extensions["code"]);
    }

    [Fact]
    public async Task Default_code_is_emitted_and_resolves_to_the_generic_text()
    {
        // 不显式设码：Code 是状态码通用码，响应里恒有 code，文案取通用词条
        var localizer = new StubLocalizer(new Dictionary<string, string>
        {
            ["Error:NotFound"] = "资源不存在",
        });

        var pd = await HandleAsync(new NotFoundException("User 42 not found"), localizer);

        Assert.Equal("资源不存在", pd.Detail);
        Assert.Equal("Error:NotFound", pd.Extensions["code"]);
    }

    [Fact]
    public async Task Falls_back_to_the_generic_code_when_an_explicit_code_is_missing_from_resources()
    {
        // 显式设了码但资源里漏配 → 回落状态码通用码，而不是把诊断消息甩给用户。
        // 这一条钉住解析链第二级不可删
        var localizer = new StubLocalizer(new Dictionary<string, string>
        {
            ["Error:NotFound"] = "资源不存在",
        });

        var ex = new NotFoundException("User 42 not found").WithCode("User:NotThere");

        var pd = await HandleAsync(ex, localizer);

        Assert.Equal("资源不存在", pd.Detail);
        Assert.Equal("User:NotThere", pd.Extensions["code"]);
    }

    [Fact]
    public async Task Falls_back_to_the_status_title_rather_than_the_diagnostic_message()
    {
        // 默认（FallbackToExceptionMessage = false）：什么都查不到时用状态码短语，
        // 不把面向运维的英文诊断消息暴露给终端用户
        var localizer = new StubLocalizer(new Dictionary<string, string>());

        var ex = new BadRequestException("stack overflow in tenant resolver").WithCode("Foo:Bar");

        var pd = await HandleAsync(ex, localizer);

        Assert.Equal("Bad Request", pd.Detail);
        Assert.DoesNotContain("tenant resolver", pd.Detail);
    }

    [Fact]
    public async Task Emits_the_message_when_explicitly_marked_user_facing()
    {
        var localizer = new StubLocalizer(new Dictionary<string, string>());

        var ex = new BadRequestException("Please pick a date in the future.").AsUserFacing();

        var pd = await HandleAsync(ex, localizer);

        Assert.Equal("Please pick a date in the future.", pd.Detail);
    }

    [Fact]
    public async Task Emits_the_message_when_the_host_opts_into_the_fallback()
    {
        var localizer = new StubLocalizer(new Dictionary<string, string>());

        var ex = new BadRequestException("something went wrong").WithCode("Foo:Bar");

        var pd = await HandleAsync(ex, localizer, fallbackToExceptionMessage: true);

        Assert.Equal("something went wrong", pd.Detail);
    }

    [Fact]
    public async Task Keeps_a_safe_text_and_the_code_when_the_localizer_throws()
    {
        // 本地化组件本身抛异常（坏资源等）绝不能覆盖原始业务异常；默认不泄露诊断消息
        var localizer = new ThrowingLocalizer();

        var ex = new BadRequestException("diagnostic fallback").WithCode("User:EmailAlreadyUsed");

        var pd = await HandleAsync(ex, localizer);

        Assert.Equal("Bad Request", pd.Detail);
        Assert.Equal("User:EmailAlreadyUsed", pd.Extensions["code"]);
    }

    [Fact]
    public async Task Without_a_localizer_uses_the_status_title_by_default()
    {
        // 未启用本地化（未注册 IStringLocalizer）——模板裁掉本地化的场景走这条路
        var ex = new BadRequestException("plain diagnostic message").WithCode("User:EmailAlreadyUsed");

        var pd = await HandleAsync(ex, localizer: null);

        Assert.Equal("Bad Request", pd.Detail);
    }

    [Fact]
    public async Task Unsupported_media_type_gets_its_own_generic_code()
    {
        // 415 此前不在通用码映射里，会落到 Error:InternalServer——那会让 415 响应带上
        // 一个明显错误的对外错误码
        var localizer = new StubLocalizer(new Dictionary<string, string>
        {
            ["Error:UnsupportedMediaType"] = "不支持的请求媒体类型。",
        });

        var pd = await HandleAsync(new UnsupportedMediaTypeException("bad content type"), localizer);

        Assert.Equal("Error:UnsupportedMediaType", pd.Extensions["code"]);
        Assert.Equal("不支持的请求媒体类型。", pd.Detail);
        Assert.Equal("Unsupported Media Type", pd.Title);
    }


    private static async Task<ProblemDetails> HandleAsync(
        BusinessException ex,
        IStringLocalizer? localizer,
        bool fallbackToExceptionMessage = false)
    {
        var services = new ServiceCollection();
        if (localizer is not null)
            services.AddSingleton(localizer);
        var provider = services.BuildServiceProvider();

        var capture = new CapturingProblemDetailsService();
        var handler = new BusinessExceptionHandler(
            new MutableOptionsMonitor<GlobalExceptionOptions>(new GlobalExceptionOptions
            {
                Enabled = true,
                FallbackToExceptionMessage = fallbackToExceptionMessage
            }),
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
