//#if (IncludeLocalization)
import { LANG_OPTIONS } from '../../core/services/language-service';
//#endif
import { SETTINGS } from '../../core/settings/setting.constants';

/** 一个可选值及其展示文案。 */
export interface SettingChoice {
  value: string;
  label: string;
}

/**
 * 取值封闭、且选项不多的设置的候选项。
 *
 * 候选项留在设置页面而不是后端，与 spartan 官方设置页同一取舍：后端设置定义只管
 * 「名称 / 默认值 / 可写层级」，不承载控件元数据——把控件类型塞进设置定义，
 * 等于让每加一种控件就要改一次后端契约。
 *
 * 时区不在这里：它有四百多项，要的是可搜索的分组下拉，见 {@link timeZoneGroups}。
 * 未列在此、也不是时区的设置渲染成文本框。
 */
/**
 * 日志级别候选项。
 *
 * 与后端 `SettingConstant.Logging.Levels`（即 Serilog 的 `LogEventLevel`）逐字一致。
 * 文案不本地化：这些是日志级别的标准名字，翻译过来反而对不上日志里实际出现的词。
 */
const LOG_LEVEL_CHOICES: readonly SettingChoice[] = [
  { value: 'Verbose', label: 'Verbose' },
  { value: 'Debug', label: 'Debug' },
  { value: 'Information', label: 'Information' },
  { value: 'Warning', label: 'Warning' },
  { value: 'Error', label: 'Error' },
  { value: 'Fatal', label: 'Fatal' },
];

/**
 * 真值只有两种的设置：用开关而不是两项下拉。
 *
 * 存的仍是 `'true'` / `'false'` 字符串——设置值在契约上一律是字符串，
 * 控件形态是界面的事，不改契约。
 */
//#if (LocalIdentity)
export const BOOLEAN_SETTINGS: ReadonlySet<string> = new Set<string>([
  SETTINGS.registration.enableEmailVerification,
]);
//#else
// 没有本地身份的形态下不存在注册策略，也就没有布尔型设置。
export const BOOLEAN_SETTINGS: ReadonlySet<string> = new Set<string>();
//#endif

/**
 * 日志级别的说明文案。
 *
 * 选完在下面显示对应说明：级别名本身（Verbose / Debug / …）说不出"选了它会多打多少日志"，
 * 而那恰恰是做这个选择时唯一想知道的事。
 */
export const LOG_LEVEL_DESCRIPTIONS: Readonly<Record<string, string>> = {
  Verbose: '记录一切，含逐条 SQL 与请求细节。仅短时排障用，长期开会迅速吃满磁盘。',
  Debug: '记录调试细节。排查问题时开，日常不建议。',
  Information: '记录正常业务流程。默认级别。',
  Warning: '只记录警告与错误，正常流程不落盘。',
  Error: '只记录错误。会漏掉"没报错但不对"的线索。',
  Fatal: '只记录导致进程终止的故障。几乎等于关掉日志。',
};

export const SETTING_CHOICES: Readonly<Record<string, readonly SettingChoice[]>> = {
  //#if (IncludeLocalization)
  // 直接取语言服务的清单：候选值就是应用真正支持的语言，另抄一份必然漂移。
  // 文案用语言自身的本地名，因此不随界面语言变化，也不需要词条。
  [SETTINGS.display.language]: LANG_OPTIONS.map((o) => ({ value: o.id, label: o.label })),
  //#endif
  [SETTINGS.logging.minimumLevel]: LOG_LEVEL_CHOICES,
  [SETTINGS.logging.requestLevel]: LOG_LEVEL_CHOICES,
};

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
  /** 是否为当前浏览器所在时区。 */
  isBrowser: boolean;
  /** 搜索时额外匹配的名字（见 {@link SEARCH_ALIASES}）。 */
  aliases: readonly string[];
}

/** 按 IANA 名的地区前缀分组。 */
export interface TimeZoneGroup {
  region: string;
  zones: readonly TimeZoneOption[];
}

/**
 * 已被时区数据库改名的时区的**搜索别名**。
 *
 * `Intl.supportedValuesOf('timeZone')` 只返回规范名，印度在多数运行时是 `Asia/Calcutta`，
 * 而人会去搜 Kolkata。这里只影响搜索匹配，不额外造出选项——同一个时区列成两行更糟。
 */
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
 * 该时区在当前运行时能否用于渲染，能则返回它的规范名。
 *
 * **判定方式刻意是"真的构造一次 formatter"，而不是查 `Intl.supportedValuesOf('timeZone')`。**
 * 后者只列规范名：`UTC`、`Asia/Kolkata`、`America/Argentina/Buenos_Aires` 都不在其中，
 * 却都能正常渲染。拿它当合法性清单去过滤，会把这些有效时区静默删掉——
 * 其中 `UTC` 还是最常见的运维选择。
 */
function canonicalTimeZone(timeZone: string): string | undefined {
  try {
    return new Intl.DateTimeFormat('en', { timeZone }).resolvedOptions().timeZone;
  } catch {
    return undefined;
  }
}

/**
 * 该时区此刻的偏移文案；取不到返回空串。
 *
 * `shortOffset` 给的是 `GMT+8`，这里改写成 `UTC+8`：偏移的通用说法是 UTC，
 * GMT 严格来说是个时区名。两种写法都参与搜索匹配（见 {@link timeZoneMatches}）。
 */
function offsetLabel(timeZone: string): { label: string; alias: string } {
  try {
    const parts = new Intl.DateTimeFormat('en-US', {
      timeZone,
      timeZoneName: 'shortOffset',
    }).formatToParts(new Date());
    const alias = parts.find((p) => p.type === 'timeZoneName')?.value ?? '';
    return { label: alias.replace(/^GMT/, 'UTC'), alias };
  } catch {
    return { label: '', alias: '' };
  }
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

/**
 * 浏览器所在时区（IANA 名）。
 *
 * `Intl.DateTimeFormat().resolvedOptions().timeZone` 直接给出运行时的默认时区，
 * 是 ECMA-402 的标准行为、各端普遍可用，因此不需要任何平台判断。
 */
export function browserTimeZone(): string | undefined {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || undefined;
  } catch {
    return undefined;
  }
}

/**
 * 全量时区，按地区分组。
 *
 * 给的是**全量**而不是精选清单：控件可搜索之后，四百多项不再是负担，而精选清单
 * 必然漏掉某些部署地。清单里额外补上 `UTC` 与浏览器时区，并按规范名去重——
 * 同一时区的规范名与旧名只保留一行，避免出现"选哪个都一样"的重复项。
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
  // 偏移**先算好再排序**：比较器会被调用 O(n log n) 次，而 offsetMinutes 每次都要
  // 构造两个 formatter；放进比较器等于把这份开销乘上比较次数。
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

/**
 * 搜索匹配：地名、完整 IANA 名、偏移（`UTC+8` 与 `GMT+8` 两种写法）与别名都算命中。
 *
 * 偏移参与匹配，因为"我在 UTC+8"是人找时区最常用的说法之一。
 */
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
