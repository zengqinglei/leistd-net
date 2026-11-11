import { InjectionToken, Signal, signal } from '@angular/core';

/** 设置面板里分区标题的级别：比面板标题低一级。 */
export type SettingsSectionHeadingLevel = 3 | 4;

/**
 * 设置面板里分区标题的级别，由外壳按自己的标题层级提供：页面标题 h2 时面板 h3、分区 h4（默认）；
 * 页面标题 h1 时（例如嵌在别的页面里预览）面板 h2、分区 h3。分区输出真实的 h3/h4 标签，不写死级别。
 */
export const SETTINGS_SECTION_HEADING_LEVEL = new InjectionToken<
  Signal<SettingsSectionHeadingLevel>
>('SETTINGS_SECTION_HEADING_LEVEL', {
  providedIn: 'root',
  factory: () => signal<SettingsSectionHeadingLevel>(4).asReadonly(),
});
