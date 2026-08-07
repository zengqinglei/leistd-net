using System.ComponentModel.DataAnnotations;
using Leistd.Ddd.Application.Contracts.Dtos;

namespace CompanyName.ProjectName.Application.Users.Dtos;

/// <summary>
/// 获取用户分页列表输入 DTO
/// </summary>
public record GetUserPagedInputDto : PagedRequestDto
{
    /// <summary>
    /// 搜索关键字（用户名、邮箱、显示名称）
    /// </summary>
    [Display(Name = "Search keyword")]
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Keyword { get; init; }

    /// <summary>
    /// 是否启用
    /// </summary>
    [Display(Name = "Active status")]
    public bool? IsActive { get; init; }

    /// <summary>
    /// 邮箱是否已验证
    /// </summary>
    [Display(Name = "Email verification status")]
    public bool? IsEmailVerified { get; init; }

#if (IncludeRoles)
    /// <summary>
    /// 角色名称（多选，命中任一角色即匹配）。单项长度上限与 Role 实体一致（64），在应用服务中校验。
    /// </summary>
    [Display(Name = "Roles")]
    [MaxLength(20, ErrorMessage = "{0} cannot contain more than {1} items.")]
    public List<string>? Roles { get; init; }
#endif
}
