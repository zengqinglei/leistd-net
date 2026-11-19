import { parseZonedInstant } from '../../src/app/shared/utils/date-range-query';
import { MockException } from '../core/models';

/** 解析后的时间范围，值为 {@link mockInstantKey} 的比较键；只给一端时另一端为 undefined。 */
export interface MockTimeRange {
  from?: string;
  to?: string;
}

/**
 * 与后端绑定同形地解析 `startTime` / `endTime`：查询参数接受带 `Z` 或显式偏移的时刻，JSON 只接受
 * 带 `Z` 的时刻；没传（`undefined`/`null`）视为省略，传了空串或日历上不存在的日期都不合法。
 * 写法不合规或起止颠倒时一次报全，返回 400 字段错误。
 */
export function parseMockTimeRange(
  values: { startTime?: unknown; endTime?: unknown },
  source: 'query' | 'json',
): MockTimeRange {
  const errors: { field: string; detail: string }[] = [];
  const parse = (field: 'startTime' | 'endTime') => {
    const value = values[field];
    if (value === undefined || value === null) {
      return undefined;
    }

    const instant =
      typeof value === 'string' && (source === 'query' || value.endsWith('Z'))
        ? parseZonedInstant(value)
        : null;
    if (!instant) {
      errors.push({ field, detail: `${field} must be a UTC time.` });
      return undefined;
    }

    return instant.key;
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

/** 数据里的时刻换成与 {@link MockTimeRange} 同口径的比较键（UTC，小数补齐到 7 位）。 */
function mockInstantKey(value: string): string {
  return parseZonedInstant(value)?.key ?? '';
}

/** 时刻是否落在范围内（两端闭区间，按完整小数位比较）。 */
export function withinMockTimeRange(value: string, range: MockTimeRange): boolean {
  const key = mockInstantKey(value);
  return (
    (range.from === undefined || key >= range.from) && (range.to === undefined || key <= range.to)
  );
}
