/**
 * 格式化工具函数
 * 提供数字、Token等数据的格式化方法
 */

/**
 * 格式化 Token 数量（K, M, B）
 *
 * @param num Token数量
 * @returns 格式化后的字符串
 * @example
 * formatTokenCount(1234) => "1.23K"
 * formatTokenCount(1234567) => "1.23M"
 * formatTokenCount(1234567890) => "1.23B"
 */
export function formatTokenCount(num: number | undefined | null): string {
  if (num == null || Number.isNaN(num)) {
    return '0';
  }

  if (num >= 1_000_000_000) return `${(num / 1_000_000_000).toFixed(2)}B`;
  if (num >= 1_000_000) return `${(num / 1_000_000).toFixed(2)}M`;
  if (num >= 1_000) return `${(num / 1_000).toFixed(2)}K`;
  return num.toLocaleString();
}

/**
 * 格式化数字（添加千位分隔符）
 *
 * @param num 数字
 * @returns 格式化后的字符串
 * @example
 * formatNumber(1234567) => "1,234,567"
 */
export function formatNumber(num: number): string {
  return num.toLocaleString();
}

/**
 * 格式化持续时间（毫秒输入），自动选择合适单位（单一最大单位）
 * 单位升级顺序：毫秒 → 秒 → 分钟 → 小时 → 天 → 年
 * 适用于耗时等场景，显示最大单位的近似值。
 *
 * @param ms 毫秒数
 * @returns 格式化后的字符串，如 "320ms"、"5.1秒"、"3分钟"、"2小时"、"7天"、"2.0年"
 */
export function formatDuration(ms: number | undefined | null): string {
  if (ms == null || ms < 0) return 'N/A';
  if (ms < 1000) return `${Math.round(ms)}ms`;
  // locale 无关的单位缩写，无需按语言切换。
  const seconds = ms / 1000;
  if (seconds < 60) return `${seconds.toFixed(1)}s`;
  const minutes = seconds / 60;
  if (minutes < 60) return `${Math.floor(minutes)}min`;
  const hours = minutes / 60;
  if (hours < 24) return `${Math.floor(hours)}h`;
  const days = hours / 24;
  if (days < 365) return `${Math.floor(days)}d`;
  return `${(days / 365).toFixed(1)}y`;
}

/**
 * 格式化持续时间（毫秒输入），复合单位精确显示
 * 适用于倒计时等需要精确的场景，显示所有非零单位。
 * 超过1年时只显示年+天，避免输出过长。
 *
 * @param ms 毫秒数
 * @returns 格式化后的字符串，如 "3天2小时15分30秒"、"2年45天"
 */
export function formatDurationVerbose(ms: number | undefined | null): string {
  if (ms == null || ms < 0) return 'N/A';
  if (ms < 1000) return `${Math.round(ms)}ms`;

  const totalSeconds = Math.floor(ms / 1000);
  const years = Math.floor(totalSeconds / (365 * 24 * 3600));
  const days = Math.floor((totalSeconds % (365 * 24 * 3600)) / (24 * 3600));
  const hours = Math.floor((totalSeconds % (24 * 3600)) / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  if (years > 0) return `${years}y${days > 0 ? `${days}d` : ''}`;
  if (days > 0)
    return `${days}d${hours > 0 ? `${hours}h` : ''}${minutes > 0 ? `${minutes}min` : ''}`;
  if (hours > 0)
    return `${hours}h${minutes > 0 ? `${minutes}min` : ''}${seconds > 0 ? `${seconds}s` : ''}`;
  if (minutes > 0) return `${minutes}min${seconds > 0 ? `${seconds}s` : ''}`;
  return `${seconds}s`;
}
