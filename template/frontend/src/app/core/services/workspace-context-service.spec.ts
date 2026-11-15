//#if (TenantWorkspace)
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd, Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';

import { AuthService } from './auth-service';
import { AuthorizationService } from './authorization-service';
import { SessionContextService } from './session-context-service';
//#if (IncludeRealTime || IncludeNotifications)
import { SignalRService } from './signalr-service';
//#endif
import { WorkspaceContextService } from './workspace-context-service';

describe('WorkspaceContextService', () => {
  it('discards a late context load after a newer workspace selection', async () => {
    const completions: (() => void)[] = [];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: () => ({ tenantId: null }) } },
        { provide: AuthorizationService, useValue: { clear: vi.fn(), loaded: () => false } },
        {
          provide: SessionContextService,
          useValue: { establish: () => new Promise<void>((resolve) => completions.push(resolve)) },
        },
        //#if (IncludeRealTime || IncludeNotifications)
        {
          provide: SignalRService,
          useValue: { setWorkspace: () => new Promise<void>(() => undefined) },
        },
        //#endif
      ],
    });
    const context = TestBed.inject(WorkspaceContextService);
    const first = context.establish('acme');
    await Promise.resolve();
    const second = context.establish('globex');
    await Promise.resolve();
    completions[1]();
    expect(await second).toBe(true);
    completions[0]();
    expect(await first).toBe(false);
    expect(context.requestScope()).toBe('globex');
  });
  it('shares recovery across simultaneous unavailable responses and releases it when finished', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const service = TestBed.inject(WorkspaceContextService);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const first = service.recoverUnavailable();
    const second = service.recoverUnavailable();
    expect(second).toBe(first);
    TestBed.inject(HttpTestingController).expectOne('/api/v1/auth/me/workspaces').flush([]);
    await first;
    expect(navigate).toHaveBeenCalledTimes(1);
    TestBed.inject(HttpTestingController).verify();
  });

  it('remembers the console as a committed landing without changing the current scope in another tab', () => {
    const events = new Subject<NavigationEnd>();
    const previous = localStorage.getItem('last-workspace');
    onTestFinished(() =>
      previous === null
        ? localStorage.removeItem('last-workspace')
        : localStorage.setItem('last-workspace', previous),
    );
    TestBed.configureTestingModule({ providers: [{ provide: Router, useValue: { events } }] });
    const service = TestBed.inject(WorkspaceContextService);
    events.next(new NavigationEnd(1, '/w/acme/platform', '/w/acme/platform'));
    expect(localStorage.getItem('last-workspace')).toBe('acme');
    events.next(new NavigationEnd(2, '/platform', '/platform'));
    expect(service.current()).toBeNull();
    expect(localStorage.getItem('last-workspace')).toBe('');
  });
});
//#endif
