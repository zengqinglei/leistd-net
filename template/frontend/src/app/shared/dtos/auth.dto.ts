/**
 * 认证契约 DTO。
 *
 * 放在 `shared/` 而不是 `features/account/`：`core/services/auth-service.ts` 是应用级单例，
 * 它要用这些类型；DTO 留在特性目录会让 `core` 反向依赖 `features`，改动或移除 account
 * 特性就会连带打断 core。它们同时被 core、account 特性与 Mock 消费。
 */

/** 登录请求。 */
export interface LoginInputDto {
  usernameOrEmail: string;
  password: string;
}

/** 登录第一步（密码或外部登录）的结果。 */
export interface SessionLoginOutputDto {
  /** 还需要第二步：凭 twoFactorToken 提交验证码或恢复码后才下发会话。 */
  requiresTwoFactor?: boolean;
  twoFactorToken?: string | null;
}

/** 用户输出；`User` 领域模型由它构造（见 `user.model.ts`）。 */
export interface UserOutputDto {
  id: string;
  username: string;
  email: string;
  displayName?: string;
  /** 头像地址：上传的图片是带版本号的站内地址（`/api/v1/users/{id}/avatar?v=…`），外部头像是原地址。 */
  avatar?: string;
  phoneNumber?: string;
  isActive: boolean;
  /** 当前邮箱是否已验证；改过邮箱后回到未验证。 */
  isEmailVerified: boolean;
  isTwoFactorEnabled?: boolean;
  /** 受限会话：所在租户要求两步验证而本人尚未启用，必须先完成设置。 */
  twoFactorSetupRequired?: boolean;
  isSuperAdmin: boolean;
  creationTime: string;
  roles: string[];
}
