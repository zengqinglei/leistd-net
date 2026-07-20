using System.Globalization;
using Leistd.Exception.AspNetCore;
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
/// 422 字段错误契约：errors 采用 RFC 9457 / JSON:API 惯用的「数组 of 对象」形态——每项一个
/// <see cref="ErrorItem"/>，同时承载 detail（本地化消息）/ pointer（JSON Pointer）/ code（机器码）/
/// localizationKey。单一数据源、无重复。端到端经 <see cref="BusinessExceptionHandler.TryHandleAsync"/> 驱动。
/// </summary>
public class ValidationErrorDetailsTests
{
    [Fact]
    public async Task Errors_are_array_of_objects_carrying_detail_pointer_and_code()
    {
        // 中文资源命中展示键：detail 应为本地化文案；code/pointer/localizationKey 一并在同一项对象里。
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

        // 不再是 ValidationProblemDetails 字典，而是普通 ProblemDetails + errors 数组扩展
        Assert.IsNotType<ValidationProblemDetails>(pd);
        var errors = Assert.IsType<ErrorItem[]>(pd.Extensions["errors"]);

        var phone = Assert.Single(errors, e => e.Pointer == "#/phone");
        Assert.Equal("号码已被占用", phone.Detail);                    // 本地化消息
        Assert.Equal("User:PhoneConflict", phone.Code);               // 字段级机器码
        Assert.Equal("User:PhoneAlreadyUsed", phone.LocalizationKey);

        var email = Assert.Single(errors, e => e.Pointer == "#/email");
        Assert.Equal("Invalid email format", email.Detail);
        Assert.Null(email.Code);                                      // 未设置 → 可空、序列化时省略
    }

    [Fact]
    public async Task Detail_falls_back_to_diagnostic_message_when_key_missing_or_absent()
    {
        // email 无 LocalizationKey；phone 有键但资源未命中 → 两者 detail 都回落诊断消息。
        var localizer = new StubLocalizer(new Dictionary<string, string>());

        var ex = new UnprocessableEntityException("email", "Email is required")
            .AddError(new ValidationError(
                Field: "phone",
                Message: "Phone diagnostic",
                LocalizationKey: "User:NotThere"));

        var pd = await HandleAsync(ex, localizer, culture: "zh-CN");
        var errors = Assert.IsType<ErrorItem[]>(pd.Extensions["errors"]);

        Assert.Equal("Email is required", Assert.Single(errors, e => e.Pointer == "#/email").Detail);
        var phone = Assert.Single(errors, e => e.Pointer == "#/phone");
        Assert.Equal("Phone diagnostic", phone.Detail);
        Assert.Equal("User:NotThere", phone.LocalizationKey);
    }

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
