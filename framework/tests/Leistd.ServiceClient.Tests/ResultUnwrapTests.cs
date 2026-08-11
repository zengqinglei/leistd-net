using System.Net;
using System.Text;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.Http;
using Xunit;

namespace Leistd.ServiceClient.Tests;

public class ResultUnwrapTests
{
    private sealed record OrderDto(Guid Id, string Name);

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string? body, string mediaType = "application/json")
    {
        var response = new HttpResponseMessage(statusCode)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://demo/api/orders/1"),
        };
        if (body is not null)
        {
            response.Content = new StringContent(body, Encoding.UTF8, mediaType);
        }

        return response;
    }

    [Fact]
    public async Task 成功信封_返回data()
    {
        var id = Guid.NewGuid();
        using var response = Response(HttpStatusCode.OK,
            $$$"""{"code":0,"message":null,"data":{"id":"{{{id}}}","name":"下单"}}""");

        var data = await response.ReadResultAsync<OrderDto>();

        Assert.NotNull(data);
        Assert.Equal(id, data.Id);
        Assert.Equal("下单", data.Name);
    }

    [Fact]
    public async Task 无数据信封_code为0不抛异常()
    {
        using var response = Response(HttpStatusCode.OK, """{"code":0,"message":"ok"}""");

        await response.ReadResultAsync();
    }

    [Fact]
    public async Task 信封code非0_抛RemoteServiceException()
    {
        using var response = Response(HttpStatusCode.OK, """{"code":50001,"message":"库存不足"}""");

        var exception = await Assert.ThrowsAsync<RemoteServiceException>(() => response.ReadResultAsync<OrderDto>());

        Assert.Equal(50001, exception.ErrorCode);
        Assert.Contains("库存不足", exception.Message);
    }

    [Fact]
    public async Task 非2xx的ProblemDetails_还原code与traceId与字段错误()
    {
        using var response = Response(HttpStatusCode.UnprocessableContent,
            """
            {"type":"https://err/validation","title":"Unprocessable Entity","status":422,
             "code":422001,"message":"参数校验失败","traceId":"00-abc-def-01",
             "errors":[{"field":"name","detail":"必填","code":"Required"}]}
            """);

        var exception = await Assert.ThrowsAsync<RemoteServiceException>(() => response.ReadResultAsync<OrderDto>());

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal(422001, exception.ErrorCode);
        Assert.Equal("00-abc-def-01", exception.RemoteTraceId);
        Assert.Contains("00-abc-def-01", exception.Message);
        var error = Assert.Single(exception.Errors);
        Assert.Equal(("name", "必填", "Required"), (error.Field, error.Detail, error.Code));
    }

    [Fact]
    public async Task 非2xx的非JSON响应_保留原始体片段()
    {
        using var response = Response(HttpStatusCode.BadGateway, "<html>gateway error</html>", "text/html");

        var exception = await Assert.ThrowsAsync<RemoteServiceException>(() => response.ReadResultAsync<OrderDto>());

        Assert.Equal(502, exception.StatusCode);
        Assert.Null(exception.ErrorCode);
        Assert.Contains("<html>gateway error</html>", exception.ResponseBody);
    }

    [Fact]
    public async Task 空响应体_抛ServiceClientException()
    {
        using var response = Response(HttpStatusCode.OK, null);

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() => response.ReadResultAsync<OrderDto>());

        Assert.Contains("响应体为空", exception.Message);
    }

    [Fact]
    public async Task 非法JSON_抛ServiceClientException()
    {
        using var response = Response(HttpStatusCode.OK, "not-json");

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() => response.ReadResultAsync<OrderDto>());

        Assert.Contains("反序列化失败", exception.Message);
    }

    [Fact]
    public async Task 未包装端点_ReadContentAsync直接反序列化()
    {
        var id = Guid.NewGuid();
        using var response = Response(HttpStatusCode.OK, $$$"""{"id":"{{{id}}}","name":"raw"}""");

        var data = await response.ReadContentAsync<OrderDto>();

        Assert.NotNull(data);
        Assert.Equal(id, data.Id);
    }

    [Fact]
    public async Task 未包装端点_非2xx同样还原远端错误()
    {
        using var response = Response(HttpStatusCode.NotFound,
            """{"status":404,"code":404001,"message":"不存在","traceId":"t-1"}""");

        var exception = await Assert.ThrowsAsync<RemoteServiceException>(() => response.ReadContentAsync<OrderDto>());

        Assert.Equal(404001, exception.ErrorCode);
        Assert.Equal("t-1", exception.RemoteTraceId);
    }
}
