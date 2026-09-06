using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Leistd.ExceptionHandling.AspNetCore;
using Leistd.ExceptionHandling.AspNetCore.Handlers;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Leistd.ExceptionHandling.AspNetCore.Constants;

namespace Leistd.ExceptionHandling.Tests;

/// <summary>
/// 真实 ASP.NET 管道回归：自动 400（[ApiController] 模型校验 → <see cref="DependencyInjection.ConfigureApiValidation"/>
/// 的 InvalidModelStateResponseFactory，经 MVC AddJsonOptions 序列化）与业务 422（<see cref="BusinessExceptionHandler"/>
/// → IProblemDetailsService，经 ConfigureHttpJsonOptions 序列化）走的是**两套独立 JSON 配置**。本用例给两者施加**同一条
/// 会改名所有属性的自定义命名策略（全大写）**，断言两条响应中 errors 数组内 <see cref="ErrorItem"/> 的属性名一致地变为
/// FIELD/DETAIL——守卫「两套配置必须统一」这一关键修复不被后续无意拆开，并证明 ErrorItem 未固定 [JsonPropertyName]。
/// （命名策略只作用于扩展项的值对象；顶层 type/title/status/detail/instance 由内置 ProblemDetailsJsonConverter 固定小写
/// 写出，扩展键 errors 按字典键原样写出——两者本就不随策略变化，故不作为断言目标。）
/// </summary>
public class ValidationErrorNamingPipelineTests
{
    // 会改变所有属性名的自定义命名策略：全大写。若任一路径固定了属性名/未跟随宿主策略，断言即失败。
    private sealed class UpperCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) => name.ToUpperInvariant();
    }

    private static void ApplyUpper(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = new UpperCaseNamingPolicy();
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }

    // 非弃用宿主构建（.NET 10）：HostBuilder + ConfigureWebHost(UseTestServer)，避免 WebHostBuilder(ASPDEPR004)
    // 与 TestServer(IWebHostBuilder)(ASPDEPR008)。TestServer/UseTestServer/GetTestClient 由 Microsoft.AspNetCore.TestHost 提供。
    private static Task<IHost> StartHostAsync()
    {
        return new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddProblemDetails();
                        services.AddExceptionHandler<BusinessExceptionHandler>();
                        services.Configure<GlobalExceptionOptions>(o => o.Enabled = true);

                        // 同一命名策略应用到两套 JSON 配置——正是模板组合根的做法。
                        services.ConfigureHttpJsonOptions(o => ApplyUpper(o.SerializerOptions));
                        services.AddControllers()
                            .AddApplicationPart(typeof(ProbeController).Assembly)
                            .AddJsonOptions(o => ApplyUpper(o.JsonSerializerOptions))
                            .ConfigureApiValidation();
                    })
                    .Configure(app =>
                    {
                        app.Use(async (context, next) =>
                        {
                            if (context.Request.Headers.ContainsKey("X-Test-Activity"))
                            {
                                using var activity = new Activity("validation-request")
                                    .SetIdFormat(ActivityIdFormat.W3C)
                                    .Start();
                                await next();
                                return;
                            }

                            await next();
                        });
                        app.UseExceptionHandler();
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapControllers());
                    });
            })
            .StartAsync();
    }

    [Fact]
    public async Task Auto400_and_manual422_use_same_host_naming_policy_and_validation_type()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        // 自动 400：缺 Name → 校验失败。
        var r400 = await client.PostAsJsonAsync("/probe/auto", new { });
        Assert.Equal(HttpStatusCode.BadRequest, r400.StatusCode);
        var body400 = await r400.Content.ReadAsStringAsync();

        // 业务 422。
        var r422 = await client.PostAsync("/probe/manual", content: null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r422.StatusCode);
        var body422 = await r422.Content.ReadAsStringAsync();

        // 关键断言在 errors 数组内每个 ErrorItem 的**属性名**上：两条响应都必须把 Field/Detail 序列化为大写
        // FIELD/DETAIL（跟随宿主命名策略），而不出现小写 field/detail 项键。
        // （注：errors 本身是 ProblemDetails.Extensions 的字典键，按字典键原样写出——不受命名策略影响；顶层
        // type/title/status/detail/instance 由内置 ProblemDetailsJsonConverter 以固定小写写出，同样不受策略影响。
        // 命名策略只作用于扩展项的**值对象**，这正是这里要证明 400/422 一致、且 ErrorItem 无 [JsonPropertyName] 的点。）
        foreach (var body in new[] { body400, body422 })
        {
            Assert.Contains("\"FIELD\":", body);
            Assert.Contains("\"DETAIL\":", body);
            // ErrorItem 未固定 [JsonPropertyName]：不应出现小写项键（field 仅来自 ErrorItem，顶层无此成员）。
            Assert.DoesNotContain("\"field\":", body);
        }

        // 校验 wire contract：400 与 422 必须携带同一个稳定校验 problem type（两条校验路径统一的第二重证据）。
        Assert.Equal(ProblemTypes.ValidationError, ReadType(body400));
        Assert.Equal(ProblemTypes.ValidationError, ReadType(body422));
    }

    [Fact]
    public async Task Auto400_emits_the_w3c_trace_id_without_span_metadata()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Activity", "true");

        var response = await client.PostAsJsonAsync("/probe/auto", new { });
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var traceId = document.RootElement.GetProperty("traceId").GetString();

        Assert.NotNull(traceId);
        Assert.Equal(32, traceId.Length);
        Assert.DoesNotContain('-', traceId);
    }

    private static string? ReadType(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
    }
}

// 注意：MVC 控制器发现要求类型为**顶层 public**（嵌套 public 类型的 Type.IsPublic 为 false，会被
// ControllerFeatureProvider 忽略），故 ProbeController 不能内嵌在测试类中。
[ApiController]
[Route("[controller]")]
public sealed class ProbeController : ControllerBase
{
    // 自动 400：缺 Name → [ApiController] 模型校验失败 → ConfigureApiValidation 的 factory 产出 errors。
    [HttpPost("auto")]
    public IActionResult Auto([FromBody] ProbeInput input) => Ok();

    // 业务 422：抛 UnprocessableEntityException → 全局 BusinessExceptionHandler 产出 errors。
    [HttpPost("manual")]
    public IActionResult Manual() => throw new UnprocessableEntityException("phone", "invalid phone");
}

public sealed record ProbeInput([Required] string Name);
