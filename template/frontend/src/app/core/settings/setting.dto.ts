/** 一项设置在各层级上的覆盖值及其可覆盖层级。 */
export interface SettingOutputDto {
  /** 设置名称，跨前后端的字符串契约。 */
  name: string;
  /** 已按请求 culture 翻译的显示名称。 */
  displayName: string;
  /** 所属分组的稳定标识；界面按它分类，未分组的由后端归入 `Other`。 */
  group: string;
  /** 已按请求 culture 翻译的分组名称。 */
  groupDisplayName: string;
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
  /**
   * 是否是进程级设置（只有宿主那一份，租户与用户都不能覆盖）。
   *
   * 为 `true` 时另两个层级标记都是 `false`，界面据此把它归到系统页而不是账户页。
   * 后端在租户上下文下根本不下发这类设置——改不了也不适用于该租户。
   */
  allowsHostScope: boolean;
  /** 数值型设置的下界；非数值型为 `null`。值域仍以服务端为准，这里只用于渲染合适的控件。 */
  minimum: number | null;
  /** 数值型设置的上界；非数值型为 `null`。 */
  maximum: number | null;
}

/** 写入一项设置；`value` 为 null 表示清除该层级，回落到下一层。 */
export interface SetSettingInputDto {
  name: string;
  value: string | null;
}
