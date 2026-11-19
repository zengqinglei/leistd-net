import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
//#if (OpenIddictServer || RemoteTokenAuth)
import { userEvent } from 'vitest/browser';
//#endif

//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import { LayoutService } from '../../../core/services/layout-service';
//#if (OpenIddictServer || RemoteTokenAuth)
import { SystemDirectoryService } from '../../../core/services/system-directory-service';
//#endif
import { SystemSwitcher } from './system-switcher';

@Component({
  selector: 'app-test-system-switcher-host',
  imports: [SystemSwitcher, ...HlmSidebarImports],
  template: '<div hlmSidebarWrapper><app-system-switcher /></div>',
})
class Host {}

describe('SystemSwitcher', () => {
  const identity = {
    key: 'identity',
    displayName: 'Identity',
    homeUrl: 'https://identity.test/',
    isCurrent: true,
  };
  //#if (OpenIddictServer || RemoteTokenAuth)
  const orders = {
    key: 'orders',
    displayName: 'Orders',
    homeUrl: 'https://orders.test/',
    isCurrent: false,
  };
  //#endif
  const systems = signal([identity]);
  const switchTo = vi.fn();

  beforeEach(() => {
    systems.set([identity]);
    switchTo.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LayoutService, useValue: { homeRoute: () => '/w/acme/platform' } },
        //#if (OpenIddictServer || RemoteTokenAuth)
        { provide: SystemDirectoryService, useValue: { systems, switchTo } },
        //#endif
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
      ],
    });
  });

  it('uses a scoped home link when there is only one system', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    expect(
      fixture.nativeElement.querySelector('[data-testid="system-home"]').getAttribute('href'),
    ).toBe('/w/acme/platform');
    expect(fixture.nativeElement.querySelector('[data-testid="system-switcher"]')).toBeNull();
  });
  //#if (OpenIddictServer || RemoteTokenAuth)

  it('lists only systems, checks the current one, and delegates the shared switch behavior', async () => {
    systems.set([identity, orders]);
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const trigger = fixture.nativeElement.querySelector('[data-testid="system-switcher"]');
    await userEvent.click(trigger);
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    const entries = [...document.querySelectorAll<HTMLElement>('[data-system]')];
    expect(entries.map((entry) => entry.dataset['system'])).toEqual(['identity', 'orders']);
    expect(entries[0].querySelector('ng-icon')).not.toBeNull();
    expect(entries[1].querySelector('ng-icon')).toBeNull();
    await userEvent.click(entries[1]);
    expect(switchTo).toHaveBeenCalledWith(orders);
  });

  it('consumes Escape inside the menu before it can close a containing drawer', async () => {
    systems.set([identity, orders]);
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    await userEvent.click(fixture.nativeElement.querySelector('[data-testid="system-switcher"]'));
    const escape = vi.fn();
    document.addEventListener('keydown', escape);
    onTestFinished(() => document.removeEventListener('keydown', escape));
    document.querySelector<HTMLElement>('[data-system="orders"]')!.focus();
    await userEvent.keyboard('{Escape}');
    await fixture.whenStable();
    expect(document.querySelector('[role="menu"]')).toBeNull();
    expect(escape).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(
      fixture.nativeElement.querySelector('[data-testid="system-switcher"]'),
    );
  });

  it('falls back to the home link when the resource directory is empty', async () => {
    systems.set([]);
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[data-testid="system-home"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="system-switcher"]')).toBeNull();
  });
  //#endif
});
