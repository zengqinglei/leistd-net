//#if (LocalIdentity)
import { HttpHandlerFn, HttpRequest } from '@angular/common/http';
import { Injector, provideZonelessChangeDetection, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { tenantInterceptor } from './tenant-interceptor';
import { TenantContextService } from '../services/tenant-context-service';

/**
 * 直接以 runInInjectionContext 驱动拦截器（与 http-error-interceptor.spec 同形态），
 * next 用 of 同步回发，断言最终发出的请求头。
 */
describe('tenantInterceptor', () => {
  const tenant = { id: '019ff8ed-221b-7673-9ba8-6b6dd5a638ab', name: 'acme', isActive: true };
  let injector: Injector;
  let context: TenantContextService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    injector = TestBed.inject(Injector);
    context = TestBed.inject(TenantContextService);
  });

  afterEach(() => localStorage.clear());

  function send(url: string): HttpRequest<unknown> {
    let sent!: HttpRequest<unknown>;
    const next: HttpHandlerFn = (req) => {
      sent = req;
      return of();
    };

    runInInjectionContext(injector, () =>
      tenantInterceptor(new HttpRequest('GET', url), next).subscribe(),
    );
    return sent;
  }

  it('已选租户时为 /api/ 请求附加 X-Tenant-Id', () => {
    context.set(tenant);

    expect(send('/api/v1/users').headers.get('X-Tenant-Id')).toBe(tenant.id);
  });

  it('未选租户（宿主）时不附加租户头', () => {
    expect(send('/api/v1/users').headers.has('X-Tenant-Id')).toBe(false);
  });

  it('非 /api/ 请求不附加租户头', () => {
    context.set(tenant);

    expect(send('/assets/config.json').headers.has('X-Tenant-Id')).toBe(false);
  });

  /**
   * 两个租户探测端点都不附租户头。
   *
   * 它们是宿主级匿名查询：附上失效租户头，"这个租户还能用吗"这一问本身会先被租户解析
   * 拒掉，而登录页在探测失败时会锁住租户区不许改，于是重试仍带同一个头、仍失败——
   * 失效的本地租户谁也清不掉。by-host 是后加的端点，漏掉它就是漏掉了这条恢复路径。
   */
  for (const url of ['/api/v1/tenants/by-name/acme', '/api/v1/tenants/by-host'] as const) {
    it(`租户探测端点不附加租户头：${url}`, () => {
      context.set(tenant);

      expect(send(url).headers.has('X-Tenant-Id')).toBe(false);
    });
  }
});
//#endif
