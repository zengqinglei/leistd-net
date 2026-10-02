/**
 * 前端需要按值分支的后端错误码。
 *
 * 必须与后端的 `AuthErrorCodes` / `SecurityErrorCodes` 逐字一致。
 * 这里只收**前端真的要据此改变行为**的那几个；只用于展示的码不进来——
 * 展示走服务端下发的 `detail`，多列一个就多一处要同步的地方。
 */
export const API_ERROR_CODES = {
  /**
   * 连续认证失败触发的临时锁定。
   *
   * 它与「会话失效」共用 401，但含义相反：**会话仍然有效**，只是这次操作被拒。
   * 拦截器必须据此放行，否则会把人踢到一个他此刻进不去的登录页（见 http-error-interceptor）。
   */
  userTemporarilyLockedOut: 'Auth:UserTemporarilyLockedOut',

  /** 组织要求两步验证而本人尚未启用：带去设置页。 */
  twoFactorSetupRequired: 'Auth:TwoFactorSetupRequired',

  /** 两步验证码不正确：停在当前页重试，不退出挑战流程。 */
  twoFactorCodeInvalid: 'Auth:TwoFactorCodeInvalid',
} as const;
