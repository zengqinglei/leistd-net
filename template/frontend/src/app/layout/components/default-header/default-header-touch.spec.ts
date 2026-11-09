import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { page } from 'vitest/browser';

import { DefaultHeader } from './default-header';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../core/services/account-preferences-service';
import { AuthService } from '../../../core/services/auth-service';
//#if (Impersonation)
import { ImpersonationService } from '../../../core/services/impersonation-service';
//#endif
//#if (IncludeLocalization)
import { LANG_OPTIONS, LanguageService } from '../../../core/services/language-service';
//#endif
import { LayoutService } from '../../../core/services/layout-service';
//#if (IncludeNotifications)
import { NotificationService } from '../../../core/services/notification-service';
//#endif
import { ThemeService } from '../../../core/services/theme-service';
import { expectTouchTarget, setCoarsePointer } from '../../../shared/utils/coarse-pointer.testing';

/**
 * 侧栏布局的页头在手机上的预算：右侧操作区的真实控件全开（通知 99+、模拟中、本地化）时，
 * 嵌套页面包屑的最近上级仍是一个完整的触屏目标；主题与语言在窄屏收起。
 * 切换工作空间这类上下文控件放在侧栏头部（default-sidebar），不放进页头右侧，否则 390px 下会把面包屑挤没。
 */
describe('DefaultHeader on touch phones', () => {
  const TITLE = 'Detail of an account with a very long registered company name';

  afterEach(() => setCoarsePointer(false));

  it('keeps the nearest breadcrumb parent a full touch target beside every header control', async () => {
    await setCoarsePointer(true);
    await page.viewport(390, 844);
    onTestFinished(() => page.viewport(1280, 800));
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        provideRouter([{ path: 'platform', children: [] }]),
        // 操作区控件各自依赖会话与后端，只给渲染所需的定值替身；控件本身是真实组件
        { provide: AuthService, useValue: { isAuthenticated: () => true, currentUser: signal({ id: 'u-1' }) } },
        {
          provide: AccountPreferencesService,
          useValue: { timeZone: signal(null), displayLocale: signal('en') },
        },
        {
          provide: ThemeService,
          useValue: { mode: signal('system'), modeIcon: signal('lucideMonitor') },
        },
        //#if (IncludeNotifications)
        // 未读数取最宽的「99+」角标
        {
          provide: NotificationService,
          useValue: {
            init: () => Promise.resolve(),
            notifications: signal([]),
            unreadCount: signal(120),
            loading: signal(false),
            loadFailed: signal(false),
          },
        },
        //#endif
        //#if (Impersonation)
        {
          provide: ImpersonationService,
          useValue: {
            status: signal({ isImpersonating: true, tenantName: 'Acme', impersonatorName: 'Admin' }),
            notifyAfterRenderIfJustExited: () => undefined,
          },
        },
        //#endif
        //#if (IncludeLocalization)
        {
          provide: LanguageService,
          useValue: { options: LANG_OPTIONS, currentMeta: signal(LANG_OPTIONS[0]), activeLang: signal('en') },
        },
        ...provideTranslocoTesting(),
        //#endif
      ],
    });
    await TestBed.inject(Router).navigateByUrl('/platform');
    const layout = TestBed.inject(LayoutService);
    layout.trail.set([{ label: 'Customer accounts', link: '/platform/parties' }]);
    // 当前页标题很长：上级与标题一起收缩时，上级也不能缩到触屏目标以下
    layout.title.set(TITLE);
    const fixture = TestBed.createComponent(DefaultHeader);
    fixture.componentRef.setInput('layout', 'sidebar');
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    // 前提：窄屏仍显示的操作区控件都已渲染并占位，否则下面的断言测不到挤压
    // prettier-ignore
    const shown = [
      //#if (IncludeNotifications)
      'app-notifications button',
      //#endif
      //#if (Impersonation)
      '[data-testid="impersonation-indicator"]',
      //#endif
    ];
    for (const selector of shown) {
      const width = host.querySelector(selector)?.getBoundingClientRect().width;
      expect(width, selector).toBeGreaterThan(0);
    }
    // 主题与语言在窄屏页头收起（个人设置的偏好里可改），组件仍在页面上
    // prettier-ignore
    const collapsed = [
      'app-theme-mode-toggle button',
      //#if (IncludeLocalization)
      'app-language-switcher button',
      //#endif
    ];
    for (const selector of collapsed) {
      const control = host.querySelector(selector);
      expect(control, selector).not.toBeNull();
      expect(control!.getBoundingClientRect().width, selector).toBe(0);
    }

    const parent = host.querySelector<HTMLElement>('[data-testid="breadcrumb-parent"] a')!;
    expectTouchTarget(parent);
    // 条目按链接的触屏宽度占位：条目缩得比链接窄时，链接伸进间距，再窄就被下一个分隔符盖住
    const right = parent.getBoundingClientRect().right;
    const item = host.querySelector('[data-testid="breadcrumb-parent"]')!;
    expect(right).toBeLessThanOrEqual(item.getBoundingClientRect().right + 0.5);
    // 导航区 overflow-hidden：链接伸出右缘的部分会被裁掉
    const nav = host.querySelector('app-page-breadcrumb nav')!;
    expect(right).toBeLessThanOrEqual(nav.getBoundingClientRect().right + 0.5);
    expect(host.querySelector('[data-slot="breadcrumb-page"]')!.textContent!.trim()).toBe(TITLE);

    const container = host.querySelector<HTMLElement>('header > div')!;
    expect(container.clientWidth).toBeLessThanOrEqual(390);
    expect(container.scrollWidth).toBeLessThanOrEqual(container.clientWidth);
  });
});
