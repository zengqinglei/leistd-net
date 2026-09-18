/**
 * 用户注册请求 DTO
 */
export interface RegisterInputDto {
  username: string;
  email: string;
  password: string;
  captchaCode?: string;
  captchaToken?: string;
  emailVerification?: EmailVerificationInputDto;
  displayName?: string;
}

/**
 * 注册安全配置输出 DTO
 */
/** 登录第二步：验证码与恢复码二选一。 */
export interface TwoFactorLoginInputDto {
  token: string;
  code?: string;
  recoveryCode?: string;
}

export interface TwoFactorStatusOutputDto {
  enabled: boolean;
  recoveryCodesLeft: number;
  /** 所在租户要求两步验证（此时不能停用）。 */
  requiredByPolicy: boolean;
}

export interface TwoFactorSetupOutputDto {
  /** Base32 密钥，供无法扫码时手动输入。 */
  secret: string;
  /** otpauth:// 地址，二维码的内容。 */
  otpAuthUri: string;
}

export interface TwoFactorRecoveryCodesOutputDto {
  recoveryCodes: string[];
}

export interface DisableTwoFactorInputDto {
  password: string;
  code: string;
}

/** 一个登录中的会话（登录设备）。 */
export interface UserSessionOutputDto {
  id: string;
  creationTime: string;
  /** 最近活跃时间，按分钟节流更新。 */
  lastSeenTime: string;
  ipAddress?: string | null;
  /** User-Agent 原文，界面归纳成"浏览器 · 系统"。 */
  userAgent?: string | null;
  /** 模拟登录建立的会话：发起人名称。 */
  impersonatorName?: string | null;
  isCurrent: boolean;
}

export interface SecurityConfigOutputDto {
  enableEmailVerification: boolean;
  /** 部署具备发邮箱验证码的前提；为 false 时"验证我的邮箱"只说明暂不可用，不给发送按钮。 */
  emailVerificationAvailable: boolean;
}

/**
 * 图形验证码输出 DTO
 */
export interface CaptchaOutputDto {
  captchaToken: string;
  captchaImageBase64: string;
}

/**
 * 发送邮件验证码请求 DTO
 */
export interface SendEmailCodeInputDto {
  email: string;
  captchaToken: string;
  captchaCode: string;
}

/**
 * 邮箱验证挑战应答 DTO
 */
export interface EmailVerificationInputDto {
  challengeId: string;
  code: string;
}

/**
 * 邮箱验证挑战输出 DTO
 */
export interface EmailVerificationChallengeOutputDto {
  challengeId: string;
  expiresInSeconds: number;
  retryAfterSeconds: number;
}

/**
 * 当前用户资料更新请求 DTO
 */
export interface UpdateCurrentUserInputDto {
  username: string;
  email: string;
  displayName?: string;
  phoneNumber?: string;
}

/** 设置自己的头像：图片的 data URL（浏览器端已裁剪缩放）；`null` 清除。 */
export interface SetAvatarInputDto {
  avatar: string | null;
}

/**
 * 修改密码请求 DTO
 */
export interface ChangePasswordInputDto {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}
//#if (ExternalLogin)

/**
 * 外部登录 URL 输出 DTO
 */
export interface ExternalLoginUrlOutputDto {
  loginUrl: string;
}

/**
 * 外部登录回调请求 DTO
 */
export interface ExternalLoginCallbackInputDto {
  provider: string;
  code: string;
  state: string;
}

/** 本人的外部账号绑定情况。 */
export interface ExternalLoginsOutputDto {
  /** 是否设有密码；没有时最后一个绑定不能解绑。 */
  hasPassword: boolean;
  /** 部署已配置的提供商，各附带本人的绑定。 */
  providers: ExternalLoginProviderOutputDto[];
}

export interface ExternalLoginProviderOutputDto {
  provider: string;
  /** 本人在该提供商下的绑定；未绑定时缺省。 */
  link?: ExternalLoginLinkOutputDto | null;
}

export interface ExternalLoginLinkOutputDto {
  id: string;
  providerUsername?: string | null;
  providerEmail?: string | null;
  creationTime: string;
}
//#endif
