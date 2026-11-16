//#if (TenantWorkspace)
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { of } from 'rxjs';
//#endif

import { EmptyWorkspace } from './empty-workspace';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../core/i18n/transloco.testing';
//#endif
import { WorkspaceContextService } from '../../core/services/workspace-context-service';

describe('EmptyWorkspace', () => {
  it('offers workspace switching and the home console while preserving the surrounding layout', async () => {
    const switchTo = vi.fn();
    await TestBed.configureTestingModule({
      imports: [EmptyWorkspace],
      providers: [
        { provide: WorkspaceContextService, useValue: { current: signal('globex'), switchTo } },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en'], {
          getTranslation: () =>
            of({
              tenantWorkspace: {
                switch: 'Switch workspace',
                backToConsole: 'Back to platform console',
              },
            }),
        }),
        //#endif
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(EmptyWorkspace);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const buttons = host.querySelectorAll('button');
    expect(buttons).toHaveLength(2);
    expect(buttons[1].textContent).toMatch(/Back to platform console|backToConsole/);
    expect(buttons[1].getBoundingClientRect().left).toBeGreaterThan(
      buttons[0].getBoundingClientRect().left,
    );
    expect(
      Math.abs(buttons[1].getBoundingClientRect().top - buttons[0].getBoundingClientRect().top),
    ).toBeLessThan(2);
    expect(host.querySelector('[hlmEmptyTitle]')).not.toBeNull();
    buttons[1].click();
    expect(switchTo).toHaveBeenCalledWith(null);
  });
});
//#endif
