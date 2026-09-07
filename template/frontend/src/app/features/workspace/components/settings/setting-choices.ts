//#if (IncludeLocalization)
import { LANG_OPTIONS } from '../../../../core/services/language-service';
import { SETTINGS } from '../../../../core/settings/setting.constants';

//#endif
/** 一个可选值及其展示文案。 */
export interface SettingChoice {
  value: string;
  label: string;
}

/**
 * 取值封闭的设置的候选项。
 *
 * 候选项留在设置页面而不是后端，与 spartan 官方设置页同一取舍：后端设置定义只管
 * 「名称 / 默认值 / 可写层级」，不承载控件元数据——把控件类型塞进设置定义，
 * 等于让每加一种控件就要改一次后端契约。
 *
 * 未列在此的设置渲染成文本框。
 */
//#if (IncludeLocalization)
export const SETTING_CHOICES: Readonly<Record<string, readonly SettingChoice[]>> = {
  // 直接取语言服务的清单：候选值就是应用真正支持的语言，另抄一份必然漂移。
  // 文案用语言自身的本地名，因此不随界面语言变化，也不需要词条。
  [SETTINGS.display.language]: LANG_OPTIONS.map((o) => ({ value: o.id, label: o.label })),
};
//#else
// 目前没有取值封闭的设置。新增枚举型设置时在这里登记候选项，设置页会自动渲染成下拉。
export const SETTING_CHOICES: Readonly<Record<string, readonly SettingChoice[]>> = {};
//#endif
