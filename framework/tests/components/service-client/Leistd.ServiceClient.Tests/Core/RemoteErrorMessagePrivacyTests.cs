using System.Net;
using System.Text;
using System.Text.Json;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.Handlers;
using Leistd.ServiceClient.Http;
using Leistd.TestBase.Doubles;
using Xunit;

namespace Leistd.ServiceClient.Tests.Core;

/// <summary>异常消息不带远端文本与查询串：它们可能含个人信息，而消息随异常对象进日志；原文只留在结构化属性里。</summary>
/// <remarks>第三方响应可能回显姓名、证件号，内部服务的 detail 也可能回显输入；查询串常带被查询人的标识。</remarks>
public class RemoteErrorMessagePrivacyTests
{
    private const string Sentinel = "SENTINEL-PII-7f3a";

    private static HttpResponseMessage Response(
        HttpStatusCode status, string body, string mediaType = "application/json", string? uri = null) => new(status)
    {
        RequestMessage = new HttpRequestMessage(HttpMethod.Post, uri ?? "https://vendor.example/api/v1/checks"),
        Content = new StringContent(body, Encoding.UTF8, mediaType),
    };

    private static void AssertNoSentinel(Exception exception)
    {
        Assert.DoesNotContain(Sentinel, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_non_json_error_body_stays_out_of_the_message_but_in_ResponseBody()
    {
        using var response = Response(HttpStatusCode.BadRequest, $"<html>name {Sentinel} rejected</html>", "text/html");

        var exception = await response.CreateRemoteErrorAsync();

        AssertNoSentinel(exception);
        Assert.Contains(Sentinel, exception.ResponseBody, StringComparison.Ordinal);
        Assert.Contains("POST https://vendor.example/api/v1/checks", exception.Message, StringComparison.Ordinal);
        Assert.Contains("400", exception.Message, StringComparison.Ordinal);
    }

    // 有业务码时 detail 作为公开说明还原到 Detail；没有业务码时不还原，但两种情况都不进 Message
    [Theory]
    [InlineData("message", null)]
    [InlineData("detail", null)]
    [InlineData("title", null)]
    [InlineData("detail", "Check:Rejected")]
    [InlineData("message", "Check:Rejected")]
    public async Task Remote_json_explanations_stay_out_of_the_message(string field, string? code)
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            [field] = $"applicant {Sentinel} rejected",
            ["code"] = code,
            ["traceId"] = "00-remote-trace-01",
        });
        using var response = Response(HttpStatusCode.UnprocessableContent, body);

        var exception = await response.CreateRemoteErrorAsync();

        AssertNoSentinel(exception);
        Assert.Contains(Sentinel, exception.ResponseBody, StringComparison.Ordinal);
        Assert.Contains("00-remote-trace-01", exception.Message, StringComparison.Ordinal);
        if (code is null)
        {
            Assert.Null(exception.Detail);
        }
        else
        {
            Assert.Contains(code, exception.Message, StringComparison.Ordinal);
            Assert.Contains(Sentinel, exception.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_query_string_stays_out_of_the_message()
    {
        using var response = Response(HttpStatusCode.NotFound, """{"status":404}""",
            uri: $"https://vendor.example/api/v1/search?name={Sentinel}#{Sentinel}");

        var exception = await response.CreateRemoteErrorAsync();

        AssertNoSentinel(exception);
        Assert.Contains("https://vendor.example/api/v1/search", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_info_in_the_address_stays_out_of_the_message()
    {
        using var response = Response(HttpStatusCode.NotFound, """{"status":404}""",
            uri: $"https://{Sentinel}:secret@vendor.example/api/v1/search");

        var exception = await response.CreateRemoteErrorAsync();

        AssertNoSentinel(exception);
        Assert.Contains("https://vendor.example/api/v1/search", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_envelope_failure_message_stays_out_of_the_message()
    {
        using var response = Response(HttpStatusCode.OK, $$"""{"code":50001,"message":"applicant {{Sentinel}} rejected"}""");

        var exception = await Assert.ThrowsAsync<RemoteServiceException>(() => response.ReadResultAsync<object>());

        AssertNoSentinel(exception);
        Assert.Equal("50001", exception.ErrorCode);
        Assert.Contains("50001", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Sentinel, exception.Detail, StringComparison.Ordinal);
    }

    // JsonException 的消息会带出出错位置的载荷字符，只作为内层异常保留
    [Fact]
    public async Task A_deserialization_failure_does_not_copy_the_parser_message()
    {
        using var response = Response(HttpStatusCode.OK, $$"""{"id": {{Sentinel}}}""");

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() => response.ReadContentAsync<object>());

        var inner = Assert.IsType<JsonException>(exception.InnerException);
        Assert.DoesNotContain(inner.Message, exception.Message, StringComparison.Ordinal);
        Assert.Equal(ServiceClientFailureKind.InvalidResponse, exception.FailureKind);
    }

    [Fact]
    public async Task A_transport_failure_message_leaves_out_the_query_string()
    {
        using var invoker = new HttpMessageInvoker(new TransportFailureHandler
        {
            InnerHandler = new ThrowingHttpMessageHandler(_ =>
                new HttpRequestException(HttpRequestError.ConnectionError, "connection refused")),
        });

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"https://vendor.example/api/v1/search?name={Sentinel}"),
            CancellationToken.None));

        Assert.DoesNotContain(Sentinel, exception.Message, StringComparison.Ordinal);
        Assert.Contains("GET https://vendor.example/api/v1/search", exception.Message, StringComparison.Ordinal);
    }
}
