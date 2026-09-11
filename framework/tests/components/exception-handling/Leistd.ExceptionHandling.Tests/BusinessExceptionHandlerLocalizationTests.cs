using Leistd.ExceptionHandling.AspNetCore.Handlers;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Leistd.TestBase.Doubles;

namespace Leistd.ExceptionHandling.Tests;

/// <summary>
/// 消息解析链：按 <c>Code</c> 查词条 → 策略允许则直出 <c>Message</c> → 状态码通用词条 → 状态短语。
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
    public async Task Prefers_the_specific_message_over_the_generic_entry_when_the_code_has_no_resource()
    {
        // 漏配词条时，通用词条（"资源不存在"）说不出哪里不对，抛出点的消息说得出。
        // 把通用词条排在消息之前，等于让漏配一条词条的代价变成"用户无从修正"
        var localizer = new StubLocalizer(new Dictionary<string, string>
        {
            ["Error:NotFound"] = "资源不存在",
        });

        var ex = new NotFoundException("User 42 not found").WithCode("User:NotThere");

        var pd = await HandleAsync(ex, localizer);

        Assert.Equal("User 42 not found", pd.Detail);
        Assert.Equal("User:NotThere", pd.Extensions["code"]);
    }

    [Fact]
    public async Task Does_not_expose_server_error_messages_by_default()
    {
        // 默认策略 ClientErrors 的那条线：4xx 说的是"你的输入哪里不对"，该给用户看；
        // 5xx 说的是系统内部出了什么事，只进日志。这里连通用词条都没配，
        // 挡住诊断串的就只剩策略本身
        var localizer = new StubLocalizer(new Dictionary<string, string>());

        var ex = new InternalServerException("connection string parse failed at offset 42");

        var pd = await HandleAsync(ex, localizer);

        Assert.Equal("Internal Server Error", pd.Detail);
        Assert.DoesNotContain("connection string", pd.Detail);
    }

    [Fact]
    public async Task Server_errors_still_get_the_generic_entry_when_it_is_configured()
    {
        var localizer = new StubLocalizer(new Dictionary<string, string>
        {
            ["Error:InternalServer"] = "系统内部错误。",
        });

        var pd = await HandleAsync(new InternalServerException("disk full"), localizer);

        Assert.Equal("系统内部错误。", pd.Detail);
    }

    [Fact]
    public async Task Falls_back_to_the_status_title_when_the_host_forbids_exposing_messages()
    {
        // MessageExposure.None：连 4xx 的消息也不外露，且这里连通用词条都没配
        var localizer = new StubLocalizer(new Dictionary<string, string>());

        var ex = new BadRequestException("stack overflow in tenant resolver").WithCode("Foo:Bar");

        var pd = await HandleAsync(ex, localizer, BusinessMessageExposure.None);

        Assert.Equal("Bad Request", pd.Detail);
        Assert.DoesNotContain("tenant resolver", pd.Detail);
    }

    [Fact]
    public async Task Emits_server_error_messages_when_the_host_opts_into_all()
    {
        // 内部系统才用的 All：没有词条可用时，5xx 的诊断消息也直出
        var localizer = new StubLocalizer(new Dictionary<string, string>());

        var ex = new InternalServerException("upstream returned 500");

        var pd = await HandleAsync(ex, localizer, BusinessMessageExposure.All);

        Assert.Equal("upstream returned 500", pd.Detail);
    }

    [Fact]
    public async Task Keeps_the_message_and_the_code_when_the_localizer_throws()
    {
        // 本地化组件本身抛异常（坏资源等）绝不能覆盖原始业务异常；策略照常生效
        var localizer = new ThrowingLocalizer();

        var ex = new BadRequestException("diagnostic fallback").WithCode("User:EmailAlreadyUsed");

        var pd = await HandleAsync(ex, localizer);

        Assert.Equal("diagnostic fallback", pd.Detail);
        Assert.Equal("User:EmailAlreadyUsed", pd.Extensions["code"]);
    }

    [Fact]
    public async Task Without_a_localizer_still_tells_the_user_what_went_wrong()
    {
        // 未启用本地化（未注册 IStringLocalizer）——模板裁掉本地化的场景走这条路。
        // 这一形态下没有任何词条，消息就是用户唯一能拿到的原因
        var ex = new BadRequestException("Password must be at least 12 characters long.")
            .WithCode("Security:PasswordTooShort");

        var pd = await HandleAsync(ex, localizer: null);

        Assert.Equal("Password must be at least 12 characters long.", pd.Detail);
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
        BusinessMessageExposure exposure = BusinessMessageExposure.ClientErrors)
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
                MessageExposure = exposure
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
