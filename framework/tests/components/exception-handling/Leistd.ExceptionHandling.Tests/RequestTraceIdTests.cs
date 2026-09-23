using System.Diagnostics;
using Leistd.ExceptionHandling.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Leistd.ExceptionHandling.Tests;

public sealed class RequestTraceIdTests
{
    [Fact]
    public void Selected_request_id_wins_over_a_nested_activity_regardless_of_format()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "request-id_with-hyphen" };
        using var activity = new Activity("nested").Start();

        Assert.Equal("request-id_with-hyphen", RequestTraceId.Get(context));
    }

    [Fact]
    public void Activity_supplies_a_w3c_id_when_request_identifier_is_empty()
    {
        var context = new DefaultHttpContext { TraceIdentifier = string.Empty };
        using var activity = new Activity("request").Start();

        Assert.Equal(activity.TraceId.ToHexString(), RequestTraceId.Get(context));
    }

    [Fact]
    public void Request_identifier_remains_available_without_an_activity()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "server-request-id" };

        Assert.Equal("server-request-id", RequestTraceId.Get(context));
    }
}
