import { Pipe, PipeTransform } from '@angular/core';

/**
 * 语义槽位：调用点声明"这里要显示什么、要多细"。
 *
 * 精度是**槽位**的属性，不是用户偏好。一列要不要秒由这一列的用途决定：通知列表要窄，
 * 审计详情要能对时。把精度提成一个全局设置，等于让这两处被同一个值拽着走，
 * 而用户根本不会为了看通知去改一个叫"日期格式"的开关。
 */
const SLOTS = {
  full: { date: 'full', time: true, seconds: true },
  short: { date: 'full', time: true, seconds: false },
  date: { date: 'full', time: false, seconds: false },
  time: { date: 'none', time: true, seconds: false },
  monthDayTime: { date: 'monthDay', time: true, seconds: false },
} as const;

export type AppDateFormat = keyof typeof SLOTS;

/** 一种书写方式：用哪个 locale 渲染，月和日取什么形态，以及要不要固定 24 小时制。 */
interface Writing {
  locale: string;
  month: 'short' | '2-digit';
  day: 'numeric' | '2-digit';
  hour12?: boolean;
  /** en-CA 会在日期与时刻之间插一个逗号，`YYYY-MM-DD HH:mm:ss` 这种写法要去掉它。 */
  stripComma: boolean;
}

/**
 * `YYYY-MM-DD HH:mm:ss`，24 小时制。
 *
 * 也是**拿不到界面语言时的回落**（未登录、设置还没加载、浏览器不报语言）：它不随环境漂移，
 * 且年月日顺序固定，不会被读成别的日期。
 */
const ISO_DASHED: Writing = {
  locale: 'en-CA',
  month: '2-digit',
  day: '2-digit',
  hour12: false,
  stripComma: true,
};

/**
 * 按界面语言的地区习惯书写，月份用**名称**而不是数字。
 *
 * 数字月日在跨地区时是有歧义的——`09/04/2026` 在美国是 9 月 4 日，在英国是 4 月 9 日，
 * 而看的人无从判断这一列用了哪一种。月份名把这个歧义直接消掉：`Sep 4, 2026` 只有一种读法。
 * 12/24 小时制交给 locale，那正是"地区习惯"的一部分。
 */
function regional(locale: string): Writing {
  return { locale, month: 'short', day: 'numeric', stripComma: false };
}

/**
 * 按界面语言语种覆盖书写方式。
 *
 * 中文是**唯一一处刻意例外**：CLDR 给 zh 的数字写法是 `2026/09/04`（斜杠），
 * 而中文软件的惯例和 GB/T 7408 都是短横线，所以改用同序、带短横线的 en-CA 渲染，
 * 并固定 24 小时制。
 *
 * 不要把这里扩成一张 locale → pattern 表：那等于把"日期格式"重新做成了配置，
 * 而手写 pattern 填错既不报错也查不出来，只会静默渲染出错误的日期。
 * 要给某个语种改写法，就在这里加一条并说明依据。
 */
const WRITING_OVERRIDES: Record<string, Writing> = {
  zh: ISO_DASHED,
};

/**
 * 管道与需要同一写法的非模板调用方（如日期区间输入框）共用的渲染入口。
 * 两处各写一份，列表里的时间与筛选框里的日期迟早写成两种样子。
 */
export function formatAppDate(
  value: string | number | Date | null | undefined,
  format: AppDateFormat = 'full',
  timeZone?: string,
  locale?: string,
): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }

  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const writing = writingFor(locale);
  const options = optionsFor(format, writing);

  try {
    return render(date, writing, options, timeZone);
  } catch {
    // 设置里存了当前运行时不认识的时区（时区数据库更名等）时退回浏览器时区，
    // 不要让一个偏好设置把整张列表打成空白。
    return render(date, writing, options);
  }
}

/**
 * 把按 {@link formatAppDate} 的 `date` 槽位写出的日期解析回**浏览器本地**的日历日；
 * 同时接受 `YYYY-MM-DD`（任何语言下都认，手输最省事）。认不出就返回 `null`。
 *
 * 不为每种语言写一套解析规则：先找四位年份，再把这一年的每一天按同一写法渲染出来比对
 * （忽略大小写、空白与标点）。写法由 `Intl` 决定、随语言变化，逐语种手写的解析规则
 * 迟早与它对不上；反查则天然对称——能显示出来的就能认回去，认回去的一定是存在的日期。
 */
export function parseAppCalendarDate(text: string, locale?: string): Date | null {
  const trimmed = text.trim();
  const iso = /^(\d{4})-(\d{2})-(\d{2})$/.exec(trimmed);
  if (iso) {
    return calendarDay(Number(iso[1]), Number(iso[2]), Number(iso[3]));
  }

  const year = /(?:^|\D)(\d{4})(?:\D|$)/.exec(trimmed);
  if (!year) {
    return null;
  }

  const target = comparable(trimmed);
  const day = new Date(Number(year[1]), 0, 1);
  while (day.getFullYear() === Number(year[1])) {
    if (comparable(formatAppDate(day, 'date', undefined, locale)) === target) {
      return new Date(day);
    }
    day.setDate(day.getDate() + 1);
  }
  return null;
}

/** 构造本地日历日并反查：`new Date(2026, 1, 31)` 不报错而是滚到 3 月，不反查就会收下不存在的日期。 */
function calendarDay(year: number, month: number, day: number): Date | null {
  const date = new Date(year, month - 1, day);
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day
    ? date
    : null;
}

function comparable(text: string): string {
  return text.toLowerCase().replace(/[^\p{L}\p{N}]/gu, '');
}

// Intl.DateTimeFormat 构造不便宜，解析时一年要渲染三百多次，按参数缓存。
const formatters = new Map<string, Intl.DateTimeFormat>();

function render(
  date: Date,
  writing: Writing,
  options: Intl.DateTimeFormatOptions,
  timeZone?: string,
): string {
  const resolved: Intl.DateTimeFormatOptions = {
    ...options,
    ...(writing.hour12 === undefined ? {} : { hour12: writing.hour12 }),
    ...(timeZone ? { timeZone } : {}),
  };
  const key = `${writing.locale}|${JSON.stringify(resolved)}`;
  let formatter = formatters.get(key);
  if (!formatter) {
    formatter = new Intl.DateTimeFormat(writing.locale, resolved);
    formatters.set(key, formatter);
  }

  const formatted = formatter.format(date);
  return writing.stripComma ? formatted.replace(',', '') : formatted;
}

/** 认不出的 locale 回落到固定写法，而不是让整列变空。 */
function writingFor(locale?: string): Writing {
  if (!locale) {
    return ISO_DASHED;
  }

  try {
    return WRITING_OVERRIDES[new Intl.Locale(locale).language] ?? regional(locale);
  } catch {
    return ISO_DASHED;
  }
}

function optionsFor(format: AppDateFormat, writing: Writing): Intl.DateTimeFormatOptions {
  const slot = SLOTS[format] ?? SLOTS.full;
  const options: Intl.DateTimeFormatOptions = {};

  if (slot.date !== 'none') {
    if (slot.date === 'full') {
      options.year = 'numeric';
    }

    options.month = writing.month;
    options.day = writing.day;
  }

  if (slot.time) {
    options.hour = '2-digit';
    options.minute = '2-digit';
    if (slot.seconds) {
      options.second = '2-digit';
    }
  }

  return options;
}

/**
 * 按指定 IANA 时区渲染时刻。
 *
 * **不要用 Angular 自带的 `date` 管道渲染业务时间**：它的时区参数只接受 `+0800` 这类固定偏移，
 * 传 IANA 名会在内部 `Date.parse` 失败后**静默回落到浏览器时区**——设置看着生效，实际没有。
 * 固定偏移也表达不了夏令时（同一地区冬夏偏移不同）。这里改用原生 `Intl.DateTimeFormat`，
 * 它直接接受 IANA 名并自带夏令时规则。
 *
 * 三个维度互不相干，由三个不同的地方决定：
 * - `format` 是**语义槽位**（这一处要日期还是要时刻、要不要秒），由调用点决定；
 * - `timeZone` 决定**哪一刻**，来自 `SettingContextService.timeZone`；
 * - `locale` 决定**怎么写**，来自 `SettingContextService.displayLocale`（界面语言）。
 *
 * 写法刻意由**界面语言**驱动，不由时区驱动：时区回答"哪一刻"，locale 才编码"日期怎么读"。
 * Web 平台也没有时区→地区的映射——`Intl` 只给时区 id，要从 `Asia/Shanghai` 推出 `zh-CN`
 * 得自带一份 IANA `zone.tab` 的时区→国家表，而它既会随时区拆分改名而漂移，
 * 又不是个函数（`Europe/Zurich` 对应德/法/意三种写法，`UTC` 没有国家）。
 *
 * 时区与语言都由调用方传入，管道保持 pure：入参变化时 Angular 自会重算，
 * 不必每轮变更检测都跑一遍。
 */
@Pipe({ name: 'appDate' })
export class AppDate implements PipeTransform {
  transform(
    value: string | number | Date | null | undefined,
    format: AppDateFormat = 'full',
    timeZone?: string,
    locale?: string,
  ): string {
    return formatAppDate(value, format, timeZone, locale);
  }
}
