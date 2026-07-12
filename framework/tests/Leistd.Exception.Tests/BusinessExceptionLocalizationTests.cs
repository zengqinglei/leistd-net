using Leistd.Exception.AspNetCore.Handlers;
using Leistd.Exception.AspNetCore.Localization;
using Leistd.Exception.AspNetCore.Options;
using Leistd.Exception.Core;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Exception.Tests;

public sealed class BusinessExceptionLocalizationTests
{
    [Fact]
    public void Business_exception_keeps_five_digit_code_and_localization_metadata()
    {
        var exception = new BadRequestException("Username already exists")
            .WithCode("01")
            .WithLocalization("Exception:40001", "admin");

        Assert.Equal(40001, exception.Code);
        Assert.Equal("Exception:40001", exception.LocalizationKey);
        Assert.Equal(["admin"], exception.LocalizationArguments);
    }

    [Fact]
    public void Business_exception_accepts_only_complete_codes_matching_its_http_prefix()
    {
        Assert.Equal(40001, new BadRequestException("Invalid request").WithCode(40001).Code);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BadRequestException("Invalid request").WithCode(4001));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BadRequestException("Invalid request").WithCode(400001));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BadRequestException("Invalid request").WithCode(50001));
    }

    [Fact]
    public async Task Handler_uses_registered_localizer_for_user_facing_values()
    {
        var problemDetailsService = new CapturingProblemDetailsService();
        var handler = new BusinessExceptionHandler(
            Options.Create(new GlobalExceptionOptions { Enable = true }),
            new TestHostEnvironment(),
            NullLogger<BusinessExceptionHandler>.Instance,
            problemDetailsService,
            [new StubLocalizer()]);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/users";

        var handled = await handler.TryHandleAsync(
            context,
            new BadRequestException("Username already exists").WithCode("01"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("请求错误", problemDetailsService.ProblemDetails?.Title);
        Assert.Equal("用户名已存在", problemDetailsService.ProblemDetails?.Detail);
        Assert.Equal("urn:leistd:error:40001", problemDetailsService.ProblemDetails?.Type);
        Assert.Equal(40001, problemDetailsService.ProblemDetails?.Extensions["code"]);
    }

    [Fact]
    public async Task Handler_falls_back_to_original_message_when_localizer_fails()
    {
        var problemDetailsService = new CapturingProblemDetailsService();
        var handler = new BusinessExceptionHandler(
            Options.Create(new GlobalExceptionOptions { Enable = true }),
            new TestHostEnvironment(),
            NullLogger<BusinessExceptionHandler>.Instance,
            problemDetailsService,
            [new ThrowingLocalizer()]);

        var handled = await handler.TryHandleAsync(
            new DefaultHttpContext(),
            new BadRequestException("Original message"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal("Original message", problemDetailsService.ProblemDetails?.Detail);
        Assert.Equal("Bad Request", problemDetailsService.ProblemDetails?.Title);
        Assert.Equal("https://tools.ietf.org/html/rfc7231#section-6.5.1", problemDetailsService.ProblemDetails?.Type);
    }

    private sealed class StubLocalizer : IExceptionResponseLocalizer
    {
        public ExceptionResponseLocalization Localize(BusinessException exception, int statusCode)
        {
            Assert.Equal(40001, exception.Code);
            Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
            return new ExceptionResponseLocalization("用户名已存在", "请求错误");
        }
    }

    private sealed class ThrowingLocalizer : IExceptionResponseLocalizer
    {
        public ExceptionResponseLocalization Localize(BusinessException exception, int statusCode) =>
            throw new InvalidOperationException("Missing resource backend");
    }

    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetails? ProblemDetails { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            ProblemDetails = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            ProblemDetails = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = nameof(Leistd.Exception.Tests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
