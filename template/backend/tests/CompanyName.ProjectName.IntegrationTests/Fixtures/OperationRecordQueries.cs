#if (LocalIdentity)
using System.Text.Json;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>经操作记录接口按动作与目标查失败记录；读者是谁由调用方的会话决定（宿主或租户）。</summary>
internal static class OperationRecordQueries
{
    public static async Task<List<(string? FailureCode, string? AuthorizationBasis)>> GetFailuresAsync(
        HttpClient client, string action, string targetId)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/v1/operation-records?offset=0&limit=50&actions={action}&outcome=Failed"));
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("targetId").GetString() == targetId)
            .Select(item => (
                item.GetProperty("failureCode").GetString(),
                item.GetProperty("authorizationBasis").GetString()))
            .ToList();
    }

    public static async Task<int> CountSucceededAsync(HttpClient client, string action, string targetId)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/v1/operation-records?offset=0&limit=50&actions={action}&outcome=Succeeded"));
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("targetId").GetString() == targetId);
    }
}
#endif
