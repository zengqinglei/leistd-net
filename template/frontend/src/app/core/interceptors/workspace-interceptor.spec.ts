//#if (TenantWorkspace)
import { HttpContext, HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { WORKSPACE_SCOPE } from './http-context-tokens';
import { workspaceInterceptor } from './workspace-interceptor';
import { WorkspaceContextService } from '../services/workspace-context-service';

describe('workspaceInterceptor', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        {
          provide: WorkspaceContextService,
          useValue: { requestScope: () => 'acme', recoverUnavailable: vi.fn() },
        },
      ],
    }),
  );

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
