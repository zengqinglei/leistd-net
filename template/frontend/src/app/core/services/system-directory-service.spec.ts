//#if (OpenIddictServer || RemoteTokenAuth)
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { FullPageNavigationService } from '../routing/full-page-navigation-service';
import { AuthService } from './auth-service';
//#if (IncludeLocalization)
import { LanguageService } from './language-service';
//#endif
import { LayoutService } from './layout-service';
import { SystemDirectoryService } from './system-directory-service';
//#if (TenantWorkspace)
import { WorkspaceContextService } from './workspace-context-service';
//#endif

const identity = {
  key: 'identity',
  displayName: 'Identity',
  homeUrl: 'https://identity.test/',
  isCurrent: true,
};
const other = {
  key: 'orders',
  displayName: 'Orders',
  homeUrl: 'https://orders.test/base/',
  isCurrent: false,
};

describe('SystemDirectoryService', () => {
  const user = signal<{ id: string; tenantId?: string } | null>({ id: 'staff' });
  const lang = signal('en');
  const scope = signal<string | null>('tenant-a');
  const assign = vi.fn();
  let service: SystemDirectoryService;
  let http: HttpTestingController;

  beforeEach(() => {
    user.set({ id: 'staff' });
    lang.set('en');
    scope.set('tenant-a');
    assign.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: user } },
        { provide: LayoutService, useValue: { homeRoute: () => '/w/tenant-a/platform' } },
        { provide: FullPageNavigationService, useValue: { assign } },
        //#if (IncludeLocalization)
        { provide: LanguageService, useValue: { activeLang: lang } },
        //#endif
        //#if (TenantWorkspace)
        { provide: WorkspaceContextService, useValue: { current: scope } },
        //#endif
      ],
    });
    service = TestBed.inject(SystemDirectoryService);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
    http.expectOne('/api/v1/systems').flush([identity, other]);
  });
  afterEach(() => http.verify());

  it('loads the directory and removes it when the home session ends', () => {
    expect(service.systems()).toEqual([identity, other]);
    user.set(null);
    TestBed.tick();
    expect(service.systems()).toEqual([]);
    http.expectNone('/api/v1/systems');
  });
  //#if (IncludeLocalization)

  it('reloads localized display names after the language changes', () => {
    lang.set('zh-CN');
    TestBed.tick();
    http.expectOne('/api/v1/systems').flush([{ ...other, displayName: '订单系统' }]);
    expect(service.systems()[0].displayName).toBe('订单系统');
  });
  //#endif

  it('uses the target home URL for a tenant user', () => {
    user.set({ id: 'member', tenantId: 'tenant-id' });
    TestBed.tick();
    http.expectOne('/api/v1/systems').flush([identity, other]);
    service.switchTo(other);
    expect(assign).toHaveBeenCalledWith(other.homeUrl);
  });
  //#if (TenantWorkspace)

  it('preserves a platform staff workspace when changing systems', () => {
    service.switchTo(other);
    expect(assign).toHaveBeenCalledWith('https://orders.test/base/w/tenant-a/platform');
    scope.set(null);
    service.switchTo(other);
    expect(assign).toHaveBeenLastCalledWith(other.homeUrl);
  });
  //#endif

  it('returns to the current scoped home without leaving the app', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    service.switchTo(identity);
    expect(navigate).toHaveBeenCalledWith('/w/tenant-a/platform');
    expect(assign).not.toHaveBeenCalled();
  });
});
//#endif
