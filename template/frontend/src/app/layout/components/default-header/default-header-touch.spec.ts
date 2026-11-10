import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
//#if (IncludeLocalization)
import { of } from 'rxjs';
//#endif
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

const TITLE = 'Detail of an account with a very long registered company name';

/**
 * 侧栏布局的页头宽度预算：右侧操作区的真实控件全开（通知 99+、模拟中、本地化）时，
 * 手机上嵌套页面包屑的最近上级仍是一个完整的触屏目标，主题与语言收起；
 * 侧栏展开的中等视口里页头比视口窄，控件按页头宽度切换，面包屑首项不收缩、上级与当前页都留得住。
 * 切换工作空间这类上下文控件放在侧栏头部（default-sidebar），不放进页头右侧，否则窄页头会把面包屑挤没。
 */
describe('DefaultHeader on touch phones', () => {
  afterEach(() => setCoarsePointer(false));

  it('keeps the nearest breadcrumb parent a full touch target beside every header control', async () => {
    await setCoarsePointer(true);
    await page.viewport(390, 844);
    onTestFinished(() => page.viewport(1280, 800));
    // 当前页标题很长：上级与标题一起收缩时，上级也不能缩到触屏目标以下
    const host = (await renderHeader()).nativeElement as HTMLElement;

    // 前提：窄屏仍显示的操作区控件都已渲染并占位，否则下面的断言测不到挤压
    // prettier-ignore
    const shown: string[] = [
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
    const title = host.querySelector<HTMLElement>('[data-slot="breadcrumb-page"]')!;
    expect(title.textContent!.trim()).toBe(TITLE);
    // 末级被截断但仍占位、在导航区内：只查文字时，缩到零宽或被裁掉也会通过
    const titleBox = title.getBoundingClientRect();
    expect(titleBox.width).toBeGreaterThan(0);
    expect(titleBox.left).toBeGreaterThanOrEqual(right);
    expect(titleBox.right).toBeLessThanOrEqual(nav.getBoundingClientRect().right + 0.5);

    const container = host.querySelector<HTMLElement>('header > div')!;
    expect(container.clientWidth).toBeLessThanOrEqual(390);
    expect(container.scrollWidth).toBeLessThanOrEqual(container.clientWidth);
  });

  it('switches controls by header width so the breadcrumb stays readable beside an expanded sidebar', async () => {
    await setCoarsePointer(true);
    // 900 宽视口减去展开的侧栏（16rem）：视口断点全部成立，页头只有 636px
    await page.viewport(900, 800);
    onTestFinished(() => page.viewport(1280, 800));
    // 上级很长、当前页较短：上级按内容宽度占大头，不设下限时当前页先被挤到一两个字
    const fixture = await renderHeader(636, {
      parent: 'Northwind Trading Company Limited Asia Pacific Holdings',
      title: 'Contacts and addresses',
    });
    const host = fixture.nativeElement as HTMLElement;

    // 页头内容盒够宽时主题照常显示：断点按页头宽度，不是收进窄屏形态了事
    const theme = host.querySelector('app-theme-mode-toggle button')!;
    expect(theme.getBoundingClientRect().width).toBeGreaterThan(0);

    // 首项不参与收缩、单行：参与收缩时被压到最小内容宽度，中文一字一行、英文一词一行
    const home = host.querySelector<HTMLElement>('[data-slot="breadcrumb-item"]:first-child')!;
    expect(getComputedStyle(home).flexShrink).toBe('0');
    const homeLines = document.createRange();
    homeLines.selectNodeContents(home.querySelector('a')!);
    expect(new Set([...homeLines.getClientRects()].map((r) => Math.round(r.top))).size).toBe(1);

    // 上级与当前页都在导航区内占位；上级先截断，当前页至少留约四个字
    const nav = host.querySelector('app-page-breadcrumb nav')!.getBoundingClientRect();
    const parent = host
      .querySelector('[data-testid="breadcrumb-parent"] a')!
      .getBoundingClientRect();
    const title = host.querySelector('[data-slot="breadcrumb-page"]')!.getBoundingClientRect();
    expect(parent.width).toBeGreaterThan(0);
    expect(title.width).toBeGreaterThanOrEqual(56);
    expect(title.left).toBeGreaterThanOrEqual(parent.right);
    expect(title.right).toBeLessThanOrEqual(nav.right + 0.5);

    // 两级上级时首项后多一个省略号：各项下限之和逼近导航区，当前页仍不能被推出导航区裁掉
    TestBed.inject(LayoutService).trail.update((trail) => [
      { label: 'Customer accounts', link: '/platform/parties' },
      ...trail,
    ]);
    await fixture.whenStable();
    expect(host.querySelector('[data-testid="breadcrumb-more"]')).not.toBeNull();
    const deepNav = host.querySelector('app-page-breadcrumb nav')!.getBoundingClientRect();
    const deepTitle = host.querySelector('[data-slot="breadcrumb-page"]')!.getBoundingClientRect();
    expect(deepTitle.width).toBeGreaterThan(0);
    expect(deepTitle.right).toBeLessThanOrEqual(deepNav.right + 0.5);

    const container = host.querySelector<HTMLElement>('header > div')!;
    expect(container.clientWidth).toBeLessThanOrEqual(636);
    expect(container.scrollWidth).toBeLessThanOrEqual(container.clientWidth);
  });
});

/**
 * 渲染侧栏布局的页头：真实操作区控件全开、嵌套页带一级上级；默认上级短、标题长。
 * `width` 给页头宽度（侧栏展开后小于视口），不给时随视口。
 */
async function renderHeader(
  width?: number,
  crumbs = { parent: 'Customer accounts', title: TITLE },
): Promise<ComponentFixture<DefaultHeader>> {
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
      // 首项给可逐字断行的中文文案：键名回落是一个不可断的词，首项被压也不折行，测不出来
      ...provideTranslocoTesting(['en'], { getTranslation: () => of({ 'menu.platform': '内部管理平台' }) }),
      //#endif
    ],
  });
  await TestBed.inject(Router).navigateByUrl('/platform');
  const layout = TestBed.inject(LayoutService);
  layout.trail.set([{ label: crumbs.parent, link: '/platform/parties' }]);
  layout.title.set(crumbs.title);
  const fixture = TestBed.createComponent(DefaultHeader);
  fixture.componentRef.setInput('layout', 'sidebar');
  if (width) fixture.nativeElement.style.cssText = `display:block;width:${width}px`;
  await fixture.whenStable();
  return fixture;
}
