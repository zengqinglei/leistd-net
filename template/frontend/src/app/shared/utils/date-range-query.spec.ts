import { describe, expect, it } from 'vitest';

import {
  dateRangeFormatter,
  dateRangeFromQuery,
  dateRangeParser,
  dateRangeToQuery,
  validTimeRange,
} from './date-range-query';

describe('date-range-query', () => {
  it('keeps only complete, parseable and ordered ranges', () => {
    expect(validTimeRange('2026-11-18T16:00:00.000Z', '2026-11-19T15:59:59.999Z')).toEqual({
      startTime: '2026-11-18T16:00:00.000Z',
      endTime: '2026-11-19T15:59:59.999Z',
    });
    expect(validTimeRange('2026-11-18T16:00:00.000Z', null)).toBeNull();
    expect(validTimeRange('not-a-date', '2026-11-19T15:59:59.999Z')).toBeNull();
    expect(validTimeRange('2026-11-20T00:00:00.000Z', '2026-11-19T00:00:00.000Z')).toBeNull();
  });

  it('turns calendar days into the whole days of the display time zone in UTC', () => {
    const query = dateRangeToQuery(
      [new Date(2026, 10, 12), new Date(2026, 10, 19)],
      'Asia/Shanghai',
    );

    expect(query).toEqual({
      startTime: '2026-11-11T16:00:00.000Z',
      endTime: '2026-11-19T15:59:59.999Z',
    });
    expect(dateRangeToQuery(null, 'Asia/Shanghai')).toEqual({ startTime: null, endTime: null });
  });

  it('restores the selected days from the URL and ignores invalid ranges', () => {
    const range = dateRangeFromQuery(
      '2026-11-11T16:00:00.000Z',
      '2026-11-19T15:59:59.999Z',
      'Asia/Shanghai',
    );

    expect(range?.map((day) => [day.getFullYear(), day.getMonth(), day.getDate()])).toEqual([
      [2026, 10, 12],
      [2026, 10, 19],
    ]);
    expect(dateRangeFromQuery('2026-11-11T16:00:00.000Z', null, 'Asia/Shanghai')).toBeUndefined();
  });

  it('formats and parses the same text', () => {
    const format = dateRangeFormatter('en');
    const parse = dateRangeParser('en');
    const text = format([new Date(2026, 10, 12), new Date(2026, 10, 19)]);

    expect(parse(text)?.map((day) => day.getDate())).toEqual([12, 19]);
    expect(parse('2026-11-12 ~ 2026-11-19')?.map((day) => day.getDate())).toEqual([12, 19]);
    expect(parse('2026-11-12')).toBeNull();
  });
});
