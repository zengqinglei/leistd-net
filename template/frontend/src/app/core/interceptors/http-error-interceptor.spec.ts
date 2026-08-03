import {
  HttpContext,
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpRequest,
} from '@angular/common/http';
import { Injector, provideZonelessChangeDetection, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
//#if (IncludeIdentity)
import { provideRouter, Router } from '@angular/router';
//#endif
import { Observable, throwError } from 'rxjs';

//#if (IncludeIdentity)
import { SILENT_AUTH } from './http-context-tokens';
//#endif
import { httpErrorInterceptor } from './http-error-interceptor';
import { ApplicationHttpError } from '../errors/application-http-error';
//#if (IncludeIdentity)
import { AuthService } from '../services/auth-service';
//#endif

/**
 * 直接以 runInInjectionContext 驱动拦截器：next 用 throwError 同步发射错误，
 * catchError 同步映射，因此错误在订阅时同步落到 error 回调（zoneless 无需 fakeAsync）。
 */
describe('httpErrorInterceptor', () => {
  let injector: Injector;

  beforeEach(() => {
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        provideZonelessChangeDetection(),
        //#if (IncludeIdentity)
        provideRouter([]),
        //#endif
      ],
    });
    injector = TestBed.inject(Injector);
  });

  function runInterceptor(error: unknown, options?: { context?: HttpContext }): unknown {
    const req = new HttpRequest('GET', '/api/test', { context: options?.context });
    const next: HttpHandlerFn = () => throwError(() => error) as Observable<HttpEvent<unknown>>;

    let caught: unknown;
    runInInjectionContext(injector, () => httpErrorInterceptor(req, next)).subscribe({
      error: (value: unknown) => (caught = value),
    });
    return caught;
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({
      status,
      statusText: `status ${status}`,
      url: '/api/test',
      error,
    });
  }

  for (const status of [403, 404, 409, 422, 500]) {
    it(`normalizes a ${status} response to an ApplicationHttpError`, () => {
      const caught = runInterceptor(httpError(status));
      expect(caught).toBeInstanceOf(ApplicationHttpError);
      expect((caught as ApplicationHttpError).status).toBe(status);
    });
  }

  it('normalizes a network failure (status 0) to an ApplicationHttpError', () => {
    const caught = runInterceptor(httpError(0, new ProgressEvent('error')));
    expect(caught).toBeInstanceOf(ApplicationHttpError);
    expect((caught as ApplicationHttpError).status).toBe(0);
  });

  it('parses an RFC 9457 errors array (code + details)', () => {
    const caught = runInterceptor(
      httpError(422, { errors: [{ code: 'name_required', detail: 'Name is required.' }] }),
    );
    const applicationError = caught as ApplicationHttpError;
    expect(applicationError).toBeInstanceOf(ApplicationHttpError);
    expect(applicationError.code).toBe('name_required');
    expect(applicationError.message).toBe('Name is required.');
    expect(applicationError.details.length).toBe(1);
  });

  it('lets a non-HTTP error pass through unchanged toward the GlobalErrorHandler', () => {
    const original = new Error('boom');
    const caught = runInterceptor(original);
    expect(caught).toBe(original);
    expect(caught instanceof ApplicationHttpError).toBe(false);
  });

  //#if (IncludeIdentity)
  it('clears auth data and redirects to /auth/login on 401', () => {
    const authService = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);
    const clearSpy = spyOn(authService, 'clearAuthData');
    const navigateSpy = spyOn(router, 'navigate');

    const caught = runInterceptor(httpError(401));

    expect(clearSpy).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/auth/login'], jasmine.anything());
    expect(caught).toBeInstanceOf(ApplicationHttpError);
    expect((caught as ApplicationHttpError).status).toBe(401);
  });

  it('honors SILENT_AUTH: skips the 401 redirect but still normalizes the error', () => {
    const authService = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);
    const clearSpy = spyOn(authService, 'clearAuthData');
    const navigateSpy = spyOn(router, 'navigate');
    const context = new HttpContext().set(SILENT_AUTH, true);

    const caught = runInterceptor(httpError(401), { context });

    expect(clearSpy).not.toHaveBeenCalled();
    expect(navigateSpy).not.toHaveBeenCalled();
    expect(caught).toBeInstanceOf(ApplicationHttpError);
  });
  //#endif
});
