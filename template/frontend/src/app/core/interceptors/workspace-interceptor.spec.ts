//#if (TenantWorkspace)
import { HttpContext, HttpErrorResponse, HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, throwError } from 'rxjs';

import { WORKSPACE_SCOPE } from './http-context-tokens';
import { workspaceInterceptor } from './workspace-interceptor';
import { ApplicationHttpError } from '../errors/application-http-error';
import { WorkspaceContextService } from '../services/workspace-context-service';

describe('workspaceInterceptor', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        {
          provide: WorkspaceContextService,
          useValue: {
            requestScope: () => 'acme',
            current: () => null,
            target: () => 'acme',
            recoverUnavailable: vi.fn().mockResolvedValue(undefined),
          },
        },
      ],
    }),
  );

  it('keeps cold-start denials for the guard and only recovers a committed workspace', async () => {
    const workspace = TestBed.inject(WorkspaceContextService);
    const error = ApplicationHttpError.from(
      new HttpErrorResponse({ status: 403, error: { code: 'Workspace:Unavailable' } }),
    );
    const send = () =>
      TestBed.runInInjectionContext(() =>
        firstValueFrom(
          workspaceInterceptor(new HttpRequest('GET', '/api/v1/permissions/current'), () =>
            throwError(() => error),
          ),
        ),
      );
    await expect(send()).rejects.toBe(error);
    expect(workspace.recoverUnavailable).not.toHaveBeenCalled();
    vi.spyOn(workspace, 'current').mockReturnValue('acme');
    await expect(send()).rejects.toBe(error);
    expect(workspace.recoverUnavailable).toHaveBeenCalledOnce();
  });

  it('selects the route workspace for business requests while keeping home endpoints unscoped', () => {
    const observed: HttpRequest<unknown>[] = [];
    const next = (request: HttpRequest<unknown>) => {
      observed.push(request);
      return of(new HttpResponse());
    };
    TestBed.runInInjectionContext(() => {
      workspaceInterceptor(new HttpRequest('GET', '/api/v1/users'), next).subscribe();
      workspaceInterceptor(new HttpRequest('GET', '/api/v1/auth/me'), next).subscribe();
      workspaceInterceptor(new HttpRequest('GET', '/i18n/en.json'), next).subscribe();
      workspaceInterceptor(
        new HttpRequest('GET', '/api/v1/users', {
          context: new HttpContext().set(WORKSPACE_SCOPE, null),
        }),
        next,
      ).subscribe();
    });
    expect(observed.map((request) => request.headers.get('X-Workspace'))).toEqual([
      'acme',
      null,
      null,
      null,
    ]);
  });
});
//#endif
