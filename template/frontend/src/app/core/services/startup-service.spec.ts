//#if (IncludeIdentity)
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';

import { AuthService } from './auth-service';
//#if (IncludeRoles)
import { AuthorizationService } from './authorization-service';
//#endif
import { StartupService } from './startup-service';
import { ApplicationHttpError } from '../errors/application-http-error';

describe('StartupService', () => {
  let authService: jasmine.SpyObj<AuthService>;
  //#if (IncludeRoles)
  // 启动流在认证之后还要拉一次权限；这里桩掉协作者，本组用例只关心状态机的分支。
  let authorizationService: jasmine.SpyObj<AuthorizationService>;
  //#endif
  let service: StartupService;

  beforeEach(() => {
    authService = jasmine.createSpyObj<AuthService>('AuthService', [
      'initializeAuth',
      'clearAuthData',
    ]);
    //#if (IncludeRoles)
    authorizationService = jasmine.createSpyObj<AuthorizationService>('AuthorizationService', [
      'initialize',
      'clear',
    ]);
    authorizationService.initialize.and.resolveTo();
    //#endif
    TestBed.configureTestingModule({
      providers: [
        StartupService,
        { provide: AuthService, useValue: authService },
        //#if (IncludeRoles)
        { provide: AuthorizationService, useValue: authorizationService },
        //#endif
      ],
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
    //#if (IncludeRoles)
    // 401 视为未登录：本地权限缓存必须一并清掉，否则上一位用户的裁剪结论会留下来。
    expect(authorizationService.clear).toHaveBeenCalled();
    //#endif
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

    //#if (IncludeRoles)
    // 权限与当前用户在同一次启动中就位，Guard 与菜单才不会闪现受保护入口。
    expect(authorizationService.initialize).toHaveBeenCalled();
    //#endif
    expect(service.status()).toBe('success');
    expect(service.error()).toBeNull();
  });
});
//#endif
