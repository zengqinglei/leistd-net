import { ApplicationRef, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoLoader, TranslocoService } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';
//#endif

import { App } from './app';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from './core/i18n/transloco.testing';
//#endif
import { StartupService } from './core/services/startup-service';
import { ThemeService } from './core/services/theme-service';
import { LayoutService } from './layout/services/layout-service';

/**
 * 启动失败页必须显示得出来——包括正是词条没取到的时候。
 *
 * 根组件不在结构指令里：文案若等词条到位，这一层就是空白；若在词条加载失败时读取即抛错，
 * 它整个渲染不出来。而这时用户手里只剩这一页。
 */
describe('App startup failure page', () => {
  //#if (IncludeLocalization)
  async function render(loader: TranslocoLoader): Promise<HTMLElement> {
    //#else
  async function render(): Promise<HTMLElement> {
    //#endif
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        {
          provide: StartupService,
          useValue: {
            status: signal('failed'),
            error: signal(new Error('boom')),
            retry: () => Promise.resolve(),
          },
        },
        // 主题与本页无关，真实实例会连带拉起媒体查询与本地存储
        { provide: ThemeService, useValue: {} },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en'], loader),
        //#endif
      ],
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  function page(host: HTMLElement) {
    return {
      header: host.querySelector('h3')!.textContent!.trim(),
      message: host.querySelector('p')!.textContent!.trim(),
      retry: host.querySelector('button')!.textContent!.trim(),
    };
  }

  it('shows the failure in English when translations are unavailable', async () => {
    //#if (IncludeLocalization)
    // Transloco 在开发模式下会自己打加载失败日志
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    // 主语言与回落语言都取不到
    const host = await render({ getTranslation: () => throwError(() => new Error('offline')) });
    //#else
    const host = await render();
    //#endif

    expect(page(host)).toEqual({
      header: 'Application Failed to Load',
      message: 'An unknown error occurred: boom',
      retry: 'Retry',
    });
  });
  //#if (IncludeLocalization)

  it('shows the failure in the active language, filling in the error details', async () => {
    const host = await render({
      getTranslation: () =>
        of({
          app: { startup: { failed: '应用加载失败', unknownErrorDetail: '未知错误：{{message}}' } },
          common: { retry: '重试' },
        }),
    });

    expect(page(host)).toEqual({
      header: '应用加载失败',
      message: '未知错误：boom',
      retry: '重试',
    });
  });
  //#endif
});

/**
 * 浏览器标签页标题只由根组件设置：页面标题取自布局标题，后面跟应用名；
 * 没有页面标题的页（认证页、落地页）只显示应用名。
 */
describe('App document title', () => {
  function setUp(): { layout: LayoutService; title: Title } {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        {
          provide: StartupService,
          useValue: {
            status: signal('loading'),
            error: signal(null),
            retry: () => Promise.resolve(),
          },
        },
        { provide: ThemeService, useValue: {} },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en', 'zh-CN']),
        //#endif
      ],
    });
    TestBed.createComponent(App);
    return { layout: TestBed.inject(LayoutService), title: TestBed.inject(Title) };
  }

  //#if (IncludeLocalization)
  it('follows the page title and the active language', async () => {
    const { layout, title } = setUp();
    const transloco = TestBed.inject(TranslocoService);
    transloco.setTranslation({ app: { name: 'Acme Portal' } }, 'en');
    transloco.setTranslation({ app: { name: '示例门户' } }, 'zh-CN');
    await TestBed.inject(ApplicationRef).whenStable();

    expect(title.getTitle()).toBe('Acme Portal');

    layout.title.set('Users');
    await TestBed.inject(ApplicationRef).whenStable();
    expect(title.getTitle()).toBe('Users · Acme Portal');

    // 页面标题由各页随语言重新设置，应用名由这里随语言更新
    transloco.setActiveLang('zh-CN');
    layout.title.set('用户管理');
    await TestBed.inject(ApplicationRef).whenStable();
    expect(title.getTitle()).toBe('用户管理 · 示例门户');

    layout.title.set('');
    await TestBed.inject(ApplicationRef).whenStable();
    expect(title.getTitle()).toBe('示例门户');
  });

  it('falls back to the English app name when the entry is missing', async () => {
    const { layout, title } = setUp();
    layout.title.set('Users');
    await TestBed.inject(ApplicationRef).whenStable();

    expect(title.getTitle()).toBe('Users · Template Project');
  });
  //#else
  it('follows the page title, showing only the app name without one', async () => {
    const { layout, title } = setUp();
    await TestBed.inject(ApplicationRef).whenStable();

    expect(title.getTitle()).toBe('Template Project');

    layout.title.set('Users');
    await TestBed.inject(ApplicationRef).whenStable();
    expect(title.getTitle()).toBe('Users · Template Project');

    layout.title.set('');
    await TestBed.inject(ApplicationRef).whenStable();
    expect(title.getTitle()).toBe('Template Project');
  });
  //#endif
});
