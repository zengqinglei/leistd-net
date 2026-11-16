//#if (TenantWorkspace)
import { CUSTOM_ELEMENTS_SCHEMA, Component, signal, type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RouterOutlet, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';

import { routes } from './app.routes';
import { AuthService } from './core/services/auth-service';
import { AuthorizationService } from './core/services/authorization-service';
import { LayoutService } from './core/services/layout-service';
import { StartupService } from './core/services/startup-service';
import { WorkspaceContextService } from './core/services/workspace-context-service';
import { DefaultLayout } from './layout/default/default-layout';
import { WorkspaceLayout } from './layout/workspace/workspace-layout';

@Component({ selector: 'app-empty-workspace', template: 'Empty workspace' })
class EmptyWorkspaceStub {}
@Component({ template: 'Protected content' })
class ProtectedPage {}

describe('workspace navigation boundaries', () => {
  it('waits for the home context before checking a console route permission', async () => {
    let release!: (value: boolean) => void;
    const establish = vi.fn(
      () =>
        new Promise<boolean>((resolve) => {
          release = resolve;
        }),
    );
    const hasAny = vi.fn(() => true);
    const wrapper = routes.find(
      (route) => route.path === 'platform' && route.children?.some((child) => child.path === ''),
    )!;
    expect(wrapper).toBeDefined();
    const platform = wrapper.children!.find((route) => route.path === '')!;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: '', component: ProtectedPage },
          {
            ...wrapper,
            children: [{ ...platform, component: ProtectedPage, loadChildren: undefined }],
          },
        ]),
        { provide: StartupService, useValue: { settled: () => of(true) } },
        {
          provide: AuthService,
          useValue: { isAuthenticated: () => true, currentUser: () => ({}) },
        },
        {
          provide: WorkspaceContextService,
          useValue: {
            refresh: () => Promise.resolve(),
            nameFromUrl: () => null,
            workspaces: () => [],
            auth: { currentUser: () => ({}) },
            establish,
          },
        },
        {
          provide: AuthorizationService,
          useValue: { hasAny, permissions: () => ['App.Business.Read'] },
        },
      ],
    });
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/platform', ProtectedPage);
    await expect.poll(() => establish.mock.calls.length).toBe(1);
    expect(hasAny).not.toHaveBeenCalled();
    release(true);
    await navigation;
    expect(hasAny).toHaveBeenCalledTimes(1);
  });

  for (const layout of [DefaultLayout, WorkspaceLayout] as Type<unknown>[]) {
    it(`keeps business content available without management permissions in ${layout.name}`, async () => {
      const permissions = signal(['App.Business.Read']);
      TestBed.overrideComponent(layout, {
        set: { imports: [RouterOutlet, EmptyWorkspaceStub], schemas: [CUSTOM_ELEMENTS_SCHEMA] },
      });
      await TestBed.configureTestingModule({
        imports: [layout],
        providers: [
          provideRouter([]),
          { provide: WorkspaceContextService, useValue: { current: () => 'acme' } },
          { provide: AuthorizationService, useValue: { permissions } },
          { provide: LayoutService, useValue: { trail: () => [] } },
        ],
      }).compileComponents();
      const fixture = TestBed.createComponent(layout);
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('app-empty-workspace')).toBeNull();
      expect(fixture.nativeElement.querySelector('router-outlet')).not.toBeNull();
      permissions.set([]);
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('app-empty-workspace')).not.toBeNull();
    });
  }
});
//#endif
