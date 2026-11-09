import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { page, userEvent } from 'vitest/browser';

import { PageBreadcrumb } from './page-breadcrumb';
//#if (IncludeLocalization)
import enTexts from '../../../../../public/i18n/en.json';
import { lineCount, pseudoCatalog } from '../../../core/i18n/pseudo-locale.testing';
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import { LayoutCrumb, LayoutService } from '../../../core/services/layout-service';
import {
  expectTouchTarget,
  setCoarsePointer,
  tapAt,
} from '../../../shared/utils/coarse-pointer.testing';

/**
 * 面包屑按「首页 > 上级… > 当前页」渲染：两级以上上级只留最近一级、其余收进省略号下拉，
 * 窄屏只留最近一级上级与当前页。可见性按真实视口下的计算样式断言。
 */
describe('PageBreadcrumb', () => {
  const CENTRE: LayoutCrumb = { label: 'Brand centre', link: '/platform/brand-center' };
  const BRAND: LayoutCrumb = { label: 'Spec Markets', link: ['/platform/brand-center', 'spec'] };

  /** 渲染面包屑；给了 `width` 时把它限制在这个宽度里（页头里留给面包屑的那一段）。 */
  async function render(
    trail: readonly LayoutCrumb[],
    title = 'Messages',
    width?: number,
  ): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        // 首级链接按当前区段取首页，先停在平台区段
        provideRouter([{ path: 'platform', children: [] }]),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
      ],
    });
    await TestBed.inject(Router).navigateByUrl('/platform');
    const layout = TestBed.inject(LayoutService);
    layout.trail.set(trail);
    layout.title.set(title);
    const fixture = TestBed.createComponent(PageBreadcrumb);
    const host = fixture.nativeElement as HTMLElement;
    if (width !== undefined) host.style.width = `${width}px`;
    await fixture.whenStable();
    return host;
  }

  /** 当前视口下看得见的条目文字与链接，按文档顺序。 */
  function visibleItems(host: HTMLElement): string[] {
    return [...host.querySelectorAll<HTMLElement>('[data-slot="breadcrumb-item"]')]
      .filter((item) => getComputedStyle(item).display !== 'none')
      .map((item) => {
        const link = item.querySelector('a');
        if (link) return `${link.textContent?.trim()} ${link.getAttribute('href')}`;
        if (item.querySelector('[data-slot="breadcrumb-ellipsis"]')) return '…';
        return item.textContent!.trim();
      });
  }

  function visibleSeparators(host: HTMLElement): number {
    return [...host.querySelectorAll('[data-slot="breadcrumb-separator"]')].filter(
      (separator) => getComputedStyle(separator).display !== 'none',
    ).length;
  }

  //#if (IncludeLocalization)
  const HOME = 'menu.platform /platform';
  //#else
  const HOME = 'Admin platform /platform';
  //#endif

  it('renders home and the page title for a top-level page', async () => {
    const host = await render([], 'Users');

    expect(visibleItems(host)).toEqual([HOME, 'Users']);
    expect(visibleSeparators(host)).toBe(1);
  });

  it('shows a single parent between home and the page title', async () => {
    const host = await render([CENTRE]);

    expect(visibleItems(host)).toEqual([HOME, 'Brand centre /platform/brand-center', 'Messages']);
    expect(visibleSeparators(host)).toBe(2);
  });

  it('collapses farther parents into the ellipsis menu and keeps the nearest one', async () => {
    const host = await render([CENTRE, BRAND]);

    expect(visibleItems(host)).toEqual([
      HOME,
      '…',
      'Spec Markets /platform/brand-center/spec',
      'Messages',
    ]);
    expect(visibleSeparators(host)).toBe(3);

    await userEvent.click(
      host.querySelector<HTMLButtonElement>('[data-testid="breadcrumb-more"]')!,
    );
    const body = host.ownerDocument.body;
    await vi.waitFor(() => expect(body.querySelector('[role="menu"]')).not.toBeNull());
    const entries = [...body.querySelectorAll<HTMLAnchorElement>('[role="menu"] a')];
    expect(
      entries.map((entry) => `${entry.textContent?.trim()} ${entry.getAttribute('href')}`),
    ).toEqual(['Brand centre /platform/brand-center']);
    body.querySelectorAll('.cdk-overlay-container').forEach((container) => container.remove());
  });

  it('keeps only the nearest parent and the page title on phones', async () => {
    await page.viewport(390, 844);
    onTestFinished(() => page.viewport(1280, 800));

    const nested = await render([CENTRE, BRAND]);
    expect(visibleItems(nested)).toEqual(['Spec Markets /platform/brand-center/spec', 'Messages']);
    expect(visibleSeparators(nested)).toBe(1);

    TestBed.resetTestingModule();
    const topLevel = await render([], 'Users');
    expect(visibleItems(topLevel)).toEqual(['Users']);
    expect(visibleSeparators(topLevel)).toBe(0);
  });

  it('names the navigation landmark from the localized label', async () => {
    const host = await render([]);
    //#if (IncludeLocalization)
    expect(host.querySelector('nav')!.getAttribute('aria-label')).toBe('layout.breadcrumb.label');
    //#else
    expect(host.querySelector('nav')!.getAttribute('aria-label')).toBe('Breadcrumb');
    //#endif
  });

  /** 放不下时上级与当前页单行截断，不撑出导航区；截断的当前页可聚焦，提示全文。 */
  it('truncates long labels on one line and shows the full page title on focus', async () => {
    const longParent = 'Customer relationship accounts and their contact people';
    const longTitle = 'Detail of an account with a very long registered company name';
    const host = await render([{ label: longParent, link: '/platform/parties' }], longTitle, 320);
    const nav = host.querySelector('nav')!;
    const parentText = host.querySelector<HTMLElement>('[data-testid="breadcrumb-parent"] span')!;
    const pageText = host.querySelector<HTMLElement>('[data-slot="breadcrumb-page"]')!;

    expect(nav.scrollWidth).toBeLessThanOrEqual(nav.clientWidth);
    // 横向溢出只在不折行时出现：既证明单行，也证明被截断
    for (const text of [parentText, pageText]) {
      expect(text.scrollWidth, text.textContent!).toBeGreaterThan(text.clientWidth);
    }

    await vi.waitFor(() => expect(pageText.getAttribute('tabindex')).toBe('0'));
    pageText.focus();
    await expect
      .poll(() => document.querySelector('[role="tooltip"]')?.textContent?.trim() ?? '')
      .toBe(longTitle);
    pageText.blur();
  });

  describe('touch targets', () => {
    afterEach(() => setCoarsePointer(false));

    it('gives the links and the ellipsis 44px targets on touch screens', async () => {
      await setCoarsePointer(true);
      const host = await render([CENTRE, BRAND]);
      const links = [...host.querySelectorAll('a[data-slot="breadcrumb-link"]')];
      expect(links.length).toBe(2);
      links.forEach(expectTouchTarget);

      const more = host.querySelector<HTMLButtonElement>('[data-testid="breadcrumb-more"]')!;
      expectTouchTarget(more);
      // 点在扩出的命中区边缘也能打开下拉
      await tapAt(more, 20, 0);
      await vi.waitFor(() => expect(document.querySelector('[role="menu"]')).not.toBeNull());
      await Promise.all(document.getAnimations().map((animation) => animation.finished));
      document.querySelectorAll('[role="menu"] a').forEach(expectTouchTarget);
      document
        .querySelectorAll('.cdk-overlay-container')
        .forEach((container) => container.remove());
    });
  });
  //#if (IncludeLocalization)

  /** 伪语言放宽首级与读屏文案（frontend-i18n.md §5）：平板宽度下仍单行、不超出导航区。 */
  it('stays on one line within the navigation in the pseudo locale', async () => {
    const pseudo = pseudoCatalog(enTexts);
    const host = await render([CENTRE, BRAND], 'Messages', 480);
    TestBed.inject(TranslocoService).setTranslation(pseudo, 'en');
    await vi.waitFor(() => expect(host.textContent).toContain(pseudo.menu.platform));

    const nav = host.querySelector('nav')!;
    expect(nav.scrollWidth).toBeLessThanOrEqual(nav.clientWidth);
    const home = host.querySelector<HTMLElement>('a[data-slot="breadcrumb-link"]')!;
    expect(lineCount(home)).toBe(1);
    expect(getComputedStyle(home).display).not.toBe('none');
    expect(host.querySelector('[data-slot="breadcrumb-ellipsis"]')!.textContent).toContain(
      pseudo.layout.breadcrumb.more,
    );
  });
  //#endif
});
