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

/** 用户输出；`User` 领域模型由它构造（见 `user.model.ts`）。 */
export interface UserOutputDto {
  id: string;
  username: string;
  email: string;
  displayName?: string;
  avatar?: string;
  phoneNumber?: string;
  isActive: boolean;
  isSuperAdmin: boolean;
  creationTime: string;
  roles: string[];
}
