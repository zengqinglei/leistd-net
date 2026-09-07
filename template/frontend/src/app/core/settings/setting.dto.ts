/** 一项设置在各层级上的覆盖值及其可覆盖层级。 */
export interface SettingOutputDto {
  /** 设置名称，跨前后端的字符串契约。 */
  name: string;
  /** 已按请求 culture 翻译的显示名称。 */
  displayName: string;
  /** 当前用户的个人覆盖值；`null` 表示未覆盖，继承租户默认值或代码默认值。 */
  userValue: string | null;
  /** 当前租户的默认覆盖值；`null` 表示未覆盖，继承代码默认值。 */
  tenantValue: string | null;
  /** 代码默认值。 */
  defaultValue: string | null;
  /** 是否允许写入租户级默认值。 */
  allowsTenantScope: boolean;
  /** 是否允许用户覆盖为个人偏好。 */
  allowsUserScope: boolean;
}

/** 写入一项设置；`value` 为 null 表示清除该层级，回落到下一层。 */
export interface SetSettingInputDto {
  name: string;
  value: string | null;
}
