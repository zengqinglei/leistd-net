//#if (IncludeLocalization)
import { Location } from '@angular/common';
//#endif
// prettier-ignore
import {
  ApplicationRef,
  //#if (IncludeLocalization)
  Component,
  ErrorHandler,
  //#endif
  provideZonelessChangeDetection,
  signal,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
//#if (IncludeLocalization)
import { provideRouter, Router } from '@angular/router';
import {
  provideTranslocoScope,
  Translation,
  TranslocoDirective,
  TranslocoLoader,
  TranslocoService,
} from '@jsverse/transloco';
import { defer, of, Subject, throwError } from 'rxjs';
//#else
import { provideRouter } from '@angular/router';
//#endif

import { App } from './app';
//#if (IncludeLocalization)
import { resolveTranslationScopes, TranslationScopeRecovery } from './core/i18n/translation-scopes';
import { provideTranslocoTesting } from './core/i18n/transloco.testing';
import { LanguageService } from './core/services/language-service';
//#endif
import { LayoutService } from './core/services/layout-service';
import { StartupService } from './core/services/startup-service';
import { ThemeService } from './core/services/theme-service';

/** 启动失败页必须显示得出来，包括词条没取到的时候：根组件不在结构指令里。 */
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
//#if (IncludeLocalization)

@Component({
  selector: 'app-scope-test-page',
  imports: [TranslocoDirective],
  template: `<ng-container *transloco="let t; prefix: 'users'">{{ t('title') }}</ng-container>`,
})
class ScopedPage {}

/** 作用域词条要等用例显式放行（或判为失败）才到达的加载器；根词条立即返回。 */
class DelayedScopeLoader implements TranslocoLoader {
  readonly requests: string[] = [];
  readonly gates = new Map<string, Subject<Translation>>();
  private readonly unavailable = new Set<string>();

  getTranslation(path: string) {
    return defer(() => {
      this.requests.push(path);
      if (!path.includes('/')) {
        return of({ common: { save: path === 'en' ? 'Save' : '保存' } });
      }
      if (this.unavailable.has(path)) {
        return throwError(() => new Error('scope unavailable'));
      }
      const gate = new Subject<Translation>();
      this.gates.set(path, gate);
      return gate;
    });
  }

  fail(path: string): void {
    this.unavailable.add(path);
    this.gates.get(path)!.error(new Error('scope unavailable'));
  }

  restore(path: string): void {
    this.unavailable.delete(path);
  }

  resolve(path: string): void {
    const english = path.endsWith('/en');
    const gate = this.gates.get(path)!;
    gate.next({
      title: english ? 'Users' : '用户管理',
      choices: { active: english ? 'Active' : '启用' },
    });
    gate.complete();
  }
}

/**
 * 首次进入功能区时作用域词条取不到：根组件显示既有的启动失败卡片，重试回到原深链，
 * 不重跑启动流程。路由解析器的其余行为见 translation-scopes.spec.ts。
 */
describe('App translation scope recovery', () => {
  afterEach(() => localStorage.removeItem(LanguageService.STORAGE_KEY));

  it('shows the existing failure card on first scope failure and retries the original deep link', async () => {
    localStorage.setItem(LanguageService.STORAGE_KEY, 'en');
    const loader = new DelayedScopeLoader();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslocoTesting(['en', 'zh-CN'], loader),
        { provide: ErrorHandler, useValue: { handleError: vi.fn() } },
        { provide: ThemeService, useValue: {} },
        {
          provide: StartupService,
          useValue: { status: signal('success'), error: signal(null), retry: vi.fn() },
        },
        provideRouter([
          {
            path: 'users',
            component: ScopedPage,
            providers: [provideTranslocoScope('users')],
            resolve: { translations: resolveTranslationScopes },
          },
        ]),
      ],
    });
    await TestBed.inject(LanguageService).initialized;
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    const url = '/users?sort=name#list';
    const entered = router.navigateByUrl(url);
    await vi.waitFor(() => expect(loader.requests).toContain('users/en'));
    loader.fail('users/en');
    expect(await entered).toBe(false);
    fixture.detectChanges();
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('h3')!.textContent).toBe('Application Failed to Load');
    expect(host.querySelector('p')!.textContent).toBe(
      'An unknown error occurred. Please try again later.',
    );
    expect(host.querySelector('button')!.textContent!.trim()).toBe('Retry');
    expect(TestBed.inject(TranslationScopeRecovery).failedUrl()).toBe(url);
    expect(TestBed.inject(Location).path(true)).toBe(url);
    loader.restore('users/en');
    host.querySelector('button')!.click();
    await vi.waitFor(() => expect(loader.gates.get('users/en')!.isStopped).toBe(false));
    loader.resolve('users/en');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(router.url).toBe(url);
    expect(TestBed.inject(TranslationScopeRecovery).failedUrl()).toBeNull();
    expect(host.querySelector('h3')).toBeNull();
    expect(host.textContent).toContain('Users');
    expect(TestBed.inject(StartupService).retry).not.toHaveBeenCalled();
  });

  it('retries a failed first navigation to the root until the scope recovers', async () => {
    localStorage.setItem(LanguageService.STORAGE_KEY, 'en');
    const loader = new DelayedScopeLoader();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslocoTesting(['en', 'zh-CN'], loader),
        { provide: ErrorHandler, useValue: { handleError: vi.fn() } },
        { provide: ThemeService, useValue: {} },
        {
          provide: StartupService,
          useValue: { status: signal('success'), error: signal(null), retry: vi.fn() },
        },
        provideRouter([
          {
            path: '',
            component: ScopedPage,
            providers: [provideTranslocoScope('users')],
            resolve: { translations: resolveTranslationScopes },
          },
        ]),
      ],
    });
    await TestBed.inject(LanguageService).initialized;
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    const entered = router.navigateByUrl('/');
    await vi.waitFor(() => expect(loader.requests).toContain('users/en'));
    loader.fail('users/en');
    expect(await entered).toBe(false);
    fixture.detectChanges();
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const recovery = TestBed.inject(TranslationScopeRecovery);
    expect(router.url).toBe('/');
    expect(recovery.failedUrl()).toBe('/');
    expect(host.querySelector('h3')!.textContent).toBe('Application Failed to Load');
    expect(host.querySelector('app-scope-test-page')).toBeNull();
    const attempts = loader.requests.filter((path) => path === 'users/en').length;
    host.querySelector('button')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(loader.requests.filter((path) => path === 'users/en').length).toBeGreaterThan(attempts);
    expect(recovery.failedUrl()).toBe('/');
    expect(host.querySelector('h3')!.textContent).toBe('Application Failed to Load');
    loader.restore('users/en');
    host.querySelector('button')!.click();
    await vi.waitFor(() => expect(loader.gates.get('users/en')!.isStopped).toBe(false));
    loader.resolve('users/en');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(router.url).toBe('/');
    expect(recovery.failedUrl()).toBeNull();
    expect(host.querySelector('h3')).toBeNull();
    expect(host.textContent).toContain('Users');
    expect(TestBed.inject(StartupService).retry).not.toHaveBeenCalled();
  });
});
//#endif
