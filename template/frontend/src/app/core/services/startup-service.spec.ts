//#if (IncludeIdentity)
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';

import { AuthService } from './auth-service';
import { StartupService } from './startup-service';
import { ApplicationHttpError } from '../errors/application-http-error';

describe('StartupService', () => {
  let authService: jasmine.SpyObj<AuthService>;
  let service: StartupService;

  beforeEach(() => {
    authService = jasmine.createSpyObj<AuthService>('AuthService', [
      'initializeAuth',
      'clearAuthData',
    ]);
    TestBed.configureTestingModule({
      providers: [StartupService, { provide: AuthService, useValue: authService }],
    });
    service = TestBed.inject(StartupService);
    // 固定为受保护路由，覆盖认证探测的全部状态转换分支。
    spyOn(
      service as unknown as { isProtectedRoute(pathname: string, hash: string): boolean },
      'isProtectedRoute',
    ).and.returnValue(true);
  });

  function httpError(status: number): ApplicationHttpError {
    return ApplicationHttpError.from(
      new HttpErrorResponse({ status, statusText: `HTTP ${status}` }),
    );
  }

  it('treats 401 as signed-out and still reaches success', async () => {
    authService.initializeAuth.and.rejectWith(httpError(401));

    await service.load();

    expect(authService.clearAuthData).toHaveBeenCalled();
    expect(service.status()).toBe('success');
  });

  it('marks startup as failed when the auth service is unavailable (503)', async () => {
    const error = httpError(503);
    authService.initializeAuth.and.rejectWith(error);

    await service.load();

    expect(service.status()).toBe('failed');
    expect(service.error()).toBe(error);
    expect(authService.clearAuthData).not.toHaveBeenCalled();
  });

  it('marks startup as failed on network errors (status 0)', async () => {
    authService.initializeAuth.and.rejectWith(httpError(0));

    await service.load();

    expect(service.status()).toBe('failed');
  });

  it('reaches success when the session probe resolves', async () => {
    authService.initializeAuth.and.resolveTo();

    await service.load();

    expect(service.status()).toBe('success');
    expect(service.error()).toBeNull();
  });
});
//#endif
