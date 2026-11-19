import { formatAppDate, parseAppCalendarDate } from '../pipes/app-date-pipe';
import { isoToZonedDate, zonedEndOfDayIso, zonedStartOfDayIso } from './zoned-time';

/** 写进 URL、发给后端的时间范围：UTC ISO 时刻，两端闭区间。 */
export interface TimeRangeQuery {
  startTime: string | null;
  endTime: string | null;
}

const ZONED_INSTANT =
  /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?(Z|([+-])(\d{2}):(\d{2}))$/;

/**
 * 带时区的 ISO 时刻换算成 UTC 后的结果：`iso` 保留原有的全部小数位；`key` 把小数补齐到 7 位，
 * 按字符串比较即按时刻先后，比较精度与传给后端的值一致。
 */
export interface ZonedInstant {
  iso: string;
  key: string;
}

/**
 * 解析带 `Z` 或显式偏移的 ISO 时刻并换算成 UTC。不带时区、日历上不存在的日期（如 2 月 30 日）
 * 或越界的时分秒返回 `null`，与后端绑定一致。小数秒原样保留，不截成毫秒：统计接口回显的边界
 * 最多 7 位小数，截断会让列表漏掉边界上的记录。
 */
export function parseZonedInstant(value: string | null | undefined): ZonedInstant | null {
  const match = value ? ZONED_INSTANT.exec(value) : null;
  if (!match) {
    return null;
  }

  const [
    ,
    year,
    month,
    day,
    hour,
    minute,
    second = '0',
    fraction = '',
    zone,
    sign,
    offsetHours,
    offsetMinutes,
  ] = match;
  const wall = Date.UTC(+year, +month - 1, +day, +hour, +minute, +second);
  const check = new Date(wall);
  if (
    check.getUTCFullYear() !== +year ||
    check.getUTCMonth() !== +month - 1 ||
    check.getUTCDate() !== +day ||
    check.getUTCHours() !== +hour ||
    check.getUTCMinutes() !== +minute ||
    check.getUTCSeconds() !== +second ||
    (zone !== 'Z' && (+offsetHours > 14 || +offsetMinutes > 59))
  ) {
    return null;
  }

  const offset =
    zone === 'Z' ? 0 : (sign === '+' ? 1 : -1) * (+offsetHours * 60 + +offsetMinutes) * 60_000;
  const base = new Date(wall - offset).toISOString().slice(0, 19);
  return { iso: `${base}.${fraction || '000'}Z`, key: `${base}.${fraction.padEnd(7, '0')}Z` };
}

/**
 * 从 URL 查询参数取出合法的时间范围，换算成 UTC（保留小数位）：控件、列表请求与按条件恢复
 * 共用这一份结果。只有一端、不合法（见 {@link parseZonedInstant}）或起止颠倒时视为未筛选。
 */
export function validTimeRange(
  start: string | null | undefined,
  end: string | null | undefined,
): { startTime: string; endTime: string } | null {
  const from = parseZonedInstant(start);
  const to = parseZonedInstant(end);
  return from && to && from.key <= to.key ? { startTime: from.iso, endTime: to.iso } : null;
}

/** URL 里的 UTC 起止 → 日期区间控件的值（展示时区的日历日，浏览器本地 `Date`）。 */
export function dateRangeFromQuery(
  start: string | null | undefined,
  end: string | null | undefined,
  timeZone?: string,
): [Date, Date] | undefined {
  const range = validTimeRange(start, end);
  if (!range) {
    return undefined;
  }

  const from = isoToZonedDate(range.startTime, timeZone);
  const to = isoToZonedDate(range.endTime, timeZone);
  return from && to ? [from, to] : undefined;
}

/** 日期区间 → UTC 起止：起始日 00:00、结束日 23:59:59.999（展示时区）；清空时两端都为 null。 */
export function dateRangeToQuery(range: [Date, Date] | null, timeZone?: string): TimeRangeQuery {
  if (!range?.[0] || !range[1]) {
    return { startTime: null, endTime: null };
  }

  return {
    startTime: zonedStartOfDayIso(range[0], timeZone),
    endTime: zonedEndOfDayIso(range[1], timeZone),
  };
}

/**
 * 区间文案，同时交给 `hlm-date-range-input` 的 `formatDates` 与 `formatInputDates`，两态共用一种写法。
 * 只读本地字段、不套时区：控件里的 `Date` 年月日已是展示时区的日历日。
 */
export function dateRangeFormatter(locale?: string) {
  const day = (date: Date | null) => (date ? formatAppDate(date, 'date', undefined, locale) : '');
  return (dates: [Date | null, Date | null]): string => {
    const [from, to] = dates;
    if (!from && !to) {
      return '';
    }

    return `${day(from)} ~ ${day(to)}`.trim();
  };
}

/** 解析手输区间（{@link dateRangeFormatter} 的写法或 `YYYY-MM-DD`）；解析不了返回 `null`。 */
export function dateRangeParser(locale?: string) {
  return (value: string): [Date, Date] | null => {
    const parts = value
      .split('~')
      .map((part) => part.trim())
      .filter(Boolean);
    if (parts.length !== 2) {
      return null;
    }

    const from = parseAppCalendarDate(parts[0], locale);
    const to = parseAppCalendarDate(parts[1], locale);
    return from && to ? [from, to] : null;
  };
}
