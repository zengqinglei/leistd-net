using System.Globalization;
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
/// 422 结构化字段错误契约验证：标准 <c>errors</c>（本地化字符串）与 <c>validationErrorDetails</c>
/// 扩展（机器契约：field/code/localizationKey/message）并存，且 message 随 culture 本地化、
/// 无键/未命中时回落诊断消息。端到端经 <see cref="BusinessExceptionHandler.TryHandleAsync"/> 驱动。
/// </summary>
public class ValidationErrorDetailsTests
{
    [Fact]
    public async Task Emits_field_level_code_and_details_alongside_localized_errors()
    {
        // 中文资源命中展示键；字段级 Code 应原样序列化，errors 与 validationErrorDetails 并存。
        var localizer = new StubLocalizer(new Dictionary<string, string>
        {
            ["User:PhoneAlreadyUsed"] = "号码已被占用",
        });

        var ex = new UnprocessableEntityException("email", "Invalid email format")
            .AddError(new ValidationError(
                Field: "phone",
                Message: "Phone number already in use",
                Code: "User:PhoneConflict",
                LocalizationKey: "User:PhoneAlreadyUsed"));

        var pd = await HandleAsync(ex, localizer, culture: "zh-CN");
        var validation = Assert.IsType<ValidationProblemDetails>(pd);

        // 展示文案唯一来源是标准 errors（本地化后的字符串，按字段分组）
        Assert.True(validation.Errors.ContainsKey("phone"));
        Assert.Contains("号码已被占用", validation.Errors["phone"]);

        // validationErrorDetails 扩展：纯机器契约（field/code/localizationKey），不含 message，与 errors 不重复
        var details = Assert.IsAssignableFrom<IEnumerable<object>>(validation.Extensions["validationErrorDetails"]);
        var phone = Assert.Single(details, d => Field(d) == "phone");
        Assert.Equal("User:PhoneConflict", Code(phone));            // 字段级 Code 被序列化
        Assert.Equal("User:PhoneAlreadyUsed", LocalizationKey(phone));
        Assert.Null(Message(phone));                                // 明细里不再冗余 message
    }

    [Fact]
    public async Task Falls_back_to_diagnostic_message_when_key_missing_or_absent()
    {
        // email 无 LocalizationKey；phone 有键但资源未命中 → 两者的展示文案（errors）都回落诊断消息。
        var localizer = new StubLocalizer(new Dictionary<string, string>());

        var ex = new UnprocessableEntityException("email", "Email is required")
            .AddError(new ValidationError(
                Field: "phone",
                Message: "Phone diagnostic",
                LocalizationKey: "User:NotThere"));

        var pd = await HandleAsync(ex, localizer, culture: "zh-CN");
        var validation = Assert.IsType<ValidationProblemDetails>(pd);

        // 展示文案（errors）回落到诊断消息
        Assert.Contains("Email is required", validation.Errors["email"]);
        Assert.Contains("Phone diagnostic", validation.Errors["phone"]);

        // 明细逐字段存在、且不含 message（机器契约与展示解耦）
        var details = Assert.IsAssignableFrom<IEnumerable<object>>(validation.Extensions["validationErrorDetails"]);
        var email = Assert.Single(details, d => Field(d) == "email");
        var phone = Assert.Single(details, d => Field(d) == "phone");
        Assert.Null(Message(email));
        Assert.Equal("User:NotThere", LocalizationKey(phone));
    }

    // ---- reflection helpers (匿名类型序列化前的属性读取) ----

    private static string? Field(object detail) => Prop(detail, "field");
    private static string? Code(object detail) => Prop(detail, "code");
    private static string? LocalizationKey(object detail) => Prop(detail, "localizationKey");
    private static string? Message(object detail) => Prop(detail, "message");

    private static string? Prop(object detail, string name) =>
        detail.GetType().GetProperty(name)?.GetValue(detail) as string;

    // ---- harness ----

    private static async Task<ProblemDetails> HandleAsync(
        BusinessException ex,
        IStringLocalizer localizer,
        string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            var services = new ServiceCollection();
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
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
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
}
