import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
//#if (OpenIddictServer || RemoteTokenAuth)
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
//#endif
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
//#if (OpenIddictServer || RemoteTokenAuth)

@Component({
  selector: 'app-test-system-switcher-drawer',
  imports: [SystemSwitcher, ...HlmSidebarImports, ...HlmSheetImports],
  template: `
    <hlm-sheet side="left" state="open">
      <hlm-sheet-content *hlmSheetPortal>
        <div hlmSidebarWrapper><app-system-switcher /></div>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
class DrawerHost {}
//#endif

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

  // 移动端侧栏是抽屉：菜单里的 Esc 只关菜单。CDK 菜单处理后标记为已处理，抽屉不再响应同一次按键
  it('closes only the menu on Escape inside a real drawer', async () => {
    systems.set([identity, orders]);
    const fixture = TestBed.createComponent(DrawerHost);
    await fixture.whenStable();
    const trigger = await vi.waitFor(() => {
      const element = document.querySelector<HTMLElement>('[data-testid="system-switcher"]');
      expect(element).not.toBeNull();
      return element!;
    });
    await userEvent.click(trigger);
    document.querySelector<HTMLElement>('[data-system="orders"]')!.focus();
    await userEvent.keyboard('{Escape}');
    await fixture.whenStable();
    expect(document.querySelector('[role="menu"]')).toBeNull();
    expect(document.querySelector('hlm-sheet-content')).not.toBeNull();
    expect(document.activeElement).toBe(trigger);
    fixture.destroy();
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
