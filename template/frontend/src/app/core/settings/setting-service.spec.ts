import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SettingService } from './setting-service';
import { SettingOutputDto } from './setting.dto';

/** 路由契约：用户偏好与系统默认值走两个端点，打错端点会被 403 或越权改全局默认值。 */
describe('SettingService', () => {
  let service: SettingService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [SettingService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SettingService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads from the settings endpoint', () => {
    service.getSettings().subscribe();

    const req = http.expectOne({ method: 'GET', url: '/api/v1/settings' });
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('writes a personal preference to current-user', () => {
    service.setForCurrentUser({ name: 'Display.TimeZone', value: 'UTC' }).subscribe();

    const req = http.expectOne({ method: 'PUT', url: '/api/v1/settings/current-user' });
    expect(req.request.body).toEqual({ name: 'Display.TimeZone', value: 'UTC' });
    req.flush(null);
  });

  it('writes a system default to current-tenant', () => {
    service.setForCurrentTenant({ name: 'Display.TimeZone', value: 'UTC' }).subscribe();

    const req = http.expectOne({ method: 'PUT', url: '/api/v1/settings/current-tenant' });
    expect(req.request.body).toEqual({ name: 'Display.TimeZone', value: 'UTC' });
    req.flush(null);
  });

  // null 是唯一的清除语义：空串会作为真实值落库，既挡住回落又被消费方当成未设置。
  it('sends null to clear an override', () => {
    service.setForCurrentUser({ name: 'Display.TimeZone', value: null }).subscribe();

    const req = http.expectOne({ method: 'PUT', url: '/api/v1/settings/current-user' });
    expect(req.request.body).toEqual({ name: 'Display.TimeZone', value: null });
    req.flush(null);
  });

  // 设置分区不选端点：按作用域选，进程级设置在系统作用域同样写当前租户（宿主上下文下即宿主那一行）
  it.each([
    { scope: 'account' as const, allowsTenantScope: true, url: '/api/v1/settings/current-user' },
    { scope: 'system' as const, allowsTenantScope: true, url: '/api/v1/settings/current-tenant' },
    { scope: 'system' as const, allowsTenantScope: false, url: '/api/v1/settings/current-tenant' },
  ])(
    'saves a $scope row (tenant scope $allowsTenantScope) to $url',
    ({ scope, allowsTenantScope, url }) => {
      const setting = { name: 'Logging.MinimumLevel', allowsTenantScope } as SettingOutputDto;
      service.saveSetting(setting, scope, 'Debug').subscribe();

      const req = http.expectOne({ method: 'PUT', url });
      expect(req.request.body).toEqual({ name: 'Logging.MinimumLevel', value: 'Debug' });
      req.flush(null);
    },
  );
});
