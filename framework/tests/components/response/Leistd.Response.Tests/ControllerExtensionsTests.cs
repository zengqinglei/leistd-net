using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Leistd.ExceptionHandling;
using Leistd.Response.AspNetCore.Extensions;
using Leistd.Response.Wrappers;
using Xunit;

namespace Leistd.Response.Tests;

public class ControllerExtensionsTests
{
    private sealed class TestController : ControllerBase;

    // 状态码来自入参，不再从业务码前三位切出来：业务码怎么编是宿主的事。
    [Theory]
    [InlineData(400, 1001)]
    [InlineData(200, 50003)]
    [InlineData(422, 7)]
    public void FailResult_uses_the_given_status_code_regardless_of_business_code(int statusCode, int code)
    {
        var result = Assert.IsType<ObjectResult>(new TestController().FailResult(statusCode, code, "失败"));

        Assert.Equal(statusCode, result.StatusCode);
        var envelope = Assert.IsType<Result>(result.Value);
        Assert.Equal(code, envelope.Code);
        Assert.Equal("失败", envelope.Message);
    }

    [Fact]
    public void FailResultWithErrors_carries_the_framework_wide_field_error_shape()
    {
        var errors = new[] { new ErrorItem("必须填写", "name", "Error:Required") };

        var result = Assert.IsType<ObjectResult>(
            new TestController().FailResultWithErrors(422, 42201, "校验失败", errors));

        Assert.Equal(422, result.StatusCode);
        var envelope = Assert.IsType<ErrorResult>(result.Value);
        Assert.Equal(errors, envelope.Errors);
    }

    // 字段错误的线上形状必须与 ProblemDetails 的 errors 一致，否则同一个服务会因为
    // 开发者当时是 throw 还是 return 而吐出两种形状。
    [Fact]
    public void Field_errors_serialize_to_detail_field_code()
    {
        var envelope = ErrorResult.Fail(42201, "invalid", [new ErrorItem("is required", "name", null)]);

        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // code 为空时省略，与 ProblemDetails 的 errors 逐字段一致。
        Assert.Contains("\"errors\":[{\"detail\":\"is required\",\"field\":\"name\"}]", json);
    }

    [Fact]
    public void OkResult_wraps_data_with_zero_code()
    {
        var result = Assert.IsType<OkObjectResult>(new TestController().OkResult(new { Id = 1 }));

        Assert.Equal(0, Assert.IsAssignableFrom<Result>(result.Value).Code);
    }
}
