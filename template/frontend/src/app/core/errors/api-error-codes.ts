/**
 * 前端需要按值分支的后端错误码，与后端常量（`AuthErrorCodes`、`SecurityErrorCodes`、
 * `PermissionErrorCodes`）逐字一致；只用于展示的码不收。
 */
export const API_ERROR_CODES = {
  /** 保存权限授予时版本已过期（他人抢先保存）：重新加载，不静默覆盖。 */
  permissionConcurrencyConflict: 'Permission:ConcurrencyConflict',
  //#if (LocalIdentity)

  /** 连续认证失败触发的临时锁定：与会话失效共用 401，但会话仍有效，拦截器须据此放行。 */
  userTemporarilyLockedOut: 'Auth:UserTemporarilyLockedOut',

  /** 组织要求两步验证而本人尚未启用：带去设置页。 */
  twoFactorSetupRequired: 'Auth:TwoFactorSetupRequired',

  /** 两步验证码不正确：停在当前页重试，不退出挑战流程。 */
  twoFactorCodeInvalid: 'Auth:TwoFactorCodeInvalid',
  //#endif
} as const;
