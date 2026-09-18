/**
 * 设置的 Mock 数据。
 *
 * 覆盖值按主体键控，键的形状对齐真实 Store 的 `ScopeKey`——**用户级也带租户**，
 * 因为设置行本身带租户归属。共用一份全局值会让切换 Mock 用户或租户后看到上一个主体的
 * 偏好，把「层级隔离」这条最该被验证的语义演成假的。
 */
export interface MockSettingDefinition {
  name: string;
  displayName: string;
  /** 分组标识；设置页左侧按它分类。真实后端会把未分组的归入 `Other`。 */
  group: string;
  defaultValue: string | null;
  allowsTenantScope: boolean;
  allowsUserScope: boolean;
  /** 进程级设置：只有宿主那一份，租户与用户都不能覆盖。 */
  allowsHostScope?: boolean;
  /** 数值型设置的取值区间；界面据此渲染带上下界的数字输入框。 */
  minimum?: number;
  maximum?: number;
  /** 布尔型设置，渲染成开关（与后端 SettingConstant.BooleanSettings 对应）。 */
  isBoolean?: boolean;
  /** 机密设置：值不下发，只报告是否设过。 */
  isSecret?: boolean;
}

export const SETTING_DEFINITIONS: MockSettingDefinition[] = [
  //#if (IncludeLocalization)
  {
    name: 'Display.Language',
    displayName: '界面语言',
    group: 'Display',
    // 留空即"跟随系统"：客户端按浏览器语言渲染，租户没有默认值可给
    defaultValue: null,
    allowsTenantScope: false,
    allowsUserScope: true,
  },
  //#endif
  {
    name: 'Display.TimeZone',
    displayName: '时区',
    group: 'Display',
    defaultValue: null,
    allowsTenantScope: false,
    allowsUserScope: true,
  },
  // 进程级：两个层级标记都是 false，只有 allowsHostScope。真实后端在租户上下文下
  // 根本不下发这类设置，Mock 只有宿主视角，因此照常列出。
  {
    name: 'Logging.MinimumLevel',
    displayName: '最小日志级别',
    group: 'Operations',
    defaultValue: 'Information',
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
  },
  {
    name: 'Logging.RequestLevel',
    displayName: '请求日志级别',
    group: 'Operations',
    defaultValue: 'Information',
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
  },
  {
    name: 'Audit.RetentionEnabled',
    displayName: '到期操作记录搬入归档',
    group: 'Audit',
    defaultValue: 'false',
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
    isBoolean: true,
  },
  {
    name: 'Audit.RetentionDays',
    displayName: '操作记录保留天数',
    group: 'Audit',
    defaultValue: '365',
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
    minimum: 30,
    maximum: 3650,
  },
  //#if (LocalIdentity)
  {
    name: 'Registration.EnableEmailVerification',
    displayName: '要求邮箱验证',
    group: 'Registration',
    defaultValue: 'false',
    allowsTenantScope: true,
    allowsUserScope: false,
    isBoolean: true,
  },
  {
    name: 'Registration.CaptchaExpiryMinutes',
    displayName: '图形验证码有效期（分钟）',
    group: 'Registration',
    defaultValue: '5',
    allowsTenantScope: true,
    allowsUserScope: false,
    minimum: 1,
    maximum: 60,
  },
  {
    name: 'Security.LockoutMaxFailedAttempts',
    displayName: '连续登录失败多少次后锁定（0 为不锁定）',
    group: 'Security',
    defaultValue: '5',
    allowsTenantScope: true,
    allowsUserScope: false,
    minimum: 0,
    maximum: 100,
  },
  {
    name: 'Security.LockoutDurationMinutes',
    displayName: '锁定时长（分钟）',
    group: 'Security',
    defaultValue: '15',
    allowsTenantScope: true,
    allowsUserScope: false,
    minimum: 1,
    maximum: 1440,
  },
  {
    name: 'Security.RequireTwoFactor',
    displayName: '要求所有人启用两步验证',
    group: 'Security',
    defaultValue: 'false',
    allowsTenantScope: true,
    allowsUserScope: false,
    isBoolean: true,
  },
  {
    name: 'Email.SmtpHost',
    displayName: 'SMTP 主机',
    group: 'Email',
    defaultValue: 'localhost',
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
  },
  {
    name: 'Email.SmtpPort',
    displayName: 'SMTP 端口',
    group: 'Email',
    defaultValue: '1025',
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
    minimum: 1,
    maximum: 65535,
  },
  {
    name: 'Email.SmtpEnableSsl',
    displayName: '启用 TLS 加密',
    group: 'Email',
    defaultValue: 'false',
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
    isBoolean: true,
  },
  {
    name: 'Email.SmtpUsername',
    displayName: 'SMTP 用户名',
    group: 'Email',
    defaultValue: null,
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
  },
  {
    name: 'Email.SmtpPassword',
    displayName: 'SMTP 口令',
    group: 'Email',
    defaultValue: null,
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
    isSecret: true,
  },
  {
    name: 'Email.DefaultFromAddress',
    displayName: '发件地址',
    group: 'Email',
    defaultValue: 'noreply@example.com',
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
  },
  {
    name: 'Email.DefaultFromName',
    displayName: '发件人名称',
    group: 'Email',
    defaultValue: 'Template Project',
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
  },
  //#if (IncludeNotifications)
  {
    name: 'Notifications.Security.Email',
    displayName: '安全提醒同时发邮件',
    group: 'Notifications',
    defaultValue: 'true',
    allowsTenantScope: false,
    allowsUserScope: true,
    isBoolean: true,
  },
  {
    name: 'Notifications.System.InApp',
    displayName: '在站内接收系统通知',
    group: 'Notifications',
    defaultValue: 'true',
    allowsTenantScope: false,
    allowsUserScope: true,
    isBoolean: true,
  },
  {
    name: 'Notifications.System.Email',
    displayName: '系统通知同时发邮件',
    group: 'Notifications',
    defaultValue: 'false',
    allowsTenantScope: false,
    allowsUserScope: true,
    isBoolean: true,
  },
  //#endif
  //#endif
];

/**
 * 用户级覆盖：`${tenantKey}:${subjectId}:${settingName}` → 值；宿主用 `host`。
 *
 * 键里的是**主体标识**（有本地身份时即会话用户 id，Resource 形态下是令牌的原始 `sub`），
 * 不是界面借用的那个 Mock persona——用 persona 做键会让同租户下的两个真实用户串数据。
 */
export const USER_SETTING_VALUES = new Map<string, string>();

/** 租户级覆盖：`${tenantKey}:${settingName}` → 值；宿主用 `host`。 */
export const TENANT_SETTING_VALUES = new Map<string, string>();
