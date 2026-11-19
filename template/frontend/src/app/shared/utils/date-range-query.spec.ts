import { describe, expect, it } from 'vitest';

import {
  dateRangeFormatter,
  dateRangeFromQuery,
  dateRangeParser,
  dateRangeToQuery,
  parseZonedInstant,
  validTimeRange,
} from './date-range-query';

describe('date-range-query', () => {
  it('keeps only complete, parseable and ordered ranges', () => {
    expect(validTimeRange('2026-11-18T16:00:00.000Z', '2026-11-19T15:59:59.999Z')).toEqual({
      startTime: '2026-11-18T16:00:00.000Z',
      endTime: '2026-11-19T15:59:59.999Z',
    });
    expect(validTimeRange('2026-11-18T16:00:00.000Z', null)).toBeNull();
    expect(validTimeRange('2026-11-01', '2026-11-07')).toBeNull();
    expect(validTimeRange('2026-11-01T00:00:00', '2026-11-07T00:00:00Z')).toBeNull();
    expect(validTimeRange('2026-11-19T08:00:00+08:00', '2026-11-19T01:00:00Z')).toEqual({
      startTime: '2026-11-19T00:00:00.000Z',
      endTime: '2026-11-19T01:00:00.000Z',
    });
    expect(validTimeRange('not-a-date', '2026-11-19T15:59:59.999Z')).toBeNull();
    expect(validTimeRange('2026-11-20T00:00:00.000Z', '2026-11-19T00:00:00.000Z')).toBeNull();
  });

  it('rejects impossible calendar days and keeps sub-millisecond precision', () => {
    expect(validTimeRange('2026-02-30T00:00:00Z', '2026-03-05T00:00:00Z')).toBeNull();
    expect(validTimeRange('2026-11-19T24:00:00Z', '2026-11-20T01:00:00Z')).toBeNull();
    expect(validTimeRange('2026-11-12T16:00:00Z', '2026-11-19T08:34:56.1234567Z')).toEqual({
      startTime: '2026-11-12T16:00:00.000Z',
      endTime: '2026-11-19T08:34:56.1234567Z',
    });
    expect(parseZonedInstant('2026-11-19T08:34:56.1234567+08:00')?.iso).toBe(
      '2026-11-19T00:34:56.1234567Z',
    );
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
