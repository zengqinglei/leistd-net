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
  defaultValue: string | null;
  allowsTenantScope: boolean;
  allowsUserScope: boolean;
}

export const SETTING_DEFINITIONS: MockSettingDefinition[] = [
  //#if (IncludeLocalization)
  {
    name: 'Display.Language',
    displayName: '界面语言',
    defaultValue: 'en',
    allowsTenantScope: true,
    allowsUserScope: true,
  },
  //#endif
  {
    name: 'Display.TimeZone',
    displayName: '时区',
    defaultValue: 'Asia/Shanghai',
    allowsTenantScope: true,
    allowsUserScope: true,
  },
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
