/**
 * 展示时区与 UTC 之间的换算。
 *
 * 为什么需要它：列表里的时间按**展示时区**（`SettingContextService.timeZone`）渲染，而接口
 * 收的是 UTC。用户在筛选器里选的"3 月 1 日"指的是展示时区里的那一天，直接 `toISOString()`
 * 用的却是**浏览器时区**——两者不一致时，筛选范围会和眼前看到的时间对不上，表现为
 * "明明列表里有这一天的记录，按这一天筛却查不到"。
 *
 * 不引第三方日期库：原生 `Intl` 已经能拿到任意时区的墙上时间，而为这一个换算引入 luxon
 * 会让整个模板多一个运行时依赖。
 */

/**
 * 解析展示时区，未设置时回落到浏览器默认时区。
 *
 * 与 `AppDate` 管道同一口径——那里是 `...(timeZone ? { timeZone } : {})`，
 * 即不传 `timeZone` 就让 `Intl` 用浏览器默认。两处必须一致：表格按一套基准渲染、
 * 筛选却按另一套换算的话，选中的区间会和眼前看到的时间对不上。
 */
function resolveZone(timeZone?: string): string {
  return timeZone || new Intl.DateTimeFormat().resolvedOptions().timeZone;
}

/**
 * 某个时刻在指定时区的偏移（毫秒）。
 *
 * 做法是把该时刻在目标时区的**墙上读数**当成 UTC 再取差值，这样不必内置时区数据库。
 */
function zoneOffsetMs(instant: Date, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hour12: false,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(instant);

  const read = (type: string) => Number(parts.find((part) => part.type === type)?.value ?? '0');
  // hour 在 hour12:false 下午夜可能给出 24，取模归零
  const wallAsUtc = Date.UTC(
    read('year'),
    read('month') - 1,
    read('day'),
    read('hour') % 24,
    read('minute'),
    read('second'),
  );

  // **两边都对齐到整秒再相减**：`Intl` 只给到秒，`wallAsUtc` 天然没有毫秒，
  // 若直接减带毫秒的 `instant`，那部分毫秒会被当成偏移的一部分算进去——
  // 结果是 23:59:59.999 这种上界被推后将近一秒、跨进次日，
  // 于是"选一天"变成"选到第二天"，反解回选择器也会显示成次日。
  return wallAsUtc - (instant.getTime() - instant.getMilliseconds());
}

/**
 * 把"某时区里的墙上时间"换算成真实时刻。
 *
 * 迭代两次：第一次用猜测时刻的偏移，第二次用修正后时刻的偏移。夏令时切换当天，
 * 目标时刻两侧的偏移不同，只算一次会差整整一小时。
 */
function wallTimeToInstant(
  year: number,
  month: number,
  day: number,
  hour: number,
  minute: number,
  second: number,
  millisecond: number,
  timeZone: string,
): Date {
  const wallAsUtc = Date.UTC(year, month, day, hour, minute, second, millisecond);
  let instant = wallAsUtc - zoneOffsetMs(new Date(wallAsUtc), timeZone);
  instant = wallAsUtc - zoneOffsetMs(new Date(instant), timeZone);
  return new Date(instant);
}

/**
 * 取「该日期在指定时区的当天 00:00:00.000」对应的 UTC ISO 串。
 *
 * `date` 只取其年月日（选择器给的是浏览器本地 `Date`，时分秒无意义）。
 */
export function zonedStartOfDayIso(date: Date, timeZone?: string): string {
  return wallTimeToInstant(
    date.getFullYear(),
    date.getMonth(),
    date.getDate(),
    0,
    0,
    0,
    0,
    resolveZone(timeZone),
  ).toISOString();
}

/**
 * 取「该日期在指定时区的当天 23:59:59.999」对应的 UTC ISO 串。
 *
 * 必须取到当天最后一刻：后端是闭区间，若上界停在 00:00:00，选中的那一天会整天查不到记录。
 */
export function zonedEndOfDayIso(date: Date, timeZone?: string): string {
  return wallTimeToInstant(
    date.getFullYear(),
    date.getMonth(),
    date.getDate(),
    23,
    59,
    59,
    999,
    resolveZone(timeZone),
  ).toISOString();
}

/**
 * 把 UTC ISO 串还原成「展示时区里的那一天」，用于从 URL 回填选择器。
 *
 * 返回的是浏览器本地 `Date`，其年月日等于该时刻在 `timeZone` 里的日历日期——
 * 选择器只认年月日，这样回填才与当初选的那天一致。
 */
export function isoToZonedDate(iso: string, timeZone?: string): Date | null {
  const instant = new Date(iso);
  if (Number.isNaN(instant.getTime())) {
    return null;
  }

  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: resolveZone(timeZone),
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(instant);

  const read = (type: string) => Number(parts.find((part) => part.type === type)?.value ?? '0');
  return new Date(read('year'), read('month') - 1, read('day'));
}
