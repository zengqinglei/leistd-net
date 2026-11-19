import { describe, expect, it } from 'vitest';

import { MockException } from '../core/models';
import { parseMockTimeRange, withinMockTimeRange } from './time-range';

function errorFields(action: () => unknown): string[] {
  try {
    action();
  } catch (error) {
    expect(error).toBeInstanceOf(MockException);
    return ((error as MockException).error as { errors: { field: string }[] }).errors.map(
      (item) => item.field,
    );
  }
  throw new Error('Expected a 400 field error.');
}

describe('mock time range', () => {
  it('accepts Z or an explicit offset in query strings and treats missing values as omitted', () => {
    expect(
      parseMockTimeRange(
        { startTime: '2026-11-19T08:00:00+08:00', endTime: '2026-11-19T01:00:00Z' },
        'query',
      ),
    ).toEqual({ from: '2026-11-19T00:00:00.0000000Z', to: '2026-11-19T01:00:00.0000000Z' });
    expect(parseMockTimeRange({ startTime: undefined, endTime: null }, 'query')).toEqual({
      from: undefined,
      to: undefined,
    });
  });

  it('accepts only Z in JSON and rejects provided empty or lower-case values', () => {
    expect(
      errorFields(() => parseMockTimeRange({ startTime: '2026-11-19T08:00:00+08:00' }, 'json')),
    ).toEqual(['startTime']);
    expect(errorFields(() => parseMockTimeRange({ startTime: '' }, 'json'))).toEqual(['startTime']);
    expect(
      errorFields(() => parseMockTimeRange({ endTime: '2026-11-19t00:00:00z' }, 'json')),
    ).toEqual(['endTime']);
  });

  it('rejects missing zones, date-only values and reversed ranges', () => {
    expect(errorFields(() => parseMockTimeRange({ startTime: '' }, 'query'))).toEqual([
      'startTime',
    ]);
    expect(
      errorFields(() => parseMockTimeRange({ startTime: '2026-02-30T00:00:00Z' }, 'query')),
    ).toEqual(['startTime']);
    expect(
      errorFields(() => parseMockTimeRange({ startTime: '2026-11-19T00:00:00' }, 'query')),
    ).toEqual(['startTime']);
    expect(errorFields(() => parseMockTimeRange({ endTime: '2026-11-19' }, 'query'))).toEqual([
      'endTime',
    ]);
    expect(
      errorFields(() =>
        parseMockTimeRange(
          { startTime: '2026-11-19T02:00:00Z', endTime: '2026-11-19T01:00:00Z' },
          'query',
        ),
      ),
    ).toEqual(['startTime', 'endTime']);
  });
  it('compares order and bounds with every fractional digit', () => {
    expect(
      errorFields(() =>
        parseMockTimeRange(
          { startTime: '2026-11-19T08:34:56.1234567Z', endTime: '2026-11-19T08:34:56.1234566Z' },
          'query',
        ),
      ),
    ).toEqual(['startTime', 'endTime']);
    const range = parseMockTimeRange({ startTime: '2026-11-19T08:34:56.1234567Z' }, 'query');
    expect(withinMockTimeRange('2026-11-19T08:34:56.123Z', range)).toBe(false);
    expect(withinMockTimeRange('2026-11-19T08:34:56.124Z', range)).toBe(true);
  });
});
