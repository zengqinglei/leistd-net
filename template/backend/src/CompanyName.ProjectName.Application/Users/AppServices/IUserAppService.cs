using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Users.Dtos;
using Leistd.Ddd.Application.Contracts.AppServices;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Data.Paging;

namespace CompanyName.ProjectName.Application.Users.AppServices;

/// <summary>
/// 用户应用服务接口
/// </summary>
public interface IUserAppService : IAppService
{
    /// <summary>
    /// 获取用户列表（分页）
    /// </summary>
    Task<PagedResult<UserManagementOutputDto>> GetPagedListAsync(
        GetUserPagedInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户详情
    /// </summary>
    Task<UserManagementOutputDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建用户
    /// </summary>
    Task<UserManagementOutputDto> CreateAsync(
        CreateUserInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新用户
    /// </summary>
    Task<UserManagementOutputDto> UpdateAsync(
        Guid id,
        UpdateUserInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 启用用户
    /// </summary>
    Task EnableAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 禁用用户
    /// </summary>
    Task DisableAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重置用户密码，并撤销该用户的全部会话（重置的是自己时保留当前会话）
    /// </summary>
#if (LocalIdentity)
    Task ResetPasswordAsync(Guid id, ResetUserPasswordInputDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解除用户的登录锁定（登录失败触发的临时锁定，或管理员锁定），并清零失败计数
    /// </summary>
    Task UnlockAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重置用户的两步验证（停用并清掉密钥与恢复码），该用户的会话全部失效
    /// </summary>
    Task ResetTwoFactorAsync(Guid id, CancellationToken cancellationToken = default);
#endif

    /// <summary>
    /// 删除用户（软删除）
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询用户当前角色
    /// </summary>
    Task<IReadOnlyList<RoleBriefDto>> GetRolesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取用户上传的头像图片；用户不存在、没有头像或头像是外部地址时为 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 不要求用户管理权限：头像随用户名出现在各处（操作记录、成员列表、顶栏），已登录即可看；
    /// 查询仍受租户过滤器约束，看不到别的租户的用户。
    /// </remarks>
    Task<UserAvatarOutputDto?> GetAvatarAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 替换用户角色。需要 App.Users.ManageRoles。
    /// </summary>
    Task<IReadOnlyList<RoleBriefDto>> ReplaceRolesAsync(
        Guid id,
        UpdateUserRolesInputDto input,
        CancellationToken cancellationToken = default);
}
