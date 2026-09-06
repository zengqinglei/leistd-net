#if (LocalIdentity)
using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Auth.Dtos;

/// <summary>
/// 外部登录回调请求
/// </summary>
public record ExternalLoginCallbackInputDto
{
    /// <summary>
    /// 授权码
    /// </summary>
    [Required(ErrorMessage = "Authorization code is required.")]
    [StringLength(500, ErrorMessage = "Authorization code cannot exceed 500 characters.")]
    public required string Code { get; init; }

    /// <summary>
    /// 状态参数
    /// </summary>
    [Required(ErrorMessage = "State is required.")]
    [StringLength(100, ErrorMessage = "State cannot exceed 100 characters.")]
    public required string State { get; init; }
}
#endif
