//#if (LocalIdentity)
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TenantContextService } from './tenant-context-service';

describe('TenantContextService', () => {
  // 只存租户键，匿名接口不返回可泄露租户存在性的标识与展示名。
  const tenantName = 'acme';

  function create(): TenantContextService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    return TestBed.inject(TenantContextService);
  }

  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('starts with no tenant context (host)', () => {
    expect(create().current()).toBeNull();
  });

  // 选定只是这一次的尝试，凭据被接受后才记住，选错且登录失败不改变下次的预选
  it('changes only the signal on set, and persists on remember', () => {
    const service = create();
    service.set(tenantName);

    expect(service.current()).toEqual({ key: tenantName });
    expect(localStorage.getItem('app.tenant')).toBeNull();

    service.remember();
    expect(localStorage.getItem('app.tenant')).toContain(tenantName);
  });

  it('forgets the remembered tenant when remembering the host', () => {
    const service = create();
    service.set(tenantName);
    service.remember();
    service.clear();

    expect(localStorage.getItem('app.tenant')).toContain(tenantName);

    service.remember();
    expect(localStorage.getItem('app.tenant')).toBeNull();
  });

  // 上下文里刻意**不存**"来自域名还是手填"：那是每次打开登录页现探的结论，不是租户的属性。
  // 存下来的话，换到一个域名不表态的地址后，界面仍会把租户锁成不可改。
  it('ignores extra fields in the stored record when restoring the context', () => {
    localStorage.setItem('app.tenant', JSON.stringify({ key: tenantName, source: 'domain' }));

    expect(create().current()).toEqual({ key: tenantName });
  });

  // 缺少 key 的存档无效，避免向租户提示头写入 undefined。
  it('treats a stored record in the legacy shape as invalid', () => {
    localStorage.setItem(
      'app.tenant',
      JSON.stringify({ id: '019ff8ed-221b-7673-9ba8-6b6dd5a638ab', name: tenantName }),
    );

    expect(create().current()).toBeNull();
  });

  it('restores the remembered context from localStorage in a new instance', () => {
    const service = create();
    service.set(tenantName);
    service.remember();

    expect(create().current()?.key).toBe(tenantName);
  });

  it('clears both the signal and localStorage on forget', () => {
    const service = create();
    service.set(tenantName);
    service.remember();
    service.forget();

    expect(service.current()).toBeNull();
    expect(localStorage.getItem('app.tenant')).toBeNull();
  });

  it('treats corrupted stored data as no context without throwing', () => {
    localStorage.setItem('app.tenant', '{"id":42}');

    expect(create().current()).toBeNull();
  });
});
//#endif
