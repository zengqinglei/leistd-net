#if (LocalIdentity)
#if (IncludeOperationRecords)
using System.Text.Json;
#else
using Microsoft.Extensions.DependencyInjection;
#endif
using Microsoft.AspNetCore.Mvc.Testing;

namespace CompanyName.ProjectName.IntegrationTests.Fixtures;

/// <summary>按实际输出适配读取证据：数据库经受保护端点，日志经宿主日志管道的收集提供程序。</summary>
internal static class OperationRecordQueries
{
    public static async Task<List<(string? FailureCode, string? AuthorizationBasis)>> GetFailuresAsync(
        WebApplicationFactory<Program> host, HttpClient client, string action, string targetId, Guid? tenantId = null)
    {
#if (IncludeOperationRecords)
        using var body = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/v1/operation-records?offset=0&limit=50&actions={action}&outcome=failed"));
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("targetId").GetString() == targetId)
            .Select(item => (
                item.GetProperty("failureCode").GetString(),
                item.GetProperty("authorizationBasis").GetString()))
            .ToList();
#else
        await Task.CompletedTask;
        return host.Services.GetRequiredService<OperationRecordLogCapture>().Snapshot()
            .Where(entry => OperationRecordLogCapture.Field(entry, "OperationAction") == action
                && OperationRecordLogCapture.Field(entry, "OperationTargetId") == targetId
                && OperationRecordLogCapture.Field(entry, "OperationTenantId") == tenantId?.ToString()
                && OperationRecordLogCapture.Field(entry, "OperationOutcome") == "Failed")
            .OrderByDescending(entry => entry.Timestamp)
            .Select(entry => (OperationRecordLogCapture.Field(entry, "OperationFailureCode"),
                OperationRecordLogCapture.Field(entry, "OperationAuthorizationBasis"))).ToList();
#endif
    }

    /// <summary>失败记录的操作人标识；系统触发、没有主体的记录为 <see langword="null"/>。</summary>
    public static async Task<List<string?>> GetFailureActorsAsync(
        WebApplicationFactory<Program> host, HttpClient client, string action, string targetId, Guid? tenantId = null)
    {
#if (IncludeOperationRecords)
        using var body = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/v1/operation-records?offset=0&limit=50&actions={action}&outcome=failed"));
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("targetId").GetString() == targetId)
            .Select(item => item.TryGetProperty("actorId", out var actor) ? actor.GetString() : null)
            .ToList();
#else
        await Task.CompletedTask;
        return host.Services.GetRequiredService<OperationRecordLogCapture>().Snapshot()
            .Where(entry => OperationRecordLogCapture.Field(entry, "OperationAction") == action
                && OperationRecordLogCapture.Field(entry, "OperationTargetId") == targetId
                && OperationRecordLogCapture.Field(entry, "OperationTenantId") == tenantId?.ToString()
                && OperationRecordLogCapture.Field(entry, "OperationOutcome") == "Failed")
            .Select(entry => OperationRecordLogCapture.Field(entry, "OperationActorId"))
            .ToList();
#endif
    }

    public static async Task<int> CountSucceededAsync(
        WebApplicationFactory<Program> host, HttpClient client, string action, string targetId, Guid? tenantId = null,
        string? authorizationBasis = null)
    {
#if (IncludeOperationRecords)
        using var body = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/v1/operation-records?offset=0&limit=50&actions={action}&outcome=succeeded"));
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("targetId").GetString() == targetId
                && (authorizationBasis is null || item.GetProperty("authorizationBasis").GetString() == authorizationBasis));
#else
        await Task.CompletedTask;
        return host.Services.GetRequiredService<OperationRecordLogCapture>().Snapshot()
            .Count(entry => OperationRecordLogCapture.Field(entry, "OperationAction") == action
                && OperationRecordLogCapture.Field(entry, "OperationTargetId") == targetId
                && OperationRecordLogCapture.Field(entry, "OperationTenantId") == tenantId?.ToString()
                && OperationRecordLogCapture.Field(entry, "OperationOutcome") == "Succeeded"
                && (authorizationBasis is null
                    || OperationRecordLogCapture.Field(entry, "OperationAuthorizationBasis") == authorizationBasis));
#endif
    }
}
#endif
