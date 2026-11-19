//#if (TenantWorkspace)
import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';

import { ApplicationHttpError } from '../errors/application-http-error';
import { AuthorizationService } from '../services/authorization-service';
import { StartupService } from '../services/startup-service';
import { WorkspaceContextService } from '../services/workspace-context-service';
import { workspaceDeniedGuard, workspaceGuard } from './workspace-guard';

@Component({ selector: 'app-workspace-content-stub', template: 'Workspace content' })
class Content {}
@Component({ selector: 'app-workspace-denied-stub', template: 'Workspace denied' })
class Denied {}

describe('workspaceGuard', () => {
  const workspaces = [{ name: 'acme', tenantId: 'tenant-a', userId: 'member-a' }];
  const current = signal<string | null>(null);
  const establish = vi.fn();
  const recoverUnavailable = vi.fn().mockResolvedValue(undefined);

  beforeEach(() => {
    current.set(null);
    establish.mockReset();
    recoverUnavailable.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'w/:workspace/platform', component: Content, canActivate: [workspaceGuard] },
          { path: 'platform', component: Content },
          {
            path: 'workspace-denied',
            component: Denied,
            canActivate: [workspaceDeniedGuard],
            runGuardsAndResolvers: 'paramsOrQueryParamsChange',
          },
        ]),
        { provide: StartupService, useValue: { settled: () => of(true) } },
        { provide: AuthorizationService, useValue: { permissions: () => ['App.Users'] } },
        {
          provide: WorkspaceContextService,
          useValue: {
            refresh: vi.fn().mockResolvedValue(undefined),
            workspaces: () => workspaces,
            auth: { currentUser: () => ({ tenantId: 'tenant-a' }) },
            nameFromUrl: (url: string) => /^\/w\/([^/]+)/.exec(url)?.[1] ?? null,
            establish,
            current,
            recoverUnavailable,
          },
        },
      ],
    });
  });

  for (const [code, reason] of [
    ['Workspace:Forbidden', 'forbidden'],
    ['Workspace:Unavailable', 'unavailable'],
  ]) {
    it(`shows the terminal page for a rejected deep link with ${reason} and preserves its address`, async () => {
      establish.mockRejectedValue(
        ApplicationHttpError.from(new HttpErrorResponse({ status: 403, error: { code } })),
      );
      const harness = await RouterTestingHarness.create();
      const target = '/w/other/platform?from=mail#members';
      await harness.navigateByUrl(target, Denied);
      const router = TestBed.inject(Router);
      expect(router.parseUrl(router.url).queryParams['reason']).toBe(reason);
      expect(TestBed.inject(Location).path(true)).toBe(target);
      expect(establish).toHaveBeenCalledWith('other');
      expect(recoverUnavailable).not.toHaveBeenCalled();
      // 浏览器历史还原内部说明页时重查原目标，不能永远保留旧拒绝结论。
      establish.mockResolvedValue(true);
      const deniedUrl = router.url;
      await harness.navigateByUrl('/platform');
      await harness.navigateByUrl(deniedUrl, Content);
      expect(router.url).toBe(target);
    });

    it(`restores the committed context when an in-app switch is ${reason}`, async () => {
      const harness = await RouterTestingHarness.create('/platform');
      establish
        .mockRejectedValueOnce(
          ApplicationHttpError.from(new HttpErrorResponse({ status: 403, error: { code } })),
        )
        .mockResolvedValue(true);
      await harness.navigateByUrl('/w/other/platform');
      expect(TestBed.inject(Router).url).toBe('/platform');
      expect(establish.mock.calls.map(([name]) => name)).toEqual(['other', null]);
      expect(recoverUnavailable).toHaveBeenCalledTimes(reason === 'unavailable' ? 1 : 0);
    });
  }
});
//#endif
