import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthorizationService } from './authorization-service';
import { routes } from '../../app.routes';
import { PERMISSIONS, PLATFORM_ENTRY_PERMISSIONS } from '../../shared/models/permission';

/**
 * 前端可见性的唯一判据。
 *
 * 这里锁住的是"不存在第二套放行规则"：超级管理员标记只是展示信息，
 * 不参与 has/hasAny/canAccessPlatform 的判定。前端多一套语义，界面就会与后端分叉——
 * 菜单里看得见、点进去 403。
 */
describe('AuthorizationService', () => {
  let service: AuthorizationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(AuthorizationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('未加载前一律按无权限处理，避免闪现受保护入口', () => {
    expect(service.loaded()).toBeFalse();
    expect(service.has(PERMISSIONS.users.default)).toBeFalse();
    expect(service.canAccessPlatform()).toBeFalse();
  });

  it('setPermissions 之后 has / hasAny / hasAll 一致生效', () => {
    service.setPermissions({
      permissions: [PERMISSIONS.users.default, PERMISSIONS.users.create],
      isSuperAdmin: false,
      versionToken: 'r1',
    });

    expect(service.loaded()).toBeTrue();
    expect(service.versionToken()).toBe('r1');

    expect(service.has(PERMISSIONS.users.default)).toBeTrue();
    expect(service.has(PERMISSIONS.roles.default)).toBeFalse();

    expect(service.hasAny(PERMISSIONS.roles.default, PERMISSIONS.users.create)).toBeTrue();
    expect(service.hasAny(PERMISSIONS.roles.default)).toBeFalse();

    expect(service.hasAll(PERMISSIONS.users.default, PERMISSIONS.users.create)).toBeTrue();
    expect(service.hasAll(PERMISSIONS.users.default, PERMISSIONS.roles.default)).toBeFalse();
  });

  it('超级管理员标记不构成第二套放行规则', () => {
    service.setPermissions({ permissions: [], isSuperAdmin: true, versionToken: 'r1' });

    expect(service.isSuperAdmin()).toBeTrue();

    // 标记是展示信息；能不能看由权限集合决定，与后端同一判据。
    expect(service.has(PERMISSIONS.users.default)).toBeFalse();
    expect(service.canAccessPlatform()).toBeFalse();
  });

  it('拥有任一平台入口权限即可进入平台区', () => {
    service.setPermissions({
      permissions: [PERMISSIONS.roles.default],
      isSuperAdmin: false,
      versionToken: 'r1',
    });

    expect(service.canAccessPlatform()).toBeTrue();
  });

  /**
   * 路由守卫与菜单/重定向必须用同一份权限清单。
   *
   * 两处曾各自硬编码，在多租户场景下不等价：路由含 tenants、canAccessPlatform 不含，
   * 于是只有租户管理权限的账号菜单里没有入口、登录后被重定向走，但直接敲 URL 能进。
   * 只把两处改成引用同一常量还不够——下一个人仍可能在路由里手写补一项，
   * 所以这里断言"同源"，让分叉在 CI 里立刻失败。
   */
  it('/platform 路由白名单与 canAccessPlatform 同源', () => {
    const platformRoute = routes.find((route) => route.path === 'platform');

    expect(platformRoute).withContext('/platform 路由不存在').toBeDefined();
    expect(platformRoute?.data?.['permissions']).toEqual([...PLATFORM_ENTRY_PERMISSIONS]);
  });

  /**
   * 平台入口权限集里的**每一项**都要能单独放行。
   *
   * 上一条锁住两处同源，但同源的清单若漏了某个模块，那个模块的专属角色照样进不去。
   * 这一条逐项验证，新增模块时忘记加入集合就会红。
   */
  it('平台入口权限集里每一项都能单独放行', () => {
    for (const permission of PLATFORM_ENTRY_PERMISSIONS) {
      service.setPermissions({
        permissions: [permission],
        isSuperAdmin: false,
        versionToken: 'r1',
      });

      expect(service.canAccessPlatform())
        .withContext(`仅持有 ${permission} 时应可进入平台区`)
        .toBeTrue();
    }
  });

  it('clear 之后回到未加载状态', () => {
    service.setPermissions({
      permissions: [PERMISSIONS.users.default],
      isSuperAdmin: true,
      versionToken: 'r1',
    });

    service.clear();

    expect(service.loaded()).toBeFalse();
    expect(service.isSuperAdmin()).toBeFalse();
    expect(service.permissions()).toEqual([]);
    expect(service.has(PERMISSIONS.users.default)).toBeFalse();
  });

  it('reload 成功时替换权限集合', async () => {
    service.setPermissions({
      permissions: [PERMISSIONS.users.default],
      isSuperAdmin: false,
      versionToken: 'r1',
    });

    const reloaded = service.reload().toPromise();
    httpMock.expectOne('/api/v1/permissions/current').flush({
      permissions: [PERMISSIONS.roles.default],
      isSuperAdmin: false,
      versionToken: 'r2',
    });
    await reloaded;

    expect(service.has(PERMISSIONS.users.default)).toBeFalse();
    expect(service.has(PERMISSIONS.roles.default)).toBeTrue();
    expect(service.versionToken()).toBe('r2');
  });

  it('reload 失败时保持原有权限，不把用户降权成空集合', async () => {
    service.setPermissions({
      permissions: [PERMISSIONS.users.default],
      isSuperAdmin: false,
      versionToken: 'r1',
    });

    const reloaded = service
      .reload()
      .toPromise()
      .catch(() => undefined);
    httpMock
      .expectOne('/api/v1/permissions/current')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await reloaded;

    // 刷新失败就清空权限，会让界面上的入口无缘无故整片消失。
    expect(service.has(PERMISSIONS.users.default)).toBeTrue();
    expect(service.versionToken()).toBe('r1');
  });
});
