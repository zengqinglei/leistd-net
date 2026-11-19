import { isoToZonedDate, zonedEndOfDayIso, zonedStartOfDayIso } from './zoned-time';
import { formatAppDate, parseAppCalendarDate } from '../pipes/app-date-pipe';

/** 写进 URL、发给后端的时间范围：UTC ISO 时刻，两端闭区间。 */
export interface TimeRangeQuery {
  startTime: string | null;
  endTime: string | null;
}

/** 带 `Z` 或显式偏移的 ISO 时刻；后端拒绝不带时区的写法，这里一并视为不合法。 */
const ZONED_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$/;

/**
 * 从 URL 查询参数取出合法的时间范围，并归一为 UTC（`toISOString`）：控件、列表请求与按条件恢复
 * 共用这一份结果。只有一端、不带时区、格式错误或起止颠倒时视为未筛选。
 */
export function validTimeRange(
  start: string | null | undefined,
  end: string | null | undefined,
): { startTime: string; endTime: string } | null {
  const parse = (value: string | null | undefined) =>
    value && ZONED_INSTANT.test(value) ? Date.parse(value) : Number.NaN;
  const from = parse(start);
  const to = parse(end);
  return Number.isFinite(from) && Number.isFinite(to) && from <= to
    ? { startTime: new Date(from).toISOString(), endTime: new Date(to).toISOString() }
    : null;
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
