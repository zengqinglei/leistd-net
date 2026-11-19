import { MockException } from '../core/models';

const QUERY_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$/;
const JSON_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?Z$/;

/** 解析后的时间范围（毫秒）；只给一端时另一端为 undefined。 */
export interface MockTimeRange {
  from?: number;
  to?: number;
}

/**
 * 与后端绑定同形地解析 `startTime` / `endTime`：查询参数接受带 `Z` 或显式偏移的时刻，
 * 空串视为未传；JSON 只接受带 `Z` 的时刻，提供了的值（含空串）必须合法。
 * 写法不合规或起止颠倒时一次报全，返回 400 字段错误。
 */
export function parseMockTimeRange(
  values: { startTime?: unknown; endTime?: unknown },
  source: 'query' | 'json',
): MockTimeRange {
  const pattern = source === 'query' ? QUERY_INSTANT : JSON_INSTANT;
  const errors: { field: string; detail: string }[] = [];
  const parse = (field: 'startTime' | 'endTime') => {
    const value = values[field];
    if (value === undefined || value === null || (source === 'query' && value === '')) {
      return undefined;
    }

    const time = typeof value === 'string' && pattern.test(value) ? Date.parse(value) : Number.NaN;
    if (!Number.isFinite(time)) {
      errors.push({ field, detail: `${field} must be a UTC time.` });
      return undefined;
    }

    return time;
  };
  const from = parse('startTime');
  const to = parse('endTime');
  if (from !== undefined && to !== undefined && from > to) {
    const detail = 'StartTime must not be later than EndTime.';
    errors.push({ field: 'startTime', detail }, { field: 'endTime', detail });
  }

  if (errors.length > 0) {
    throw new MockException(400, { errors });
  }

  return { from, to };
}
