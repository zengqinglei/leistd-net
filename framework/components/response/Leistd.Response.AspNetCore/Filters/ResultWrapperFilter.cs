using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Leistd.Response.Wrappers;

namespace Leistd.Response.AspNetCore.Filters;

/// <summary>
/// 将成功响应包装为统一结果。
/// </summary>
public class ResultWrapperFilter(ILogger<ResultWrapperFilter> logger) : IAsyncResultFilter
{
    /// <inheritdoc />
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.ActionDescriptor.EndpointMetadata.Any(m => m is Attributes.NoWrapAttribute))
        {
            await next();
            return;
        }

        if (context.Result is ObjectResult objectResult && objectResult.Value is not Result)
        {
            if (objectResult.StatusCode is null or >= 200 and < 300)
            {
                logger.LogDebug("Wrapping response: {ActionName}", context.ActionDescriptor.DisplayName);

                var wrappedResult = Result<object?>.Ok(objectResult.Value);

                context.Result = new ObjectResult(wrappedResult)
                {
                    StatusCode = objectResult.StatusCode ?? 200
                };
            }
        }

        await next();
    }
}
