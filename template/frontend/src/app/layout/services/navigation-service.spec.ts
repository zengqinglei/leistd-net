import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif

import { NavigationService } from './navigation-service';
//#if (IncludeLocalization)
import en from '../../../../public/i18n/en.json';
import { provideTranslocoTesting } from '../../core/i18n/transloco.testing';
//#endif
import { AuthService } from '../../core/services/auth-service';
import { AuthorizationService } from '../../core/services/authorization-service';

const user = signal<{ tenantId: string | null }>({ tenantId: null });

describe('NavigationService scope names', () => {
  async function setup(tenantId: string | null) {
    user.set({ tenantId });
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'platform', children: [] }]),
        { provide: AuthService, useValue: { currentUser: user } },
        {
          provide: AuthorizationService,
          useValue: { hasAny: () => true, canAccessPlatform: () => true },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
      ],
    });
    //#if (IncludeLocalization)
    TestBed.inject(TranslocoService).setTranslation(en, 'en');
    //#endif
    await TestBed.inject(Router).navigateByUrl('/platform');
    return TestBed.inject(NavigationService);
  }
  //#if (IncludeMultiTenancy)

  it('names both-side host entries explicitly and preserves host-only names', async () => {
    const navigation = await setup(null);
    const items = navigation.menuGroups().flatMap((group) => group.items);
    expect(items.find((item) => item.route === '/platform')?.label).toBe('Platform home');
    expect(items.find((item) => item.route === '/platform/users')?.label).toBe('Platform accounts');
    expect(
      items
        .filter((item) => item.side === 'both')
        .every((item) => item.label.startsWith('Platform ')),
    ).toBe(true);
    expect(
      items
        .filter((item) => item.side === 'host')
        .every((item) => !item.label.startsWith('Platform ')),
    ).toBe(true);
    expect(navigation.currentAreaLabel()).toBe('Platform management');
  });

  it('uses original names and administration inside a tenant, with no host-only menus', async () => {
    const navigation = await setup('acme-id');
    const items = navigation.menuGroups().flatMap((group) => group.items);
    expect(items.find((item) => item.route === '/platform/users')?.label).toBe('User management');
    expect(items.some((item) => item.side === 'host')).toBe(false);
    expect(items.map((item) => item.label).join(' ')).not.toMatch(/platform|tenant/i);
    expect(navigation.currentAreaLabel()).toBe('Administration');
  });
  //#else

  it('uses original names without platform or tenant terminology when multitenancy is disabled', async () => {
    const navigation = await setup(null);
    const labels = navigation
      .menuGroups()
      .flatMap((group) => group.items.map((item) => item.label));
    expect(labels).toContain('User management');
    expect(labels.join(' ')).not.toMatch(/platform|tenant/i);
    expect(navigation.currentAreaLabel()).toBe('Administration');
  });
  //#endif
});

@Component({ template: '' })
class Page {}

/**
 * 当前菜单项的判定。列表页的分页、排序、筛选都写在查询参数里，
 * 按完整 URL 比较的话，一翻页侧栏和顶栏的当前项就会丢失。
 */
describe('NavigationService current item', () => {
  let service: NavigationService;
  let router: Router;

  const item = (route: string) => ({ label: route, icon: 'lucideGauge', route });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: 'platform',
            children: [
              { path: '', component: Page },
              { path: 'users', component: Page },
              { path: 'settings/:panel', component: Page },
            ],
          },
          {
            path: 'w/:workspace/platform',
            children: [
              { path: '', component: Page },
              { path: 'users', component: Page },
            ],
          },
        ]),
        {
          provide: AuthorizationService,
          useValue: { hasAny: () => true, canAccessPlatform: signal(true) },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
      ],
    });
    service = TestBed.inject(NavigationService);
    router = TestBed.inject(Router);
  });

  it('keeps a list page current while its query parameters change', async () => {
    const users = item('/platform/users');
    await router.navigateByUrl('/platform/users');
    expect(service.isItemActive(users)).toBe(true);

    // 翻页、筛选只改查询参数与锚点
    await router.navigateByUrl('/platform/users?page=2&isActive=true#top');
    expect(service.isItemActive(users)).toBe(true);
    expect(service.isItemActive(item('/platform'))).toBe(false);

    await router.navigateByUrl('/platform/settings/operations');
    expect(service.isItemActive(users)).toBe(false);
  });

  it('treats sub-pages as part of their menu entry', async () => {
    await router.navigateByUrl('/platform/settings/operations');

    expect(service.isItemActive(item('/platform/settings'))).toBe(true);
  });

  it('matches the platform landing page only exactly', async () => {
    await router.navigateByUrl('/platform');
    expect(service.isItemActive(item('/platform'))).toBe(true);

    await router.navigateByUrl('/platform/users');
    expect(service.isItemActive(item('/platform'))).toBe(false);
  });

  // 租户工作区里菜单路由带 /w/<名称> 前缀：落地页仍只精确匹配，切到其他菜单后不再保持选中
  it('matches the workspace platform landing page only exactly', async () => {
    const landing = item('/w/acme/platform');
    await router.navigateByUrl('/w/acme/platform');
    expect(service.isItemActive(landing)).toBe(true);

    await router.navigateByUrl('/w/acme/platform/users');
    expect(service.isItemActive(landing)).toBe(false);
    expect(service.isItemActive(item('/w/acme/platform/users'))).toBe(true);
  });
});
