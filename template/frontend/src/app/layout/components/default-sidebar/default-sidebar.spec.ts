import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { Translation, TranslocoService } from '@jsverse/transloco';
//#endif
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
//#if (IncludeLocalization)
import { Subject } from 'rxjs';
//#endif

import { DefaultSidebar } from './default-sidebar';
//#if (IncludeLocalization)
import enTexts from '../../../../../public/i18n/en.json';
//#endif
//#if (IncludeLocalization)
import { lineCount, pseudoCatalog } from '../../../core/i18n/pseudo-locale.testing';
//#endif
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
//#if (TenantWorkspace)
import { AuthService } from '../../../core/services/auth-service';
//#endif
import { AuthorizationService } from '../../../core/services/authorization-service';
import { LayoutService } from '../../../core/services/layout-service';
//#if (OpenIddictServer || RemoteTokenAuth)
import { SystemDirectoryService } from '../../../core/services/system-directory-service';
//#endif
//#if (TenantWorkspace)
import { WorkspaceContextService } from '../../../core/services/workspace-context-service';
//#endif
import {
  PERMISSIONS,
  PLATFORM_ENTRY_PERMISSIONS,
} from '../../../shared/constants/permission.constants';
import { expectTouchTarget, setCoarsePointer } from '../../../shared/utils/coarse-pointer.testing';

//#if (IncludeLocalization)
// 空词条下 translate() 回落成键名，所以这里按**键**断言：分组骨架与文案无关，
// 改一句中文不该让这组用例变红。
const WORK = 'layout.sidebar.groupWork';
const BUSINESS = 'layout.sidebar.groupBusiness';
const IDENTITY = 'layout.sidebar.groupIdentity';
//#if (OpenIddictServer || IncludeWebhooks)
const INTEGRATIONS = 'layout.sidebar.groupIntegrations';
//#endif
//#if (IncludeOperationRecords)
const AUDIT = 'layout.sidebar.groupAudit';
//#endif
const SYSTEM = 'layout.sidebar.groupSystem';
//#else
const WORK = 'Work';
const BUSINESS = 'Business';
const IDENTITY = 'Identity & access';
//#if (OpenIddictServer || IncludeWebhooks)
const INTEGRATIONS = 'Integrations';
//#endif
//#if (IncludeOperationRecords)
const AUDIT = 'Audit';
//#endif
const SYSTEM = 'System';
//#endif

/**
 * 侧栏菜单的信息架构：钉住两个区的分组骨架、不留兜底组、个人设置只在工作空间、
 * 权限不足的组整组消失。
 */
describe('DefaultSidebar menu groups', () => {
  /** 只构造类，不渲染模板：分组是类上的 computed，模板依赖与信息架构无关的 sidebar 上下文。 */
  function build(options: { platform: boolean; permissions?: readonly string[] }): DefaultSidebar {
    const granted = new Set(options.permissions ?? []);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        // 真实 LayoutService 的 isPlatform 由路由派生，要靠导航才能切换；
        // 本组用例要的只是"当前在哪个区"这一个输入。
        {
          provide: LayoutService,
          useValue: {
            isPlatform: signal(options.platform),
            isHostScope: signal(options.platform),
            currentUrl: signal(options.platform ? '/platform' : '/workspace/dashboard'),
          },
        },
        {
          provide: AuthorizationService,
          useValue: {
            hasAny: (...permissions: string[]) => permissions.some((p) => granted.has(p)),
            canAccessPlatform: () => options.platform,
          },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
      ],
    });

    return TestBed.runInInjectionContext(() => new DefaultSidebar());
  }

  const groupsOf = (sidebar: DefaultSidebar) => sidebar.menuGroups().map((group) => group.label);
  const routesOf = (sidebar: DefaultSidebar) =>
    sidebar.menuGroups().flatMap((group) => group.items.map((item) => item.route));

  it('orders workspace groups as work → business with no fallback group', () => {
    const sidebar = build({ platform: false });

    expect(groupsOf(sidebar)).toEqual([WORK, BUSINESS]);
    expect(routesOf(sidebar)).toEqual(['/workspace/dashboard', '/workspace/placeholder']);
  });

  // 个人设置只有头像菜单一个入口；工作空间侧由上一条的精确 toEqual 覆盖，这里只查平台侧。
  it('leaves the personal settings entry out of the menu tree', () => {
    const everything = Object.values(PERMISSIONS).map((group) => group.default);

    expect(routesOf(build({ platform: true, permissions: everything }))).not.toContain(
      '/workspace/settings',
    );
  });

  it('orders platform groups work → identity & access → developer → audit → system', () => {
    const permissions = [
      PERMISSIONS.users.default,
      PERMISSIONS.roles.default,
      PERMISSIONS.settings.default,
      //#if (IncludeOperationRecords)
      PERMISSIONS.operationRecords.default,
      //#endif
      //#if (LocalIdentity && IncludeMultiTenancy)
      PERMISSIONS.tenants.default,
      //#endif
      //#if (OpenIddictServer)
      PERMISSIONS.openApplications.default,
      //#endif
      //#if (IncludeWebhooks)
      PERMISSIONS.webhooks.default,
      //#endif
    ];
    const expected = [WORK, IDENTITY];
    //#if (OpenIddictServer || IncludeWebhooks)
    expected.push(INTEGRATIONS);
    //#endif
    //#if (IncludeOperationRecords)
    expected.push(AUDIT);
    //#endif
    expected.push(SYSTEM);

    const sidebar = build({ platform: true, permissions });

    expect(groupsOf(sidebar)).toEqual(expected);
    expect(routesOf(sidebar)).toContain('/platform/settings');
  });

  // 能看到任一平台菜单项的账号都必须进得了 /platform，反之入口权限也不能放进一个看不到任何菜单的空平台：
  // 少一项，那个岗位的账号菜单为空、还会被重定向走；多一项，进来只看到工作组。
  it('matches platform entry permissions one-to-one with platform menu items', () => {
    const everything = Object.values(PERMISSIONS).map((group) => group.default);
    const allRoutes = routesOf(build({ platform: true, permissions: everything }));
    TestBed.resetTestingModule();

    expect(routesOf(build({ platform: true, permissions: PLATFORM_ENTRY_PERMISSIONS }))).toEqual(
      allRoutes,
    );
    // 仪表盘随平台常驻，它的权限只决定统计是否可见；其余入口权限各自带出一个菜单项
    for (const permission of PLATFORM_ENTRY_PERMISSIONS) {
      TestBed.resetTestingModule();
      expect(
        routesOf(build({ platform: true, permissions: [permission] })).length,
        permission,
      ).toBeGreaterThan(permission === PERMISSIONS.dashboard.default ? 0 : 1);
    }
  });

  // 权限不足时整组消失，不留一个空标题——空标题看起来像加载失败。
  it('shows only the work group on the platform without admin permissions', () => {
    const sidebar = build({ platform: true });

    expect(groupsOf(sidebar)).toEqual([WORK]);
    expect(routesOf(sidebar)).toEqual(['/platform']);
  });
});
//#if (IncludeLocalization)

/**
 * 菜单文案随词条到达更新：首次读菜单时词条可能还在路上，靠同步 translate() 会把键名
 * 留在菜单上。
 */
describe('DefaultSidebar menu labels', () => {
  it('shows the translated labels once translations arrive after the first read', () => {
    const translations = new Subject<Translation>();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: LayoutService,
          useValue: {
            isPlatform: signal(false),
            isHostScope: signal(false),
            currentUrl: signal('/workspace/dashboard'),
          },
        },
        {
          provide: AuthorizationService,
          useValue: { hasAny: () => true, canAccessPlatform: () => false },
        },
        ...provideTranslocoTesting(['en'], { getTranslation: () => translations }),
      ],
    });
    const sidebar = TestBed.runInInjectionContext(() => new DefaultSidebar());
    const labels = () => sidebar.menuGroups().map((group) => group.label);
    expect(labels()).not.toContain('Work');

    translations.next({ layout: { sidebar: { groupWork: 'Work', groupBusiness: 'Business' } } });
    translations.complete();

    expect(labels()).toEqual(['Work', 'Business']);
  });
});
//#endif

/** 与默认布局同样的包法：侧栏放在 hlmSidebarWrapper 里，宽度变量与折叠状态都从这里来。 */
@Component({
  selector: 'app-test-sidebar-host',
  imports: [DefaultSidebar, ...HlmSidebarImports],
  template: `<div hlmSidebarWrapper class="h-svh"><app-default-sidebar /></div>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class SidebarHost {}

//#if (IncludeLocalization)
/**
 * 伪语言按标签容量放宽文案（frontend-i18n.md §5）：展开的侧栏里分组标题与菜单项仍完整放下；
 * 品牌块的应用名随项目改名、长度不定，放不下时单行截断，不撑出侧栏。
 */
describe('DefaultSidebar pseudo locale', () => {
  for (const platform of [true, false]) {
    it(`fits the ${platform ? 'platform' : 'workspace'} menu into the expanded sidebar`, async () => {
      TestBed.configureTestingModule({
        // prettier-ignore
        providers: [
          provideRouter([]),
          provideHttpClient(),
          provideHttpClientTesting(),
        //#if (OpenIddictServer || RemoteTokenAuth)
        { provide: SystemDirectoryService, useValue: { systems: signal([]), switchTo: vi.fn() } },
        //#endif
          {
            provide: LayoutService,
            useValue: {
              isPlatform: signal(platform),
              isHostScope: signal(platform),
              currentUrl: signal(platform ? '/platform' : '/workspace/dashboard'),
              homeRoute: signal('/workspace/dashboard'),
            },
          },
          {
            provide: AuthorizationService,
            useValue: { hasAny: () => true, canAccessPlatform: () => true },
          },
          ...provideTranslocoTesting(),
        ],
      });
      TestBed.inject(TranslocoService).setTranslation(pseudoCatalog(enTexts), 'en');
      const fixture = TestBed.createComponent(SidebarHost);
      TestBed.inject(HlmSidebarService).setOpen(true);
      fixture.autoDetectChanges();
      await fixture.whenStable();
      const host = fixture.nativeElement as HTMLElement;

      const sidebar = Array.from(
        host.querySelectorAll<HTMLElement>('[data-sidebar="sidebar"]'),
      ).find((panel) => panel.getBoundingClientRect().width > 0)!;
      const labels = Array.from(
        sidebar.querySelectorAll<HTMLElement>(
          '[data-sidebar="group-label"], [data-slot="sidebar-menu-button"] > span:last-child',
        ),
      );
      expect(labels.length).toBeGreaterThan(0);
      for (const label of labels) {
        const text = label.textContent!.trim();
        // 文案来自词条：伪语言的方括号在，写死的文案没有
        expect(text).toMatch(/^\[.+\]$/);
        expect(label.getBoundingClientRect().width, text).toBeGreaterThan(0);
        // 完整放下：没被截断，也没折成两行
        if (label.scrollWidth > label.clientWidth)
          expect(getComputedStyle(label).textOverflow).toBe('ellipsis');
        expect(lineCount(label), text).toBe(1);
      }

      // 单系统品牌块只保留应用名，不再显示区域。
      const brand = Array.from(
        sidebar.querySelectorAll<HTMLElement>('[data-testid="system-home"] > span'),
      );
      expect(brand).toHaveLength(1);
      for (const line of brand) {
        const box = line.getBoundingClientRect();
        expect(box.width > 0 && box.height > 0, line.textContent!).toBe(true);
        expect(line.textContent!.trim()).toMatch(/^\[.+\]$/);
        expect(lineCount(line), line.textContent!).toBe(1);
      }
      expect(sidebar.scrollWidth).toBeLessThanOrEqual(sidebar.clientWidth);
    });
  }
});

//#endif
/** 折叠成图标轨时按钮原本只有 32px 见方；触屏下侧栏加宽，按钮宽高都到 44px 且整块可点。 */
describe('DefaultSidebar touch targets', () => {
  afterEach(() => setCoarsePointer(false));

  it('keeps every collapsed menu button a full 44px target on touch screens', async () => {
    await setCoarsePointer(true);
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (OpenIddictServer || RemoteTokenAuth)
        { provide: SystemDirectoryService, useValue: { systems: signal([]), switchTo: vi.fn() } },
        //#endif
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
      ],
    });
    const fixture = TestBed.createComponent(SidebarHost);
    TestBed.inject(HlmSidebarService).setOpen(false);
    fixture.detectChanges();
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('[data-collapsible="icon"]')).not.toBeNull();
    const buttons = host.querySelectorAll('[data-slot="sidebar-menu-button"]');
    expect(buttons.length).toBeGreaterThan(0);
    buttons.forEach(expectTouchTarget);
    // 只露图标：按钮放大后，没收起的文字会从图标旁边露出来
    const labels = Array.from(host.querySelectorAll('[data-slot="sidebar-menu-button"] > span'));
    expect(labels.filter((label) => label.getBoundingClientRect().width > 0)).toEqual([]);
    expect(host.querySelector('[data-testid="sidebar-search"]')).toBeNull();
    expect(host.querySelector('[data-testid="user-menu-trigger"]')).toBeNull();
  });
});
//#if (TenantWorkspace)

describe('DefaultSidebar tenant selection and platform management', () => {
  async function setup(current: string | null, count = 2, tenantId: string | null = null) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
        { provide: AuthService, useValue: { currentUser: signal({ id: 'person', tenantId }) } },
        {
          provide: AuthorizationService,
          useValue: { hasAny: () => true, canAccessPlatform: () => true },
        },
        //#if (OpenIddictServer || RemoteTokenAuth)
        { provide: SystemDirectoryService, useValue: { systems: signal([]), switchTo: vi.fn() } },
        //#endif
      ],
    });
    //#if (IncludeLocalization)
    TestBed.inject(TranslocoService).setTranslation(enTexts, 'en');
    //#endif
    const workspace = TestBed.inject(WorkspaceContextService);
    workspace.workspaces.set(
      [
        { userId: 'member-acme', tenantId: 'acme-id', name: 'acme' },
        { userId: 'member-globex', tenantId: 'globex-id', name: 'globex' },
      ].slice(0, count),
    );
    workspace.current.set(current);
    workspace.canAccessConsole.set(true);
    const fixture = TestBed.createComponent(SidebarHost);
    const sidebar = TestBed.inject(HlmSidebarService);
    sidebar.setOpen(true);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement, sidebar, workspace };
  }

  for (const scenario of [
    {
      name: 'staff in a tenant',
      current: 'acme',
      count: 2,
      tenantId: null,
      selector: true,
      platform: true,
      back: false,
    },
    {
      name: 'staff in platform management',
      current: null,
      count: 2,
      tenantId: null,
      selector: false,
      platform: false,
      back: true,
    },
    {
      name: 'staff with one workspace',
      current: 'acme',
      count: 1,
      tenantId: null,
      selector: false,
      platform: true,
      back: false,
    },
    {
      name: 'a tenant user',
      current: 'acme',
      count: 2,
      tenantId: 'acme-id',
      selector: false,
      platform: false,
      back: false,
    },
  ]) {
    it(`shows only the appropriate controls for ${scenario.name}`, async () => {
      const { host } = await setup(scenario.current, scenario.count, scenario.tenantId);
      expect(!!host.querySelector('[data-testid="tenant-selector"]')).toBe(scenario.selector);
      expect(!!host.querySelector('[data-testid="platform-management"]')).toBe(scenario.platform);
      expect(!!host.querySelector('[data-testid="return-workspace"]')).toBe(scenario.back);
      expect(host.querySelector('[data-testid="area-switcher"]')).toBeNull();
    });
  }

  for (const dark of [false, true]) {
    it(`renders the context size and semantic colors in ${dark ? 'dark' : 'light'} mode`, async () => {
      document.documentElement.classList.toggle('dark', dark);
      try {
        const { fixture, host, workspace } = await setup('acme');
        const picker = host.querySelector<HTMLElement>('[data-testid="tenant-selector"]')!;
        expect(picker.getBoundingClientRect().height).toBe(32);
        expect(picker.querySelector('ng-icon[name="lucideBuilding2"]')).not.toBeNull();
        expect(picker.querySelector('ng-icon[name="lucideChevronsUpDown"]')).not.toBeNull();
        const entry = host.querySelector<HTMLElement>('[data-testid="platform-management"]')!;
        expect(entry.getBoundingClientRect().height).toBe(40);
        expect(entry.querySelector('div')!.getBoundingClientRect().width).toBe(26);
        expect(entry.querySelector('ng-icon[name="lucideShieldCheck"]')).not.toBeNull();
        expect(entry.querySelector('ng-icon[name="lucideChevronRight"]')).not.toBeNull();
        const entryBackground = getComputedStyle(entry.querySelector('div')!).backgroundColor;
        workspace.current.set(null);
        await fixture.whenStable();
        const context = host.querySelector<HTMLElement>('[data-testid="platform-context"]')!;
        expect(context.getBoundingClientRect().height).toBe(32);
        expect(context.tabIndex).toBe(-1);
        expect(context.querySelector('button, a, [tabindex]')).toBeNull();
        const shield = context.querySelector('ng-icon[name="lucideShieldCheck"]')!;
        expect(shield.getBoundingClientRect().width).toBe(16);
        expect(shield.getBoundingClientRect().height).toBe(16);
        expect(getComputedStyle(context).backgroundColor).toBe(entryBackground);
        expect(
          host.querySelector(
            '[data-testid="return-workspace"] ng-icon[name="lucideCornerDownLeft"]',
          ),
        ).not.toBeNull();
      } finally {
        document.documentElement.classList.remove('dark');
      }
    });
  }

  it('expands from the tenant icon, opens the same picker and keeps switching behavior', async () => {
    const { fixture, host, sidebar, workspace } = await setup('acme');
    sidebar.setOpen(false);
    await fixture.whenStable();
    const icon = host.querySelector<HTMLButtonElement>('[data-testid="tenant-context-icon"]')!;
    expect(icon.getBoundingClientRect().width).toBe(32);
    expect(icon.getAttribute('aria-label')).toBe('acme');
    expect(
      host.querySelector('[data-testid=platform-management]')?.getAttribute('aria-label'),
    ).toBeTruthy();
    icon.click();
    await fixture.whenStable();
    await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
    await fixture.whenStable();
    expect(sidebar.state()).toBe('expanded');
    expect(document.querySelector('[role="listbox"]')).not.toBeNull();
    const switchTo = vi.spyOn(workspace, 'switchTo').mockImplementation(() => undefined);
    const option = Array.from(document.querySelectorAll<HTMLElement>('[role="option"]')).find(
      (item) => item.textContent?.includes('globex'),
    )!;
    option.click();
    await fixture.whenStable();
    expect(switchTo).toHaveBeenCalledWith('globex');
  });
});
//#endif
