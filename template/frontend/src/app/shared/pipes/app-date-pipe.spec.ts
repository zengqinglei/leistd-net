import { AppDate } from './app-date-pipe';

/**
 * 时区换算与书写方式。
 *
 * 时区那部分是**Angular 自带 `date` 管道做不到的事**：它的时区参数只接受固定偏移，
 * 传 IANA 名会静默回落到浏览器时区——界面照常渲染，设置却没生效。这里的断言就是
 * 那条静默失败的哨兵：真值换成 `date` 管道会立刻变红。
 *
 * 书写方式由 **locale** 决定，不由时区决定；精度由**槽位**决定，不由用户偏好决定。
 * 因此断言按"哪个维度该管什么"分组，而不是逐个 locale 抄一遍 ICU 输出——
 * 抄输出的用例会在 CLDR 数据更新时集体变红，却查不出任何真问题。
 */
describe('AppDate', () => {
  const pipe = new AppDate();
  const utc = '2026-09-04T10:16:30Z';

  describe('时区决定"哪一刻"', () => {
    it('按 IANA 名换算，同一时刻在不同时区渲染成不同时间', () => {
      expect(pipe.transform(utc, 'full', 'Asia/Shanghai', 'zh-CN')).toBe('2026-09-04 18:16:30');
      expect(pipe.transform(utc, 'full', 'UTC', 'zh-CN')).toBe('2026-09-04 10:16:30');
      expect(pipe.transform(utc, 'full', 'America/New_York', 'zh-CN')).toBe('2026-09-04 06:16:30');
    });

    it('夏令时按当时的规则算，不是一个固定偏移', () => {
      // 纽约冬季 UTC-5、夏季 UTC-4：固定偏移表达不了这件事
      expect(pipe.transform('2026-01-15T12:00:00Z', 'full', 'America/New_York', 'zh-CN')).toBe(
        '2026-01-15 07:00:00',
      );
      expect(pipe.transform('2026-07-15T12:00:00Z', 'full', 'America/New_York', 'zh-CN')).toBe(
        '2026-07-15 08:00:00',
      );
    });

    it('时区无法识别时回落到浏览器时区，而不是把整列渲染成空', () => {
      const browserRendered = pipe.transform(utc, 'full', undefined, 'zh-CN');

      expect(pipe.transform(utc, 'full', 'Mars/Olympus', 'zh-CN')).toBe(browserRendered);
      expect(browserRendered).not.toBe('');
    });
  });

  describe('locale 决定"怎么写"', () => {
    it('中文按 GB/T 7408 用短横线与 24 小时制', () => {
      // CLDR 给 zh 的数字写法是 2026/09/04（斜杠），这里是一处刻意例外
      expect(pipe.transform(utc, 'full', 'Asia/Shanghai', 'zh-CN')).toBe('2026-09-04 18:16:30');
      expect(pipe.transform(utc, 'full', 'Asia/Shanghai', 'zh-TW')).toBe('2026-09-04 18:16:30');
    });

    it('其余语言用月份名，消掉数字月日的歧义', () => {
      const us = pipe.transform(utc, 'full', 'Asia/Shanghai', 'en-US');
      const gb = pipe.transform(utc, 'full', 'Asia/Shanghai', 'en-GB');

      // 09/04 在美国是 9 月 4 日、在英国是 4 月 9 日；月份名让两边都只有一种读法
      expect(us).not.toContain('09/04');
      expect(gb).not.toContain('09/04');
      expect(us).toContain('Sep');
      expect(gb).toContain('Sep');
      // 字段顺序仍随地区变
      expect(us).not.toBe(gb);
    });

    it('12/24 小时制交给 locale', () => {
      expect(pipe.transform(utc, 'full', 'Asia/Shanghai', 'en-US')).toContain('PM');
      expect(pipe.transform(utc, 'full', 'Asia/Shanghai', 'en-GB')).toContain('18:');
    });

    it('拿不到 locale 时回落到固定的 ISO 写法，不随环境漂移', () => {
      const fallback = '2026-09-04 18:16:30';

      expect(pipe.transform(utc, 'full', 'Asia/Shanghai')).toBe(fallback);
      expect(pipe.transform(utc, 'full', 'Asia/Shanghai', '')).toBe(fallback);
      // 认不出的 locale 同样回落，而不是抛出去把整列打空
      expect(pipe.transform(utc, 'full', 'Asia/Shanghai', 'not a locale!!')).toBe(fallback);
    });
  });

  describe('槽位决定"多细"', () => {
    it('full 到秒，short 到分', () => {
      expect(pipe.transform(utc, 'full', 'Asia/Shanghai', 'zh-CN')).toBe('2026-09-04 18:16:30');
      expect(pipe.transform(utc, 'short', 'Asia/Shanghai', 'zh-CN')).toBe('2026-09-04 18:16');
    });

    it('date 只有日期，time 只有时刻', () => {
      expect(pipe.transform(utc, 'date', 'Asia/Shanghai', 'zh-CN')).toBe('2026-09-04');
      expect(pipe.transform(utc, 'time', 'Asia/Shanghai', 'zh-CN')).toBe('18:16');
    });

    // 通知列表这类窄位置：不带年份，也不带秒
    it('monthDayTime 省掉年与秒', () => {
      expect(pipe.transform(utc, 'monthDayTime', 'Asia/Shanghai', 'zh-CN')).toBe('09-04 18:16');
    });

    it('精度只由槽位决定，同一槽位在不同 locale 下细到同一级', () => {
      for (const locale of ['zh-CN', 'en-US', 'en-GB', 'ja-JP']) {
        // 秒只出现在 full 上
        expect(/\d{1,2}:\d{2}:\d{2}/.test(pipe.transform(utc, 'full', 'UTC', locale))).toBeTrue();
        expect(/\d{1,2}:\d{2}:\d{2}/.test(pipe.transform(utc, 'short', 'UTC', locale))).toBeFalse();
      }
    });
  });

  it('空值与非法值渲染成空串，不是 Invalid Date', () => {
    expect(pipe.transform(null, 'full', 'Asia/Shanghai')).toBe('');
    expect(pipe.transform(undefined, 'full', 'Asia/Shanghai')).toBe('');
    expect(pipe.transform('', 'full', 'Asia/Shanghai')).toBe('');
    expect(pipe.transform('not-a-date', 'full', 'Asia/Shanghai')).toBe('');
  });
});
