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
    group: 'Logging',
    defaultValue: 'Information',
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
  },
  {
    name: 'Logging.RequestLevel',
    displayName: '请求日志级别',
    group: 'Logging',
    defaultValue: 'Information',
    allowsTenantScope: false,
    allowsUserScope: false,
    allowsHostScope: true,
  },
  //#if (LocalIdentity)
  {
    name: 'Registration.EnableEmailVerification',
    displayName: '要求邮箱验证',
    group: 'Registration',
    defaultValue: 'false',
    allowsTenantScope: true,
    allowsUserScope: false,
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
