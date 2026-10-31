import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { ReturnUrlService } from './return-url-service';

/** 认证流程之后回到原目标的共同判定：只接站内地址，设置页自身不算目标，授权端点整页回到服务端。 */
describe('ReturnUrlService', () => {
  let service: ReturnUrlService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    service = TestBed.inject(ReturnUrlService);
    router = TestBed.inject(Router);
  });

  it('keeps an in-site target with its query string and fragment', () => {
    expect(service.resolve('/platform/users?offset=20#list')).toBe(
      '/platform/users?offset=20#list',
    );
  });

  for (const unusable of [
    null,
    undefined,
    '',
    'platform/users',
    '//evil.example.com',
    'https://evil.example.com',
    'javascript://evil',
    '/\\evil.example.com',
    // 指向设置页自身：设置完成后又回到设置页，原地打转
    '/auth/two-factor-setup',
    '/auth/two-factor-setup?returnUrl=%2Fworkspace',
  ]) {
    it(`rejects the unusable target ${JSON.stringify(unusable)}`, () => {
      expect(service.resolve(unusable)).toBeNull();
    });
  }

  it('builds the setup page address with the target, or without one when it is unusable', () => {
    expect(router.serializeUrl(service.twoFactorSetup('/platform/users?offset=20'))).toBe(
      '/auth/two-factor-setup?returnUrl=%2Fplatform%2Fusers%3Foffset%3D20',
    );
    expect(router.serializeUrl(service.twoFactorSetup('https://evil.example.com'))).toBe(
      '/auth/two-factor-setup',
    );
  });

  it('navigates inside the application to an in-site target', async () => {
    const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fallback = vi.fn(async () => true);

    expect(await service.navigate('/workspace/settings/security', fallback)).toBe('routed');
    expect(navigateByUrl).toHaveBeenCalledExactlyOnceWith('/workspace/settings/security');
    expect(fallback).not.toHaveBeenCalled();
  });

  it('falls back when there is no usable target', async () => {
    const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fallback = vi.fn(async () => true);

    expect(await service.navigate('https://evil.example.com', fallback)).toBe('fallback');
    expect(fallback).toHaveBeenCalledOnce();
    expect(navigateByUrl).not.toHaveBeenCalled();
  });
  it.each([null, '/workspace'])('preserves cancellation for target %s', async (target) => {
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(false);
    expect(await service.navigate(target, async () => false)).toBe('cancelled');
  });
  //#if (OpenIddictServer)

  // 授权端点不是前端路由：整页回到服务端，原样带回缓存请求的引用与重新认证证明
  it('loads an authorization endpoint target as a full page, keeping its parameters', async () => {
    const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const loadPage = vi.spyOn(service, 'loadPage').mockImplementation(() => undefined);
    const target =
      '/connect/authorize?client_id=resource&request_uri=urn%3Aabc&reauthentication=proof';

    // 调用方据此保持加载态：页面已经在离开，不该先复位再消失
    expect(await service.navigate(target, vi.fn())).toBe('left');
    expect(loadPage).toHaveBeenCalledExactlyOnceWith(target);
    expect(navigateByUrl).not.toHaveBeenCalled();
  });

  // 与站内目标同一套校验：只认站内的授权端点，外站与协议相对地址不算
  it('tells a server page target apart from in-site and off-site targets', () => {
    expect(service.isServerPage('/connect/authorize?client_id=resource')).toBe(true);
    expect(service.isServerPage('/workspace')).toBe(false);
    expect(service.isServerPage('//evil.example.com/connect/authorize')).toBe(false);
    expect(service.isServerPage('https://evil.example.com/connect/authorize')).toBe(false);
    expect(service.isServerPage(null)).toBe(false);
  });

  // 没能发起整页导航就不算离开：异常交给调用方，由它恢复界面
  it('does not report leaving when the full page load cannot start', async () => {
    vi.spyOn(service, 'loadPage').mockImplementation(() => {
      throw new Error('navigation blocked');
    });

    await expect(
      service.navigate('/connect/authorize?client_id=resource', vi.fn()),
    ).rejects.toThrow('navigation blocked');
  });
  //#endif
});
