using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 部署安全：开发环境以外，只在单机上成立的回落必须缺配即失败，而不是带着隐患照常启动。
/// </summary>
/// <remarks>
/// 测试宿主跑在 Testing 环境（非开发环境），与预发、生产走同一条校验路径。
/// 每条用例只撤掉一项配置，断言启动失败且错误指明缺的是哪个键。
/// </remarks>
public sealed class DeploymentSafeguardsTests
{
    // 本地密钥目录随容器重建而消失、多副本之间不共享：登录 Cookie、租户连接串、机密设置随之无法解密
    [Fact]
    public void Data_protection_keys_must_be_persisted_outside_development()
    {
        using var factory = new ProjectWebApplicationFactory();

        var exception = StartupFailure(factory, builder => builder.UseSetting("DataProtection:KeysPath", ""));

        Assert.Contains("DataProtection:KeysPath", exception.ToString(), StringComparison.Ordinal);
    }
#if (OpenIddictServer)

    // 开发证书默认关闭：每台机器各一份，多副本互不认、重建容器后令牌全部失效。未打开时必须给证书文件
    [Fact]
    public void Token_certificates_are_required_unless_development_certificates_are_enabled()
    {
        using var factory = new ProjectWebApplicationFactory();

        var exception = StartupFailure(factory, builder => builder.UseSetting("OAuth:UseDevelopmentCertificates", "false"));

        Assert.Contains("OAuth:SigningCertificatePath", exception.ToString(), StringComparison.Ordinal);
    }
#endif

    private static Exception StartupFailure(ProjectWebApplicationFactory factory, Action<IWebHostBuilder> configure)
    {
        using var host = factory.WithWebHostBuilder(configure);
        return Assert.ThrowsAny<Exception>(() => host.CreateClient());
    }
}
