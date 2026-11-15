//#if (TenantWorkspace && LocalIdentity)
import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
// prettier-ignore
import {
  BehaviorSubject,
  //#if (IncludeLocalization)
  of,
  //#endif
} from 'rxjs';

import { TenantStaffSheet } from './tenant-staff-sheet';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif

describe('TenantStaffSheet', () => {
  it('uses the staff query as its only open state and sends selected roles without credentials', async () => {
    const query = new BehaviorSubject(convertToParamMap({ staff: 'tenant-1' }));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: { queryParamMap: query, snapshot: { queryParamMap: query.value } },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
      ],
    });
    const sheet = TestBed.runInInjectionContext(() => new TenantStaffSheet());
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/tenants/tenant-1').flush({ id: 'tenant-1', name: 'acme' });
    http.expectOne('/api/v1/tenants/tenant-1/staff').flush([]);
    http
      .expectOne('/api/v1/tenants/tenant-1/staff/roles')
      .flush([{ id: 'role-1', name: 'Admin', displayName: 'Admin' }]);
    http
      .expectOne('/api/v1/tenants/tenant-1/staff/candidates')
      .flush([{ id: 'home-1', username: 'alice' }]);
    await Promise.resolve();
    await Promise.resolve();
    expect(sheet.tenantId()).toBe('tenant-1');
    sheet.openGrant();
    sheet.selectedUserId.set('home-1');
    await sheet.save();
    http.expectNone((request) => request.method === 'POST');
    sheet.toggleRole('role-1');
    const saving = sheet.save();
    const grant = http.expectOne('/api/v1/tenants/tenant-1/staff');
    expect(grant.request.body).toEqual({ homeUserId: 'home-1', roleIds: ['role-1'] });
    grant.flush(null);
    await Promise.resolve();
    await Promise.resolve();
    http
      .match((request) => request.method === 'GET')
      .forEach((request) =>
        request.flush(
          request.request.url.endsWith('/tenant-1') ? { id: 'tenant-1', name: 'acme' } : [],
        ),
      );
    await saving;
    expect(sheet.dialogOpen()).toBe(false);
    sheet.openGrant({
      homeUserId: 'home-1',
      memberId: 'member-1',
      username: 'alice',
      isActive: true,
      creationTime: '',
      roles: [{ id: 'role-1', name: 'Admin', displayName: 'Admin' }],
    });
    expect(sheet.editing()?.homeUserId).toBe('home-1');
    expect(sheet.selectedRoles()).toEqual(['role-1']);
    query.next(convertToParamMap({}));
    TestBed.tick();
    expect(sheet.tenantId()).toBeNull();
    http.verify();
  });
  it('only offers row expansion for hidden columns and keeps role rows compact', async () => {
    const query = new BehaviorSubject(convertToParamMap({ staff: 'tenant-2' }));
    const viewport = new BehaviorSubject<BreakpointState>({
      matches: true,
      breakpoints: {
        '(min-width: 768px)': true,
        '(min-width: 1024px)': true,
      },
    });
    await TestBed.configureTestingModule({
      imports: [TenantStaffSheet],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BreakpointObserver, useValue: { observe: () => viewport } },
        {
          provide: ActivatedRoute,
          useValue: { queryParamMap: query, snapshot: { queryParamMap: query.value } },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en'], {
          getTranslation: () =>
            of({
              tenants: {
                staff: {
                  person: 'Person',
                  roles: 'Roles',
                  status: 'Status',
                  creationTime: 'Granted at',
                  actions: 'Actions',
                  active: 'Enabled',
                  edit: 'Edit roles',
                  grantDescription:
                    'Each person granted access gets a linked account in {{name}} and acts with the selected roles.',
                },
              },
            }),
        }),
        //#endif
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(TenantStaffSheet);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const roles = [
      { id: 'admin', name: 'Admin', displayName: 'Administrator', isStatic: true },
      { id: 'member', name: 'Member', displayName: 'Member', isStatic: false },
    ];
    http.expectOne('/api/v1/tenants/tenant-2').flush({ id: 'tenant-2', name: 'acme' });
    http.expectOne('/api/v1/tenants/tenant-2/staff').flush([
      {
        homeUserId: 'home-1',
        memberId: 'member-1',
        username: 'alice',
        isActive: true,
        creationTime: '2026-11-16T00:00:00Z',
        roles: [roles[0]],
      },
    ]);
    http.expectOne('/api/v1/tenants/tenant-2/staff/roles').flush(roles);
    http
      .expectOne('/api/v1/tenants/tenant-2/staff/candidates')
      .flush([{ id: 'home-1', username: 'alice' }]);
    await expect.poll(() => fixture.componentInstance.tenant()?.name).toBe('acme');
    await fixture.whenStable();
    fixture.detectChanges();
    const content = () => document.querySelector('hlm-sheet-content')!;
    expect(fixture.componentInstance.hasCollapsedColumns()).toBe(false);
    expect(content().querySelector('tbody button[aria-label="alice"]')).toBeNull();
    expect(content().querySelector('thead th:last-child .sr-only')?.textContent).toContain(
      'Actions',
    );
    fixture.componentInstance.openGrant();
    fixture.detectChanges();
    await fixture.whenStable();
    const dialog = document.querySelector('hlm-dialog-content')!;
    expect(dialog.textContent).toContain('linked account in acme');
    const labels = dialog.querySelectorAll<HTMLLabelElement>('label[for^="staff-role-"]');
    expect(labels).toHaveLength(2);
    const first = labels[0].parentElement!.getBoundingClientRect();
    const next = labels[1].parentElement!.getBoundingClientRect();
    expect(next.top - first.bottom).toBeLessThanOrEqual(5);
    fixture.componentInstance.dialogOpen.set(false);
    viewport.next({
      matches: false,
      breakpoints: { '(min-width: 768px)': false, '(min-width: 1024px)': false },
    });
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.componentInstance.hasCollapsedColumns()).toBe(true);
    expect(content().querySelector('tbody button[aria-label="alice"]')).not.toBeNull();
    http.verify();
  });
});
//#endif
