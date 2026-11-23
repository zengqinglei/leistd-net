import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
//#if (IncludeLocalization)
import { of } from 'rxjs';
//#endif
import { page } from 'vitest/browser';

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
import { DefaultHeader } from './default-header';

const TITLE = 'Detail of an account with a very long registered company name';

/**
 * 侧栏布局的页头宽度预算：右侧操作区的真实控件全开（通知 99+、模拟中、本地化）时，
 * 手机上嵌套页面包屑的最近上级仍是一个完整的触屏目标，主题与语言收起；
 * 侧栏展开的中等视口里页头比视口窄，控件按页头宽度切换，面包屑首项不收缩、上级与当前页都留得住。
 * 切换工作空间这类上下文控件放在侧栏头部（default-sidebar），不放进页头右侧，否则窄页头会把面包屑挤没。
 */
describe('DefaultHeader on touch phones', () => {
  afterEach(() => setCoarsePointer(false));

  it('drops the breadcrumb on phones so every header control keeps its place', async () => {
    await setCoarsePointer(true);
    await page.viewport(390, 844);
    onTestFinished(() => page.viewport(1280, 800));
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

    // 手机屏宽只剩一两个字的位置：面包屑连同前面的竖分隔线整个不放，而不是留一截被裁的链接或省略号
    expect(widthOf(host, 'app-page-breadcrumb')).toBe(0);
    expect(widthOf(host, 'hlm-separator')).toBe(0);

    const avatar = host.querySelector<HTMLElement>('app-user-menu')!;
    expect(avatar.parentElement!.lastElementChild).toBe(avatar);
    expect(avatar.getBoundingClientRect().width).toBeGreaterThan(0);
    const container = host.querySelector<HTMLElement>('header > div')!;
    expect(container.clientWidth).toBeLessThanOrEqual(390);
    expect(container.scrollWidth).toBeLessThanOrEqual(container.clientWidth);
  });

  it('keeps the parent a full touch target and the title readable beside an expanded sidebar on tablets', async () => {
    await setCoarsePointer(true);
    // 769 宽视口减去展开的侧栏：视口断点 sm 成立，页头只有 505px；首项与省略号要按面包屑宽度收起
    await page.viewport(769, 800);
    onTestFinished(() => page.viewport(1280, 800));
    const fixture = await renderHeader(505);
    TestBed.inject(LayoutService).trail.update((trail) => [
      { label: 'Customer accounts', link: '/platform/parties' },
      ...trail,
    ]);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(widthOf(host, '[data-testid="breadcrumb-home"]')).toBe(0);
    expect(widthOf(host, '[data-testid="breadcrumb-more"]')).toBe(0);
    const parent = host.querySelector<HTMLElement>('[data-testid="breadcrumb-parent"] a')!;
    expectTouchTarget(parent);
    // 条目按链接的触屏宽度占位：条目缩得比链接窄时，链接伸进间距，再窄就被下一个分隔符盖住
    const right = parent.getBoundingClientRect().right;
    const item = host.querySelector('[data-testid="breadcrumb-parent"]')!;
    expect(right).toBeLessThanOrEqual(item.getBoundingClientRect().right + 0.5);
    // 导航区 overflow-hidden：链接伸出右缘的部分会被裁掉
    const nav = host.querySelector('app-page-breadcrumb nav')!.getBoundingClientRect();
    expect(right).toBeLessThanOrEqual(nav.right + 0.5);
    const title = host.querySelector('[data-slot="breadcrumb-page"]')!.getBoundingClientRect();
    expect(title.width).toBeGreaterThanOrEqual(56);
    expect(title.left).toBeGreaterThanOrEqual(right);
    expect(title.right).toBeLessThanOrEqual(nav.right + 0.5);
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

    // 新增搜索与头像后，636px 页头收起主题与语言，给面包屑留位；断点按页头宽度。
    const theme = host.querySelector('app-theme-mode-toggle button')!;
    expect(theme.getBoundingClientRect().width).toBe(0);

    // 面包屑不足 20rem：首项收起，只留「上级 > 当前页」
    expect(widthOf(host, '[data-testid="breadcrumb-home"]')).toBe(0);

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

    // 两级上级：省略号随首项收起，当前页仍不能被推出导航区裁掉
    TestBed.inject(LayoutService).trail.update((trail) => [
      { label: 'Customer accounts', link: '/platform/parties' },
      ...trail,
    ]);
    await fixture.whenStable();
    expect(widthOf(host, '[data-testid="breadcrumb-more"]')).toBe(0);
    const deepNav = host.querySelector('app-page-breadcrumb nav')!.getBoundingClientRect();
    const deepTitle = host.querySelector('[data-slot="breadcrumb-page"]')!.getBoundingClientRect();
    // 当前页仍留下约四个字，而不是被挤到只剩零宽（Release 曾在此处为 0）
    expect(deepTitle.width).toBeGreaterThanOrEqual(56);
    expect(deepTitle.right).toBeLessThanOrEqual(deepNav.right + 0.5);

    const avatar = host.querySelector<HTMLElement>('app-user-menu')!;
    expect(avatar.parentElement!.lastElementChild).toBe(avatar);
    expect(avatar.getBoundingClientRect().width).toBeGreaterThan(0);
    const container = host.querySelector<HTMLElement>('header > div')!;
    expect(container.clientWidth).toBeLessThanOrEqual(636);
    expect(container.scrollWidth).toBeLessThanOrEqual(container.clientWidth);
  });

  it('shows the home crumb and collapsed parents once the breadcrumb is wide enough', async () => {
    await setCoarsePointer(true);
    await page.viewport(1024, 800);
    onTestFinished(() => page.viewport(1280, 800));
    const fixture = await renderHeader(760);
    TestBed.inject(LayoutService).trail.update((trail) => [
      { label: 'Customer accounts', link: '/platform/parties' },
      ...trail,
    ]);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    const home = host.querySelector<HTMLElement>('[data-testid="breadcrumb-home"]')!;
    // 首项收缩时单行截断，不折行：文字不包 truncate 时会被压到最小内容宽度，中文一字一行、英文一词一行
    const homeLines = document.createRange();
    homeLines.selectNodeContents(home.querySelector('a span')!);
    expect(new Set([...homeLines.getClientRects()].map((r) => Math.round(r.top))).size).toBe(1);

    expectTouchTarget(home.querySelector('a')!);
    expectTouchTarget(host.querySelector<HTMLElement>('[data-testid="breadcrumb-more"]')!);
    expectTouchTarget(host.querySelector<HTMLElement>('[data-testid="breadcrumb-parent"] a')!);
    const nav = host.querySelector('app-page-breadcrumb nav')!.getBoundingClientRect();
    const title = host.querySelector('[data-slot="breadcrumb-page"]')!.getBoundingClientRect();
    expect(title.width).toBeGreaterThanOrEqual(56);
    expect(title.right).toBeLessThanOrEqual(nav.right + 0.5);
  });

  it('keeps the parent legible beside a long title with a mouse', async () => {
    // 细指针没有 44px 命中区撑着：只靠收缩权重时，长标题会把上级压成零宽，只剩两个相邻分隔符
    await page.viewport(900, 800);
    onTestFinished(() => page.viewport(1280, 800));
    const host = (await renderHeader(636)).nativeElement as HTMLElement;

    const parent = host.querySelector('[data-testid="breadcrumb-parent"]')!.getBoundingClientRect();
    expect(parent.width).toBeGreaterThanOrEqual(44);
    const nav = host.querySelector('app-page-breadcrumb nav')!.getBoundingClientRect();
    const title = host.querySelector('[data-slot="breadcrumb-page"]')!.getBoundingClientRect();
    expect(title.width).toBeGreaterThanOrEqual(56);
    expect(title.right).toBeLessThanOrEqual(nav.right + 0.5);
  });
});

/** 元素的渲染宽度；收起（display: none）的条目为 0。 */
function widthOf(host: HTMLElement, selector: string): number {
  return host.querySelector(selector)!.getBoundingClientRect().width;
}

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
      { provide: AuthService, useValue: { isAuthenticated: () => true, currentUser: signal({ id: 'u-1', username: 'admin' }) } },
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
      ...provideTranslocoTesting(['en'], { getTranslation: () => of({ 'menu.platform': '内部管理后台', 'menu.platformManagement': '内部平台管理' }) }),
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
