import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthorizationService } from './authorization-service';
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

  // 避免闪现受保护入口
  it('treats every permission as denied until loaded', () => {
    expect(service.loaded()).toBe(false);
    expect(service.has(PERMISSIONS.users.default)).toBe(false);
    expect(service.canAccessPlatform()).toBe(false);
  });

  it('applies has / hasAny / hasAll consistently after setPermissions', () => {
    service.setPermissions({
      permissions: [PERMISSIONS.users.default, PERMISSIONS.users.create],
      isSuperAdmin: false,
      versionToken: 'r1',
    });

    expect(service.loaded()).toBe(true);
    expect(service.versionToken()).toBe('r1');

    expect(service.has(PERMISSIONS.users.default)).toBe(true);
    expect(service.has(PERMISSIONS.roles.default)).toBe(false);

    expect(service.hasAny(PERMISSIONS.roles.default, PERMISSIONS.users.create)).toBe(true);
    expect(service.hasAny(PERMISSIONS.roles.default)).toBe(false);

    expect(service.hasAll(PERMISSIONS.users.default, PERMISSIONS.users.create)).toBe(true);
    expect(service.hasAll(PERMISSIONS.users.default, PERMISSIONS.roles.default)).toBe(false);
  });

  it('does not treat the super admin flag as a second grant rule', () => {
    service.setPermissions({ permissions: [], isSuperAdmin: true, versionToken: 'r1' });

    expect(service.isSuperAdmin()).toBe(true);

    // 标记是展示信息；能不能看由权限集合决定，与后端同一判据。
    expect(service.has(PERMISSIONS.users.default)).toBe(false);
    expect(service.canAccessPlatform()).toBe(false);
  });

  it('grants platform access with any single platform entry permission', () => {
    service.setPermissions({
      permissions: [PERMISSIONS.roles.default],
      isSuperAdmin: false,
      versionToken: 'r1',
    });

    expect(service.canAccessPlatform()).toBe(true);
  });

  /**
   * 平台入口权限集里的**每一项**都要能单独放行。
   *
   * 路由与菜单同源由 app.routes.spec.ts 锁住，但同源的清单若漏了某个模块，那个模块的专属角色照样进不去。
   * 这一条逐项验证，新增模块时忘记加入集合就会红。
   */
  it('grants platform access for each platform entry permission on its own', () => {
    for (const permission of PLATFORM_ENTRY_PERMISSIONS) {
      service.setPermissions({
        permissions: [permission],
        isSuperAdmin: false,
        versionToken: 'r1',
      });

      expect(service.canAccessPlatform(), `仅持有 ${permission} 时应可进入平台区`).toBe(true);
    }
  });

  it('returns to the unloaded state after clear', () => {
    service.setPermissions({
      permissions: [PERMISSIONS.users.default],
      isSuperAdmin: true,
      versionToken: 'r1',
    });

    service.clear();

    expect(service.loaded()).toBe(false);
    expect(service.isSuperAdmin()).toBe(false);
    expect(service.permissions()).toEqual([]);
    expect(service.has(PERMISSIONS.users.default)).toBe(false);
  });

  it('replaces the permission set when reload succeeds', async () => {
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

    expect(service.has(PERMISSIONS.users.default)).toBe(false);
    expect(service.has(PERMISSIONS.roles.default)).toBe(true);
    expect(service.versionToken()).toBe('r2');
  });

  it('keeps the existing permissions instead of emptying them when reload fails', async () => {
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
    expect(service.has(PERMISSIONS.users.default)).toBe(true);
    expect(service.versionToken()).toBe('r1');
  });
});
