import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, map } from 'rxjs';

import { ThemeService } from './theme-service';

/**
 * 跟随系统的暗色判定。系统偏好由测试替身给出：测试浏览器自身是亮色，
 * 服务若绕开注入的 BreakpointObserver 自己去读媒体查询，这里的"系统切到暗色"就不会生效。
 */
describe('ThemeService', () => {
  const query = '(prefers-color-scheme: dark)';
  let systemDark: BehaviorSubject<boolean>;

  // 只有查询的确是系统暗色偏好时才如实回报，查错了查询就恒为不匹配
  const state = (value: string, matches: boolean): BreakpointState => ({
    matches: value === query && matches,
    breakpoints: { [value]: value === query && matches },
  });

  function create(): ThemeService {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: BreakpointObserver,
          useValue: {
            observe: (value: string) => systemDark.pipe(map((matches) => state(value, matches))),
          },
        },
      ],
    });
    return TestBed.inject(ThemeService);
  }

  beforeEach(() => {
    localStorage.removeItem(ThemeService.STORAGE_KEY);
    systemDark = new BehaviorSubject(false);
  });

  afterEach(() => {
    localStorage.removeItem(ThemeService.STORAGE_KEY);
    document.documentElement.classList.remove('dark');
  });

  it('follows the system preference in system mode, including later changes', () => {
    systemDark.next(true);
    const service = create();

    expect(service.mode()).toBe('system');
    expect(service.isDarkTheme()).toBe(true);

    systemDark.next(false);
    expect(service.isDarkTheme()).toBe(false);
  });

  it('ignores the system preference once a mode is chosen explicitly', () => {
    systemDark.next(true);
    const service = create();

    service.setMode('light');
    expect(service.isDarkTheme()).toBe(false);

    systemDark.next(false);
    service.setMode('dark');
    expect(service.isDarkTheme()).toBe(true);
  });

  it('applies the resolved theme to the document root', () => {
    systemDark.next(true);
    create();
    TestBed.tick();

    expect(document.documentElement.classList.contains('dark')).toBe(true);
  });
});
