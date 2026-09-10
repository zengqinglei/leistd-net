import { timeZoneGroups, timeZoneMatches, TimeZoneOption } from './setting-choices';

/**
 * 时区清单的合法性判定。
 *
 * 上一版拿 `Intl.supportedValuesOf('timeZone')` 当"全部合法时区"去过滤一份手挑清单，
 * 结果把 `UTC`、`Asia/Kolkata`、`America/Argentina/Buenos_Aires` 静默删掉了——
 * 它只列**规范名**，而这些别名同样能用于渲染。这组用例钉住"按能否渲染判定"这条口径。
 */
describe('时区候选项', () => {
  const groups = timeZoneGroups();
  const all: TimeZoneOption[] = groups.flatMap((group) => [...group.zones]);
  const find = (id: string) => all.find((zone) => zone.value === id);

  it('每一项都能真的用于渲染', () => {
    expect(all.length).toBeGreaterThan(100);
    for (const zone of all) {
      expect(() =>
        new Intl.DateTimeFormat('en', { timeZone: zone.value }).format(new Date()),
      ).not.toThrow();
    }
  });

  // UTC 不在 supportedValuesOf 里，却是最常见的运维选择：它缺席就是个 bug。
  it('包含 UTC', () => {
    expect(find('UTC')).toBeTruthy();
  });

  it('包含常用地区，且改名过的时区能按两种拼法搜到', () => {
    expect(find('Asia/Shanghai')).toBeTruthy();

    // 印度在多数运行时的规范名是 Asia/Calcutta，但人会去搜 Kolkata
    const india = all.find((zone) => /Calcutta|Kolkata/.test(zone.value));
    expect(india).toBeTruthy();
    expect(timeZoneMatches(india!, 'Kolkata')).toBeTrue();
    expect(timeZoneMatches(india!, 'Calcutta')).toBeTrue();
  });

  // 同一个时区列成两行，选哪个都一样，只会让人怀疑自己选错了。
  it('同一时区不出现两次', () => {
    expect(all.length).toBe(new Set(all.map((zone) => zone.value)).size);
  });

  it('浏览器所在时区一定在候选里，并且被标出来', () => {
    const browser = Intl.DateTimeFormat().resolvedOptions().timeZone;
    const marked = all.filter((zone) => zone.isBrowser);

    expect(marked.length).toBe(1);
    expect(
      new Intl.DateTimeFormat('en', { timeZone: marked[0].value }).resolvedOptions().timeZone,
    ).toBe(new Intl.DateTimeFormat('en', { timeZone: browser }).resolvedOptions().timeZone);
  });

  it('分组非空且按地区名有序', () => {
    expect(groups.length).toBeGreaterThan(1);
    for (const group of groups) {
      expect(group.zones.length).toBeGreaterThan(0);
    }
    expect(groups.map((group) => group.region)).toEqual(
      [...groups.map((group) => group.region)].sort((a, b) => a.localeCompare(b)),
    );
  });

  it('偏移用 UTC 写法展示，GMT 写法也能搜到', () => {
    const shanghai = find('Asia/Shanghai')!;

    expect(shanghai.offsetLabel).toBe('UTC+8');
    expect(timeZoneMatches(shanghai, 'UTC+8')).toBeTrue();
    expect(timeZoneMatches(shanghai, 'GMT+8')).toBeTrue();
  });

  it('搜索匹配地名与完整 IANA 名，不匹配无关词', () => {
    const shanghai = find('Asia/Shanghai')!;

    expect(timeZoneMatches(shanghai, 'shang')).toBeTrue();
    expect(timeZoneMatches(shanghai, 'Asia/Shang')).toBeTrue();
    expect(timeZoneMatches(shanghai, '  ')).toBeTrue();
    expect(timeZoneMatches(shanghai, 'reykjavik')).toBeFalse();
  });
});
