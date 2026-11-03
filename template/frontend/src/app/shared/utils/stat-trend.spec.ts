import { changeOf, sparklineOf } from './stat-trend';

describe('changeOf', () => {
  it('rounds the change rate to whole percents', () => {
    expect(changeOf(129, 100)).toBe(0.29);
    expect(changeOf(1001, 1000)).toBe(0);
    expect(changeOf(50, 100)).toBe(-0.5);
  });

  it('does not compare with a missing or zero previous value', () => {
    expect(changeOf(5, null)).toBeNull();
    expect(changeOf(5, undefined)).toBeNull();
    expect(changeOf(5, 0)).toBeNull();
  });
});

describe('sparklineOf', () => {
  it('needs at least two drawable points', () => {
    expect(sparklineOf(null, 'red')).toBeNull();
    expect(sparklineOf([3], 'red')).toBeNull();
    expect(sparklineOf([null, 3, null], 'red')).toBeNull();
    expect(sparklineOf([1, 2], 'red')).not.toBeNull();
  });

  it('skips empty buckets instead of drawing them as zero', () => {
    expect(sparklineOf([0.5, null, 1], 'red')).not.toBeNull();
  });
});
