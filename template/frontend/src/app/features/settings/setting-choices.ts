import { inject, InjectionToken, InputSignal, Signal, Type } from '@angular/core';

import {
  AccountPreferencesService,
  PreferenceWriteOutcome,
} from '../../core/services/account-preferences-service';
//#if (IncludeLocalization)
import { LANG_OPTIONS, SUPPORTED_LANGS } from '../../core/services/language-service';
//#endif
import { THEME_MODES } from '../../core/services/theme-service';
import { SETTINGS } from '../../core/settings/setting.constants';
import { SettingOutputDto } from '../../core/settings/setting.dto';
import { AccountPreferenceField } from '../../shared/dtos/account-preferences.dto';

/*
 * 设置页的声明表：项目在设置页上的差异只写在这个文件里，通用的设置分区组件不出现任何设置名。
 * 值域、层级与显示名来自后端设置定义；这里只补后端定义不承载的界面信息。没有登记的设置按通用规则渲染：
 * 布尔为开关，带区间为数字框，带候选值（`allowedValues`）为下拉，其余为文本框。
 */

/** 候选项文案：词条前缀（取 `<前缀>.<值>`，缺词条时显示原值），或直接给出文案的函数。 */
export type SettingValueLabel = string | ((value: string) => string);

/**
 * 候选项的文案。候选值只认后端下发的 `allowedValues`，这里不再列一份值域；没有登记的设置直接显示原值
 * （日志级别不翻译：翻译后对不上配置与日志里实际出现的词）。
 */
export const SETTING_VALUE_LABELS: Readonly<Partial<Record<string, SettingValueLabel>>> = {
  //#if (IncludeLocalization)
  // 各语种用自己的本地名，不随界面语言变化，与页头的语言切换同一份清单
  [SETTINGS.display.language]: (value) =>
    LANG_OPTIONS.find((option) => option.id === value)?.label ?? value,
  //#endif
  // 与页头的主题切换同一组词条，两处写法一致
  [SETTINGS.display.theme]: 'theme.mode',
};

/** 按当前取值显示的说明（词条前缀，取 `<前缀>.<值>`）：本层的值或继承来的值都适用。级别名说不出会多打多少日志。 */
export const SETTING_VALUE_DESCRIPTIONS: Readonly<Partial<Record<string, string>>> = {
  [SETTINGS.logging.minimumLevel]: 'settings.logLevelHints',
};

/**
 * 显示名说不清写法或效果的设置，在标签下方给一句固定说明（词条键）。后端设置定义不承载界面说明，
 * 没有登记的设置没有说明。
 */
export const SETTING_DESCRIPTIONS: Readonly<Partial<Record<string, string>>> = {
  [SETTINGS.telemetry.exportEnabled]: 'settings.settingHints.telemetryExportEnabled',
  [SETTINGS.telemetry.exportEndpoint]: 'settings.settingHints.telemetryExportEndpoint',
  [SETTINGS.telemetry.exportHeaders]: 'settings.settingHints.telemetryExportHeaders',
};

/**
 * 专用控件：
 * - `timeZone`：可搜索的分组下拉，候选为全量时区（见 {@link timeZoneGroups}）；
 * - `language`：界面语言下拉，只支持一种语言时不出这一行；
 * - `theme`：外观下拉，个人页未设置时显示设备当前的外观；
 * - 组件类：项目自己的控件（如值为 JSON 数组的多选），见 {@link SettingCustomControl}。
 *
 * `language` 与 `timeZone` 由设备兜底：本层未覆盖时说明值从哪层来（跟随组织、部署默认、跟随系统）。
 */
export type SettingControl = 'timeZone' | 'language' | 'theme' | Type<SettingCustomControl>;

/** 设置分区交给自定义控件的一行：值、占位与无效态随分区状态更新。 */
export interface SettingControlContext {
  readonly setting: SettingOutputDto;
  /** 本层的值：正在编辑或等待写入落定时为输入，否则为快照里本层的覆盖值，未覆盖为空串。 */
  readonly value: Signal<string>;
  /** 本层未覆盖时生效的继承值（原值，未登记文案），控件自己决定怎么显示；没有时为空串。 */
  readonly inherited: Signal<string>;
  readonly invalid: Signal<boolean>;
  /** 控件可聚焦元素的 id：本行标签的 `for` 指向它。 */
  readonly controlId: string;
  /** 本行描述与错误的 id，控件放进 `aria-describedby`。 */
  readonly describedBy: Signal<string>;
  /** 提交本层的值，`null` 清除覆盖；与内置控件一样即改即存、排队写入。 */
  commit(value: string | null): void;
}

/**
 * 自定义控件：只渲染控件本身，标签、说明、写入状态、字段错误与恢复按钮仍由设置分区渲染。值的形状与校验
 * 由项目约定（框架的 `allowedValues` 是整值候选，按元素多选不能登记它），候选项由控件自己给出。
 */
export interface SettingCustomControl {
  readonly context: InputSignal<SettingControlContext>;
}

/** 用专用控件渲染的设置；没有登记的按通用规则渲染。 */
export const SETTING_CONTROLS: Readonly<Partial<Record<string, SettingControl>>> = {
  //#if (IncludeLocalization)
  [SETTINGS.display.language]: 'language',
  //#endif
  [SETTINGS.display.timeZone]: 'timeZone',
  [SETTINGS.display.theme]: 'theme',
};

/** 显式依赖：上级的取值满足 `shownWhen` 时，子设置缩进显示在上级之下。 */
export interface SettingDependency {
  /** 上级设置名。 */
  parent: string;
  /** 按上级的取值（本层的值，没有时为继承值）判断子设置是否显示。 */
  shownWhen: (parentValue: string) => boolean;
}

/**
 * 设置之间的显式依赖（子设置名 → 依赖），补充命名约定表达不了的情形，例如某个枚举取值不为 `off` 时才显示：
 * `'Reports.Archive.Days': { parent: 'Reports.Mode', shownWhen: (mode) => mode !== 'off' }`。
 *
 * 命名约定不用登记：`{前缀}.Enabled` 开关下自动挂同前缀的其他设置，开关为真时显示。同一子设置两种都命中时以这里为准。
 * 上级不在当前页面上（被分组或权限过滤掉）时子设置照常显示在顶层；收起只是不显示，值不清空。
 * 依赖不能成环：组件不做环检测，成环的设置不会出现。
 */
export const SETTING_DEPENDENCIES: Readonly<Partial<Record<string, SettingDependency>>> = {};

/** 个人页账户偏好行所在的分组标识（与后端设置定义的分组相同），偏好暂时读不到时这一组照常出现并说明原因。 */
export const ACCOUNT_PREFERENCE_GROUP = 'Display';

/** 个人页的一行账户偏好：偏好字段、同名设置，以及候选值（偏好不取设置快照，候选值在这里给出）。 */
export interface AccountPreferenceSetting {
  field: AccountPreferenceField;
  name: string;
  allowedValues?: readonly string[];
}

/**
 * 个人页的账户偏好行，按显示顺序：先影响能否读懂的语言，再影响数据解读的时区，最后外观。
 * 候选值与后端定义的 `allowedValues` 相同，取自前端自己的语言与主题清单（i18n 闸门核对语言清单两端一致）。
 */
export const ACCOUNT_PREFERENCE_SETTINGS: readonly AccountPreferenceSetting[] = [
  //#if (IncludeLocalization)
  { field: 'language', name: SETTINGS.display.language, allowedValues: SUPPORTED_LANGS },
  //#endif
  { field: 'timeZone', name: SETTINGS.display.timeZone },
  { field: 'theme', name: SETTINGS.display.theme, allowedValues: THEME_MODES },
];

/** 偏好行的读写来源：生效值、个人覆盖、是否已读到，以及单字段写入（结果见 `PreferenceWriteOutcome`）。 */
export interface AccountPreferenceSource {
  readonly preferences: Signal<Readonly<Record<AccountPreferenceField, string | null>> | null>;
  readonly overrides: Signal<Readonly<Record<AccountPreferenceField, string | null>> | null>;
  readonly known: Signal<boolean>;
  write(field: AccountPreferenceField, value: string | null): Promise<PreferenceWriteOutcome>;
}

/**
 * 偏好行的读写来源。{@link ACCOUNT_PREFERENCE_SETTINGS} 为空时设置分区不取用它，也不出偏好分组；
 * 偏好由别的服务持有、接口不同的项目在这里接适配器。
 */
export const ACCOUNT_PREFERENCE_SOURCE = new InjectionToken<AccountPreferenceSource>(
  'ACCOUNT_PREFERENCE_SOURCE',
  { providedIn: 'root', factory: () => inject(AccountPreferencesService) },
);

/** 时区选项：一个分组里的一行。 */
export interface TimeZoneOption {
  /** IANA 名，也是存进设置的值。 */
  value: string;
  /** 去掉地区前缀后的地名，下划线换成空格。 */
  city: string;
  /** 此刻的 UTC 偏移文案，如 `UTC+8`。 */
  offsetLabel: string;
  /** 同一偏移的 `GMT+8` 写法，只用于搜索匹配。 */
  offsetAlias: string;
  isBrowser: boolean;
  /** 搜索时额外匹配的名字（见 {@link SEARCH_ALIASES}）。 */
  aliases: readonly string[];
}

/** 按 IANA 名的地区前缀分组。 */
export interface TimeZoneGroup {
  region: string;
  zones: readonly TimeZoneOption[];
}

/** 已改名时区的搜索别名（如规范名 `Asia/Calcutta` 可搜 Kolkata）；只影响匹配，不另造选项。 */
const SEARCH_ALIASES: Record<string, readonly string[]> = {
  'Asia/Calcutta': ['Kolkata'],
  'Asia/Kolkata': ['Calcutta'],
  'Asia/Saigon': ['Ho Chi Minh'],
  'Asia/Ho_Chi_Minh': ['Saigon'],
  'Europe/Kiev': ['Kyiv'],
  'Europe/Kyiv': ['Kiev'],
  'Asia/Rangoon': ['Yangon'],
  'Asia/Yangon': ['Rangoon'],
  'America/Buenos_Aires': ['Argentina'],
};

/**
 * 该时区在当前运行时能否渲染，能则返回规范名。刻意构造一次 formatter 判定，不查
 * `Intl.supportedValuesOf('timeZone')`：它不含 `UTC`、`Asia/Kolkata` 等可用值。
 */
function canonicalTimeZone(timeZone: string): string | undefined {
  try {
    return new Intl.DateTimeFormat('en', { timeZone }).resolvedOptions().timeZone;
  } catch {
    return undefined;
  }
}

/**
 * 该时区此刻的偏移文案（`GMT+8` 改写为 `UTC+8`，两种写法都参与搜索）；零偏移写成 `UTC+0`，
 * 与其他偏移同一形态，也不会在 `UTC` 时区上写出 `UTC · UTC`。取不到返回空串。
 */
function offsetLabel(timeZone: string): { label: string; alias: string } {
  try {
    const parts = new Intl.DateTimeFormat('en-US', {
      timeZone,
      timeZoneName: 'shortOffset',
    }).formatToParts(new Date());
    const alias = parts.find((p) => p.type === 'timeZoneName')?.value ?? '';
    const label = alias === 'GMT' ? 'UTC+0' : alias.replace(/^GMT/, 'UTC');
    return { label, alias };
  } catch {
    return { label: '', alias: '' };
  }
}

/** 该时区此刻的 UTC 偏移文案，如 `UTC+8`；不是有效时区（如"跟随系统"这类占位文案）时返回空串。 */
export function timeZoneOffsetLabel(timeZone: string): string {
  return offsetLabel(timeZone).label;
}

/** 该时区此刻的 UTC 偏移分钟数，用于组内排序；取不到按 0 处理。 */
function offsetMinutes(timeZone: string): number {
  try {
    const now = new Date();
    // 同一时刻在目标时区与 UTC 下的"墙上时间"之差即为偏移
    const local = new Date(now.toLocaleString('en-US', { timeZone }));
    const utc = new Date(now.toLocaleString('en-US', { timeZone: 'UTC' }));
    return Math.round((local.getTime() - utc.getTime()) / 60_000);
  } catch {
    return 0;
  }
}

/** 浏览器所在时区（IANA 名）。 */
export function browserTimeZone(): string | undefined {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || undefined;
  } catch {
    return undefined;
  }
}

/**
 * 全量时区，按地区分组：可搜索后全量不再是负担，精选清单必然漏掉部署地。额外补上 `UTC` 与
 * 浏览器时区，并按规范名去重。
 */
export function timeZoneGroups(): readonly TimeZoneGroup[] {
  const browser = browserTimeZone();
  const candidates: string[] = [];
  try {
    candidates.push(...Intl.supportedValuesOf('timeZone'));
  } catch {
    // 运行时没有这个 API：至少还能给出 UTC 与浏览器时区
  }

  // UTC 与浏览器时区都可能不在规范名清单里，显式补上；顺序在后，
  // 去重时会让规范名优先成为展示用的那一个。
  candidates.push('UTC');
  if (browser) {
    candidates.push(browser);
  }

  const chosen = new Map<string, string>();
  for (const id of candidates) {
    const canonical = canonicalTimeZone(id);
    if (canonical && !chosen.has(canonical)) {
      chosen.set(canonical, id);
    }
  }

  const browserCanonical = browser ? canonicalTimeZone(browser) : undefined;
  // 偏移先算好再排序：offsetMinutes 每次构造两个 formatter，放进比较器会乘上比较次数。
  const groups = new Map<string, { option: TimeZoneOption; offset: number }[]>();

  for (const [canonical, id] of chosen) {
    const separator = id.indexOf('/');
    // 没有地区前缀的（UTC、GMT 之类）单独归到一组，而不是塞进某个大洲
    const region = separator < 0 ? 'UTC' : id.slice(0, separator);
    const city = separator < 0 ? id : id.slice(separator + 1).replaceAll('_', ' ');

    const offset = offsetLabel(id);
    const zones = groups.get(region) ?? [];
    zones.push({
      option: {
        value: id,
        city,
        offsetLabel: offset.label,
        offsetAlias: offset.alias,
        isBrowser: canonical === browserCanonical,
        aliases: SEARCH_ALIASES[id] ?? [],
      },
      offset: offsetMinutes(id),
    });
    groups.set(region, zones);
  }

  return [...groups.entries()]
    .map(([region, zones]) => ({
      region,
      // 组内按偏移排：找时区多半是"我在 UTC+8"，同偏移再按地名稳定排序
      zones: zones
        .sort((a, b) => a.offset - b.offset || a.option.city.localeCompare(b.option.city))
        .map((entry) => entry.option),
    }))
    .sort((a, b) => a.region.localeCompare(b.region));
}

/** 搜索匹配：地名、完整 IANA 名、偏移（`UTC+8` 与 `GMT+8`）与别名都算命中。 */
export function timeZoneMatches(option: TimeZoneOption, search: string): boolean {
  const needle = search.trim().toLowerCase();
  if (needle.length === 0) {
    return true;
  }

  const haystack = [
    option.city,
    option.value,
    option.offsetLabel,
    option.offsetAlias,
    ...option.aliases,
  ];
  return haystack.some((text) => text.toLowerCase().includes(needle));
}
