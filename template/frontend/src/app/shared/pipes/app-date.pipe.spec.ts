import { AppDate } from './app-date.pipe';

/**
 * 按 IANA 时区渲染时刻。
 *
 * 这些用例存在的理由：Angular 自带的 `date` 管道只接受 `+0800` 这类固定偏移，
 * 传 IANA 名会在内部解析失败后**静默回落到浏览器时区**——界面照常渲染，设置却没生效，
 * 编译和端到端都发现不了。固定偏移也表达不了夏令时。
 */
describe('AppDate', () => {
  const pipe = new AppDate();

  it('renders in the given IANA time zone', () => {
    const utc = '2026-01-15T12:00:00Z';

    expect(pipe.transform(utc, 'full', 'Asia/Shanghai')).toBe('2026-01-15 20:00:00');
    expect(pipe.transform(utc, 'full', 'America/New_York')).toBe('2026-01-15 07:00:00');
    expect(pipe.transform(utc, 'full', 'UTC')).toBe('2026-01-15 12:00:00');
  });

  // 同一地区冬夏偏移不同，固定偏移表达不了；纽约 1 月 -5、7 月 -4。
  it('follows daylight saving transitions', () => {
    expect(pipe.transform('2026-01-15T12:00:00Z', 'full', 'America/New_York')).toBe(
      '2026-01-15 07:00:00',
    );
    expect(pipe.transform('2026-07-15T12:00:00Z', 'full', 'America/New_York')).toBe(
      '2026-07-15 08:00:00',
    );
  });

  it('supports the compact format used in the notification list', () => {
    expect(pipe.transform('2026-01-15T12:00:00Z', 'monthDayTime', 'Asia/Shanghai')).toBe(
      '01-15 20:00',
    );
  });

  // 时区数据库会更名，库里的历史值迟早失效；那时退回浏览器时区，不要让整列变空。
  it('falls back to the browser time zone when the time zone is unusable', () => {
    const utc = '2026-01-15T12:00:00Z';
    const browserRendered = pipe.transform(utc, 'full', undefined);

    expect(pipe.transform(utc, 'full', 'Mars/Olympus')).toBe(browserRendered);
    expect(browserRendered).not.toBe('');
  });

  it('renders nothing for empty input', () => {
    expect(pipe.transform(null, 'full', 'Asia/Shanghai')).toBe('');
    expect(pipe.transform(undefined, 'full', 'Asia/Shanghai')).toBe('');
    expect(pipe.transform('', 'full', 'Asia/Shanghai')).toBe('');
    expect(pipe.transform('not-a-date', 'full', 'Asia/Shanghai')).toBe('');
  });
});
