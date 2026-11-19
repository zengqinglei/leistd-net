//#if (TenantWorkspace)
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';

//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../i18n/transloco.testing';
//#endif
import { WorkspaceContextService } from '../../services/workspace-context-service';
import { WorkspaceDenied } from './workspace-denied';

describe('WorkspaceDenied', () => {
  for (const reason of ['forbidden', 'unavailable']) {
    it(`explains ${reason} with an empty state and offers the login landing`, async () => {
      const refresh = vi.fn().mockResolvedValue(undefined);
      await TestBed.configureTestingModule({
        imports: [WorkspaceDenied],
        providers: [
          provideRouter([]),
          {
            provide: ActivatedRoute,
            useValue: { queryParamMap: of(convertToParamMap({ reason })) },
          },
          {
            provide: WorkspaceContextService,
            useValue: { refresh, landingUrl: () => '/w/acme/platform' },
          },
          //#if (IncludeLocalization)
          ...provideTranslocoTesting(['en']),
          //#endif
        ],
      }).compileComponents();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      const fixture = TestBed.createComponent(WorkspaceDenied);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(fixture.componentInstance.description()).toBe(`tenantWorkspace.${reason}Description`);
      const host = fixture.nativeElement as HTMLElement;
      expect(host.querySelector('[hlmEmptyTitle]')?.textContent).toMatch(
        /deniedTitle|You cannot access this workspace/,
      );
      expect(host.querySelectorAll('button')).toHaveLength(1);
      host.querySelector('button')!.click();
      await fixture.whenStable();
      expect(refresh).toHaveBeenCalledOnce();
      expect(navigate).toHaveBeenCalledWith('/w/acme/platform');
    });
  }
});
//#endif
