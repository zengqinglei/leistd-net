namespace CompanyName.ProjectName.Application.Auth.Dtos;

public record SecurityConfigOutputDto
{
    public bool EnableEmailVerification { get; init; }

    /// <summary>
    /// 部署具备发邮箱验证码的前提（验证码摘要密钥已配置）。
    /// </summary>
    /// <remarks>
    /// 与上面的开关是两回事：那个管"注册时要不要验证"，这个管"能不能发验证码"。
    /// 个人资料里的"验证我的邮箱"据此决定是给出发送按钮，还是只说明暂不可用——
    /// 不给这一项的话，用户点下去得到的是一个 500。
    /// </remarks>
    public bool EmailVerificationAvailable { get; init; }
}
