using System.Runtime.CompilerServices;
using Leistd.ExceptionHandling.AspNetCore.Handlers;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Leistd.TestBase.Doubles;

namespace Leistd.ExceptionHandling.Tests;

public sealed class ExceptionDetailsTests
{
    [Fact]
    public async Task Diagnostic_fields_are_omitted_by_default()
    {
        var (handler, capture, _) = CreateHandler(new GlobalExceptionOptions());
        var exception = CaptureBusinessException();

        await HandleAsync(handler, exception);

        Assert.False(capture.ProblemDetails!.Extensions.ContainsKey("details"));
        Assert.False(capture.ProblemDetails.Extensions.ContainsKey("stackTrace"));
    }

    [Fact]
    public async Task Enabled_diagnostics_keep_details_and_stack_trace_distinct()
    {
        var (handler, capture, _) = CreateHandler(new GlobalExceptionOptions
        {
            IncludeExceptionDetails = true
        });
        var exception = CaptureBusinessException();

        await HandleAsync(handler, exception);

        Assert.Equal("business diagnostics", capture.ProblemDetails!.Extensions["details"]);
        var stackTrace = Assert.IsType<string>(capture.ProblemDetails.Extensions["stackTrace"]);
        Assert.Contains(nameof(CaptureBusinessException), stackTrace);
        Assert.DoesNotContain("business diagnostics", stackTrace);
    }

    [Fact]
    public async Task Enabled_switch_is_read_for_each_exception()
    {
        var (handler, _, monitor) = CreateHandler(new GlobalExceptionOptions());

        Assert.True(await HandleAsync(handler, CaptureBusinessException()));

        monitor.Set(new GlobalExceptionOptions { Enabled = false });
        Assert.False(await HandleAsync(handler, CaptureBusinessException()));

        monitor.Set(new GlobalExceptionOptions { Enabled = true });
        Assert.True(await HandleAsync(handler, CaptureBusinessException()));
    }

    [Fact]
    public async Task Detail_switch_is_read_for_each_exception()
    {
        var (handler, capture, monitor) = CreateHandler(new GlobalExceptionOptions());

        await HandleAsync(handler, CaptureBusinessException());
        Assert.False(capture.ProblemDetails!.Extensions.ContainsKey("details"));

        monitor.Set(new GlobalExceptionOptions { IncludeExceptionDetails = true });
        await HandleAsync(handler, CaptureBusinessException());
        Assert.True(capture.ProblemDetails!.Extensions.ContainsKey("details"));

        monitor.Set(new GlobalExceptionOptions { IncludeExceptionDetails = false });
        await HandleAsync(handler, CaptureBusinessException());
        Assert.False(capture.ProblemDetails!.Extensions.ContainsKey("details"));
    }

    private static (
        BusinessExceptionHandler Handler,
        CapturingProblemDetailsService Capture,
        MutableOptionsMonitor<GlobalExceptionOptions> Monitor) CreateHandler(
            GlobalExceptionOptions options)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var capture = new CapturingProblemDetailsService();
        var monitor = new MutableOptionsMonitor<GlobalExceptionOptions>(options);
        var handler = new BusinessExceptionHandler(
            monitor,
            NullLogger<BusinessExceptionHandler>.Instance,
            capture,
            services);
        return (handler, capture, monitor);
    }

    private static async Task<bool> HandleAsync(
        BusinessExceptionHandler handler,
        Exception exception)
    {
        var context = new DefaultHttpContext();
        return await handler.TryHandleAsync(context, exception, default);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BusinessException CaptureBusinessException()
    {
        try
        {
            throw new BadRequestException("diagnostic message")
                .WithDetails("business diagnostics");
        }
        catch (BusinessException exception)
        {
            return exception;
        }
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
}
