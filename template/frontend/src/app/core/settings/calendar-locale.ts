import { effect, inject, provideEnvironmentInitializer, untracked } from '@angular/core';
//#if (IncludeLocalization)
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { BrnCalendarI18n, injectBrnCalendarI18n } from '@spartan-ng/brain/calendar';

import { firstDayOfWeek } from '../../shared/utils/locale-week';
import { AccountPreferencesService } from '../services/account-preferences-service';

/** 任取一个周日（2026-10-11）推出一周七天的名称，下标 0 = 周日，与日历组件的约定一致。 */
const A_SUNDAY = Date.UTC(2026, 9, 11);

/** 日历翻页按钮的读屏名：日视图翻月，年月视图翻一年或翻一页年份。键名与 `common.calendar` 词条一致。 */
export interface CalendarNavLabels {
  previousMonth: string;
  nextMonth: string;
  previousYear: string;
  nextYear: string;
  previousYears: string;
  nextYears: string;
}

/** 英文读屏名：不启用多语言时直接使用，启用时作为词条加载前的初始值。 */
const ENGLISH_NAV_LABELS: CalendarNavLabels = {
  previousMonth: 'Previous month',
  nextMonth: 'Next month',
  previousYear: 'Previous year',
  nextYear: 'Next year',
  previousYears: 'Previous years',
  nextYears: 'Next years',
};

/**
 * 日历（日期选择器）的星期名、月份标题、年月名与翻页按钮的读屏名跟随界面写法：组件默认星期写死英文、
 * 月份标题却按浏览器语言，中文界面里会出现"2026年10月"配"Su Mo Tu"。locale 取 `displayLocale`，
 * 与 `appDate` 同一来源；一周从哪天开始按 locale 的习惯。
 */
export function provideAppCalendarLocale() {
  return provideEnvironmentInitializer(() => {
    const i18n = injectBrnCalendarI18n();
    const displayLocale = inject(AccountPreferencesService).displayLocale;
    //#if (IncludeLocalization)
    // toSignal 包住 selectTranslateObject 让它随语言切换重算——静态字符串会固定在启动时的语言上
    const transloco = inject(TranslocoService);
    const labels = toSignal(transloco.selectTranslateObject<CalendarNavLabels>('common.calendar'), {
      initialValue: ENGLISH_NAV_LABELS,
    });
    //#endif

    effect(() => {
      //#if (IncludeLocalization)
      const config = calendarLocale(displayLocale(), labels());
      //#else
      const config = calendarLocale(displayLocale(), ENGLISH_NAV_LABELS);
      //#endif
      // use() 先读再写同一个配置 signal：不隔离读取，effect 会订阅自己的写入而无限重排
      untracked(() => i18n.use(config));
    });
  });
}

/** 按 locale 生成日历的全部文案；认不出的 locale 由 `Intl` 回落到运行时默认。 */
export function calendarLocale(
  locale: string | undefined,
  labels: CalendarNavLabels = ENGLISH_NAV_LABELS,
): Partial<BrnCalendarI18n> {
  const format = (options: Intl.DateTimeFormatOptions) => {
    try {
      return new Intl.DateTimeFormat(locale, { timeZone: 'UTC', ...options });
    } catch {
      return new Intl.DateTimeFormat(undefined, { timeZone: 'UTC', ...options });
    }
  };
  const weekdayShort = format({ weekday: 'short' });
  const weekdayLong = format({ weekday: 'long' });
  const monthLong = format({ month: 'long', year: 'numeric' });
  const monthShort = format({ month: 'short' });
  const yearOnly = format({ year: 'numeric' });
  const dayOf = (index: number) => new Date(A_SUNDAY + index * 86_400_000);
  const monthOf = (month: number, year = 2000) => new Date(Date.UTC(year, month, 1));

  return {
    formatWeekdayName: (index) => weekdayShort.format(dayOf(index)),
    labelWeekday: (index) => weekdayLong.format(dayOf(index)),
    formatHeader: (month, year) => monthLong.format(monthOf(month, year)),
    formatMonth: (month) => monthShort.format(monthOf(month)),
    formatYear: (year) => yearOnly.format(monthOf(0, year)),
    months: () =>
      Array.from({ length: 12 }, (_, month) => monthShort.format(monthOf(month))) as ReturnType<
        BrnCalendarI18n['months']
      >,
    labelPrevious: () => labels.previousMonth,
    labelNext: () => labels.nextMonth,
    labelPreviousYear: () => labels.previousYear,
    labelNextYear: () => labels.nextYear,
    labelPreviousYears: () => labels.previousYears,
    labelNextYears: () => labels.nextYears,
    // ISO 写法 1 = 周一 … 7 = 周日，日历组件用 0 = 周日
    firstDayOfWeek: () =>
      (firstDayOfWeek(locale) % 7) as ReturnType<BrnCalendarI18n['firstDayOfWeek']>,
  };
}
