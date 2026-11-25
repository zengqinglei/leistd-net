import { firstDayOfWeek } from './locale-week';

/** 一周的第一天随界面语言；运行时给不出时取周一。 */
describe('firstDayOfWeek', () => {
  it('falls back to Monday when the runtime does not provide week information', () => {
    const Locale = Intl.Locale;
    vi.spyOn(Intl, 'Locale').mockImplementation(
      class extends Locale {
        getWeekInfo = undefined;
        weekInfo = undefined;
      },
    );

    expect(firstDayOfWeek('en-US')).toBe(1);
  });

  it('falls back to Monday for an unknown locale', () => {
    expect(firstDayOfWeek('not a locale')).toBe(1);
  });
});
