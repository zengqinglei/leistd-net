import { formatDuration, formatNumber, formatTokenCount } from './format.utils';

/**
 * 基础 smoke 测试：纯函数、无 Angular/本地化依赖，存在于所有生成场景。
 * 作用之一是保证「非本地化」项目也至少有一个 spec——否则 `ng test` 会因零 spec 以非零码退出。
 */
describe('format.utils', () => {
  it('formatTokenCount 按量级缩写并处理空值', () => {
    expect(formatTokenCount(null)).toBe('0');
    expect(formatTokenCount(undefined)).toBe('0');
    expect(formatTokenCount(999)).toBe('999');
    expect(formatTokenCount(1_500)).toBe('1.50K');
    expect(formatTokenCount(2_500_000)).toBe('2.50M');
    expect(formatTokenCount(3_200_000_000)).toBe('3.20B');
  });

  it('formatNumber 添加千位分隔符', () => {
    expect(formatNumber(1_234_567)).toBe('1,234,567');
  });

  it('formatDuration 自动选择最大单位', () => {
    expect(formatDuration(null)).toBe('N/A');
    expect(formatDuration(-1)).toBe('N/A');
    expect(formatDuration(320)).toBe('320ms');
    expect(formatDuration(5_100)).toBe('5.1s');
    expect(formatDuration(3 * 60_000)).toBe('3min');
  });
});
