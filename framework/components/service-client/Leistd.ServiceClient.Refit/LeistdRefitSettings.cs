using System.Text.Json;
using Leistd.ServiceClient.Http;
using Refit;

namespace Leistd.ServiceClient.Refit;

/// <summary>
/// Leistd 统一的 <see cref="RefitSettings"/> 工厂：System.Text.Json（Web 默认，camelCase、
/// 大小写不敏感）序列化 + 非 2xx 响应经 <c>CreateRemoteErrorAsync</c> 还原为
/// <c>RemoteServiceException</c>——业务代码无论走手写路径还是 Refit 路径，
/// 捕获的都是同一错误契约，不感知 <see cref="ApiException"/>。
/// </summary>
public static class LeistdRefitSettings
{
    /// <summary>
    /// 创建统一配置的 <see cref="RefitSettings"/>。
    /// </summary>
    /// <param name="jsonOptions">自定义序列化选项；默认 <see cref="JsonSerializerDefaults.Web"/></param>
    /// <remarks>
    /// 注意：<c>ExceptionFactory</c> 只作用于由 Refit 反序列化的返回形态（<c>Task&lt;T&gt;</c> 等）。
    /// 返回 <c>Task&lt;HttpResponseMessage&gt;</c> 的方法（文件下载等）拿到的是原始响应，
    /// 须自行调用 <c>EnsureRemoteSuccessAsync()</c> 完成错误还原。
    /// </remarks>
    public static RefitSettings Create(JsonSerializerOptions? jsonOptions = null)
    {
        return new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(
                jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ExceptionFactory = async response =>
                response.IsSuccessStatusCode ? null : await response.CreateRemoteErrorAsync(),
        };
    }
}
