//#if (LocalIdentity)
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TenantContextService } from './tenant-context-service';

describe('TenantContextService', () => {
  const tenant = {
    id: '019ff8ed-221b-7673-9ba8-6b6dd5a638ab',
    name: 'acme',
    displayName: 'Acme Inc.',
    isActive: true,
  };

  function create(): TenantContextService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    return TestBed.inject(TenantContextService);
  }

  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('初始无租户上下文（宿主）', () => {
    expect(create().current()).toBeNull();
  });

  it('set 后 signal 与 localStorage 双写', () => {
    const service = create();
    service.set(tenant);

    expect(service.current()).toEqual({
      id: tenant.id,
      name: tenant.name,
      displayName: tenant.displayName,
    });
    expect(localStorage.getItem('app.tenant')).toContain(tenant.id);
  });

  // 上下文里刻意**不存**"来自域名还是手填"：那是每次打开登录页现探的结论，不是租户的属性。
  // 存下来的话，换到一个域名不表态的地址后，界面仍会把租户锁成不可改。
  it('恢复上下文时忽略历史记录里多出来的字段', () => {
    localStorage.setItem(
      'app.tenant',
      JSON.stringify({
        id: tenant.id,
        name: tenant.name,
        displayName: tenant.displayName,
        source: 'domain',
      }),
    );

    expect(create().current()).toEqual({
      id: tenant.id,
      name: tenant.name,
      displayName: tenant.displayName,
    });
  });

  it('新实例从 localStorage 恢复上下文', () => {
    create().set(tenant);

    // 模拟刷新页面：新实例读回持久化的租户
    expect(create().current()?.id).toBe(tenant.id);
  });

  it('clear 同时清空 signal 与 localStorage', () => {
    const service = create();
    service.set(tenant);
    service.clear();

    expect(service.current()).toBeNull();
    expect(localStorage.getItem('app.tenant')).toBeNull();
  });

  it('损坏的存储数据被当作无上下文，不抛异常', () => {
    localStorage.setItem('app.tenant', '{"id":42}');

    expect(create().current()).toBeNull();
  });
});
//#endif
