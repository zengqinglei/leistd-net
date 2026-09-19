//#if (LocalIdentity)
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ImpersonationService } from './impersonation-service';

describe('ImpersonationService', () => {
  const key = 'impersonation.exitedNotice';

  function create(): ImpersonationService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    return TestBed.inject(ImpersonationService);
  }

  afterEach(() => sessionStorage.removeItem(key));

  it('退出提示标记只消费一次', () => {
    const service = create();
    sessionStorage.setItem(key, '1');

    expect(service.consumeExitedNotice()).toBeTrue();
    expect(service.consumeExitedNotice()).toBeFalse();
    expect(sessionStorage.getItem(key)).toBeNull();
  });

  it('没有标记时不提示', () => {
    expect(create().consumeExitedNotice()).toBeFalse();
  });

  it('存储不可用时不抛出，只是不提示', () => {
    const service = create();
    spyOn(Storage.prototype, 'getItem').and.throwError('SecurityError');

    expect(service.consumeExitedNotice()).toBeFalse();
  });
});
//#endif
