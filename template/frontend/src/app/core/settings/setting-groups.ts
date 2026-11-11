import { SettingOutputDto } from './setting.dto';

/*
 * 设置列表的纯判据：这一层能编辑哪些设置、怎么切成分组、分组在 URL 上怎么写。放在 core 而不是设置页里：
 * 侧栏这类 layout 组件也要用同一判据决定入口，layout 不能依赖 features；判据只有一份，入口与面板才不会对不上。
 */

/** 设置作用域：`account` 写当前用户偏好；`system` 写当前租户（或宿主）下所有人的默认值，写入授权不同。 */
export type SettingScope = 'account' | 'system';

/** 一个分组及归到它下面的设置项；分组标识、顺序与显示名都来自后端。 */
export interface SettingGroup {
  key: string;
  label: string;
  settings: SettingOutputDto[];
}

/** 本作用域下能看到的设置。 */
export function settingsInScope(
  settings: readonly SettingOutputDto[],
  scope: SettingScope,
): SettingOutputDto[] {
  // 系统页：允许租户级或进程级（后者两个层级标记都是 false，只看 allowsTenantScope 会漏掉），
  // 且当前用户有该分组的管理权限；正确性以服务端算出的 canManage 为准
  return scope === 'account'
    ? settings.filter((s) => s.allowsUserScope)
    : settings.filter((s) => (s.allowsTenantScope || s.allowsHostScope) && s.canManage);
}

/** 按分组切分，分组标识与顺序来自后端（未分组的已归入 `Other`），前端不另列清单。 */
export function groupSettings(settings: readonly SettingOutputDto[]): SettingGroup[] {
  const byKey = new Map<string, SettingGroup>();
  for (const setting of settings) {
    const group = byKey.get(setting.group) ?? {
      key: setting.group,
      label: setting.groupDisplayName || setting.group,
      settings: [],
    };
    group.settings.push(setting);
    byKey.set(setting.group, group);
  }

  return [...byKey.values()];
}

/** 分组标识在 URL 里的写法：`RegistrationSecurity` → `registration-security`。 */
export function groupPath(key: string): string {
  return key.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();
}
