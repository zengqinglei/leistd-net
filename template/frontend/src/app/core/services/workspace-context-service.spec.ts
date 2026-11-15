//#if (TenantWorkspace)
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

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
        { provide: SignalRService, useValue: { setWorkspace: () => Promise.resolve() } },
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
});
//#endif
