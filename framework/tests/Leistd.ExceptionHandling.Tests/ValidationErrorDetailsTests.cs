using System.Text.Json;
using System.Globalization;
using Leistd.ExceptionHandling.AspNetCore;
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
/// 422 字段错误契约：errors 是 RFC 9457 Problem Details 的 Leistd 自定义「数组 of 对象」扩展——每项一个
/// <see cref="ErrorItem"/>，同时承载 detail（本地化消息）/ field（出错字段）/ code（机器码）/
/// localizationKey。单一数据源、无重复。端到端经 <see cref="BusinessExceptionHandler.TryHandleAsync"/> 驱动。
/// </summary>
public class ValidationErrorDetailsTests
{
    [Fact]
    public async Task Errors_are_array_of_objects_carrying_detail_field_and_code()
    {
        // 中文资源命中错误码：detail 应为本地化文案；code/field 一并在同一项对象里。
        var localizer = new StubLocalizer(new Dictionary<string, string>
        {
            ["User:PhoneConflict"] = "号码已被占用",
        });

        var ex = new UnprocessableEntityException("email", "Invalid email format")
            .AddError(new ValidationError(
                Field: "phone",
                Message: "Phone number already in use",
                Code: "User:PhoneConflict"));

        var pd = await HandleAsync(ex, localizer, culture: "zh-CN");

        // 不再是 ValidationProblemDetails 字典，而是普通 ProblemDetails + errors 数组扩展
        Assert.IsNotType<ValidationProblemDetails>(pd);
        var errors = Assert.IsType<ErrorItem[]>(pd.Extensions["errors"]);

        var phone = Assert.Single(errors, e => e.Field == "phone");
        Assert.Equal("号码已被占用", phone.Detail);                    // 本地化消息
        Assert.Equal("User:PhoneConflict", phone.Code);               // 错误码兼词条键

        var email = Assert.Single(errors, e => e.Field == "email");
        Assert.Equal("Invalid email format", email.Detail);
        Assert.Null(email.Code);                                      // 未设置 → 可空、序列化时省略
    }

    [Fact]
    public async Task Detail_falls_back_to_diagnostic_message_when_key_missing_or_absent()
    {
        // email 无错误码；phone 有码但资源未命中 → 两者 detail 都回落诊断消息。
        // 字段级没有"所属类别"可推默认码，因此保持可空，这与顶层不同
        var localizer = new StubLocalizer(new Dictionary<string, string>());

        var ex = new UnprocessableEntityException("email", "Email is required")
            .AddError(new ValidationError(
                Field: "phone",
                Message: "Phone diagnostic",
                Code: "User:NotThere"));

        var pd = await HandleAsync(ex, localizer, culture: "zh-CN");
        var errors = Assert.IsType<ErrorItem[]>(pd.Extensions["errors"]);

        Assert.Equal("Email is required", Assert.Single(errors, e => e.Field == "email").Detail);
        var phone = Assert.Single(errors, e => e.Field == "phone");
        Assert.Equal("Phone diagnostic", phone.Detail);
        Assert.Equal("User:NotThere", phone.Code);
    }

    [Fact]
    public void Serializes_to_camelCase_and_omits_null_machine_fields()
    {
        // ProblemDetails 默认以 camelCase 序列化：无需 JsonPropertyName 即得 detail/field/code；
        // 可空的 code 未设置时应省略（WhenWritingNull）。
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // 断言 key 命名（camelCase）——用 ASCII 值避免非 ASCII 转义干扰对 key 的判断。
        var withCode = JsonSerializer.Serialize(
            new ErrorItem("phone taken", "phone", "User:PhoneConflict"), options);
        Assert.Contains("\"detail\":", withCode);
        Assert.Contains("\"field\":", withCode);
        Assert.Contains("\"code\":\"User:PhoneConflict\"", withCode);
        // 不应出现 PascalCase 键（即 JsonPropertyName 冗余、camelCase 默认已生效）
        Assert.DoesNotContain("\"Detail\":", withCode);
        Assert.DoesNotContain("\"Code\":", withCode);

        // 可空机器字段未设置时省略
        var noCode = JsonSerializer.Serialize(new ErrorItem("required", "email", null), options);
        Assert.DoesNotContain("\"code\":", noCode);
        Assert.Contains("\"detail\":", noCode);
        Assert.Contains("\"field\":", noCode);
    }


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
                new MutableOptionsMonitor<GlobalExceptionOptions>(new() { Enabled = true }),
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
