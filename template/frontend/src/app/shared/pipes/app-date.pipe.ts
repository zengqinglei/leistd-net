import { Pipe, PipeTransform } from '@angular/core';

/** 预设格式：与 Angular `DatePipe` 的常用格式对应，但按 IANA 时区渲染。 */
const FORMATS: Record<string, Intl.DateTimeFormatOptions> = {
  short: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' },
  full: {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  },
  date: { year: 'numeric', month: '2-digit', day: '2-digit' },
  time: { hour: '2-digit', minute: '2-digit' },
  monthDayTime: { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' },
};

export type AppDateFormat = keyof typeof FORMATS;

/**
 * 按指定 IANA 时区渲染时刻。
 *
 * **不要用 Angular 自带的 `date` 管道渲染业务时间**：它的时区参数只接受 `+0800` 这类固定偏移，
 * 传 IANA 名会在内部 `Date.parse` 失败后**静默回落到浏览器时区**——设置看着生效，实际没有。
 * 固定偏移也表达不了夏令时（同一地区冬夏偏移不同）。这里改用原生 `Intl.DateTimeFormat`，
 * 它直接接受 IANA 名并自带夏令时规则。
 *
 * 时区由调用方传入（通常来自 `SettingContextService.timeZone` 这个 signal），
 * 管道本身保持 pure：时区作为入参变化时 Angular 自会重算，不必每轮变更检测都跑一遍。
 */
@Pipe({ name: 'appDate' })
export class AppDate implements PipeTransform {
  transform(
    value: string | number | Date | null | undefined,
    format: AppDateFormat = 'full',
    timeZone?: string,
  ): string {
    if (value === null || value === undefined || value === '') {
      return '';
    }

    const date = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(date.getTime())) {
      return '';
    }

    try {
      // en-CA 给出 YYYY-MM-DD，与既有界面的日期写法一致，且不随界面语言漂移。
      return new Intl.DateTimeFormat('en-CA', {
        ...FORMATS[format],
        hour12: false,
        ...(timeZone ? { timeZone } : {}),
      })
        .format(date)
        .replace(',', '');
    } catch {
      // 设置里存了当前运行时不认识的时区（时区数据库更名等）时退回浏览器时区，
      // 不要让一个偏好设置把整张列表打成空白。
      return new Intl.DateTimeFormat('en-CA', { ...FORMATS[format], hour12: false })
        .format(date)
        .replace(',', '');
    }
  }
}
