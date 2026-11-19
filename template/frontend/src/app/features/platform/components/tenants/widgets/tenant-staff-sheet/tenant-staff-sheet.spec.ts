//#if (TenantWorkspace && LocalIdentity)
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, ParamMap, provideRouter } from '@angular/router';
// prettier-ignore
import {
  BehaviorSubject,
  //#if (IncludeLocalization)
  of,
  //#endif
} from 'rxjs';
import { page } from 'vitest/browser';

import { TenantStaffSheet } from './tenant-staff-sheet';
import { ConfirmService } from '../../../../../../core/feedback/confirm-service';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { TenantStaffOutputDto } from '../../../../dtos/tenant-staff.dto';

const ROLES = [
  { id: 'admin', name: 'Admin', displayName: 'Administrator', isStatic: true },
  { id: 'support', name: 'support', displayName: 'Support', isStatic: false },
  { id: 'audit', name: 'audit', displayName: 'Auditor', isStatic: false },
];
const STAFF: TenantStaffOutputDto[] = [
  {
    homeUserId: 'home-1',
    memberId: 'member-1',
    username: 'alice',
    linkedUsername: 'platform.alice-old',
    displayName: 'Alice',
    isActive: true,
    creationTime: '2026-11-16T00:00:00Z',
    roles: ROLES,
  },
  {
    homeUserId: 'home-2',
    memberId: 'member-2',
    username: 'bob',
    linkedUsername: 'platform.bob',
    isActive: false,
    creationTime: '2026-11-17T00:00:00Z',
    roles: [ROLES[1]],
  },
];
const LABELS = ['Person', 'Linked account', 'Roles', 'Account status', 'Granted at'];

describe('TenantStaffSheet', () => {
  let fixture: ComponentFixture<TenantStaffSheet>;
  let http: HttpTestingController;
  let query: BehaviorSubject<ParamMap>;

  async function setup(tenantId = 'tenant-1'): Promise<void> {
    query = new BehaviorSubject(convertToParamMap({ staff: tenantId }));
    await TestBed.configureTestingModule({
      imports: [TenantStaffSheet],
      // prettier-ignore
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfirmService, useValue: { open: vi.fn().mockResolvedValue(false) } },
        {
          provide: ActivatedRoute,
          useValue: { queryParamMap: query, snapshot: { queryParamMap: query.value } },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en'], {
          getTranslation: () =>
            of({
              common: { details: 'View details', loadFailed: "Couldn't load the list" },
              tenants: {
                staff: {
                  summary: '{{name}} · {{count}} granted',
                  person: 'Person',
                  linkedUsername: 'Linked account',
                  roles: 'Roles',
                  status: 'Account status',
                  creationTime: 'Granted at',
                  actions: 'Actions',
                  active: 'Enabled',
                  inactive: 'Disabled',
                  edit: 'Edit roles',
                  remove: 'Remove access',
                  grant: 'Grant access',
                  noMatch: 'No matching staff',
                },
              },
            }),
        }),
        //#endif
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TenantStaffSheet);
    fixture.detectChanges();
  }

  async function flushLoad(
    staff: TenantStaffOutputDto[] = STAFF,
    tenantId = 'tenant-1',
  ): Promise<void> {
    http
      .expectOne(`/api/v1/tenants/${tenantId}`)
      .flush({ id: tenantId, name: 'acme', displayName: 'Acme Corp' });
    http.expectOne(`/api/v1/tenants/${tenantId}/staff`).flush(staff);
    http.expectOne(`/api/v1/tenants/${tenantId}/staff/roles`).flush(ROLES);
    await expect.poll(() => fixture.componentInstance.loading()).toBe(false);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const content = () => document.querySelector('hlm-sheet-content')!;
  const headers = () =>
    [...content().querySelectorAll('thead th')]
      .map((th) => th.textContent?.trim() ?? '')
      .filter((text) => text !== 'Actions');
  const displayed = (element: Element | null) =>
    !!element && getComputedStyle(element).display !== 'none';

  afterEach(() => {
    http.verify();
    fixture.destroy();
  });

  it('opens from the staff query only, loads no candidates and summarizes the full list', async () => {
    await setup();
    await flushLoad();
    http.expectNone((request) => request.url.endsWith('/candidates'));
    expect(content().querySelector('h2')?.classList).toContain('text-base');
    const summary = content().querySelector('[data-testid="staff-summary"]')!;
    expect(summary.classList).toContain('text-xs');
    expect(summary.textContent?.trim()).toBe('Acme Corp · 2 granted');

    // 抽屉 672px 放不下全部列：按优先级先收起授予时间，其余列按定义顺序
    expect(headers()).toEqual(LABELS.slice(0, 4));
    expect(fixture.componentInstance.hasCollapsedColumns()).toBe(true);
    const first = content().querySelector('tbody tr')!;
    expect(first.textContent).toContain('Alice');
    expect(first.textContent).toContain('alice');
    expect(first.querySelector('.font-mono')?.textContent?.trim()).toBe('platform.alice-old');
    const badges = [...first.querySelectorAll('app-readonly-list-popover [hlmBadge]')];
    expect(badges.map((badge) => badge.textContent?.trim())).toEqual([
      'Administrator',
      'Support',
      '+1',
    ]);
    expect(badges.every((badge) => badge.getAttribute('variant') === 'secondary')).toBe(true);
    expect(first.querySelector('[appStatusDot]')).not.toBeNull();

    fixture.componentInstance.search.set('platform.bob');
    fixture.detectChanges();
    expect(content().querySelectorAll('tbody tr')).toHaveLength(1);
    fixture.componentInstance.search.set('nobody');
    fixture.detectChanges();
    expect(content().querySelector('[data-testid="staff-no-match"]')?.textContent?.trim()).toBe(
      'No matching staff',
    );
    expect(content().querySelector('tbody button')).toBeNull();
    // 全集条数不受搜索影响
    expect(summary.textContent?.trim()).toBe('Acme Corp · 2 granted');

    query.next(convertToParamMap({}));
    TestBed.tick();
    expect(fixture.componentInstance.tenantId()).toBeNull();
  });

  it('tiles both row actions from sm and folds them into a menu with removal after a separator', async () => {
    await setup();
    await flushLoad();
    const row = () => content().querySelector('tbody tr')!;
    const edit = row().querySelector('[data-testid="staff-edit"]')!;
    const remove = row().querySelector('[data-testid="staff-remove"]')!;
    expect(displayed(edit)).toBe(true);
    expect(displayed(remove)).toBe(true);
    expect(displayed(row().querySelector('[data-testid="staff-menu"]'))).toBe(false);
    expect(edit.getAttribute('aria-label')).toBe('Edit roles - Alice');
    expect(remove.getAttribute('aria-label')).toBe('Remove access - Alice');
    expect(remove.classList).toContain('text-destructive');

    await page.viewport(390, 844);
    onTestFinished(() => page.viewport(1280, 800));
    await expect.poll(() => fixture.componentInstance.hasCollapsedColumns()).toBe(true);
    fixture.detectChanges();
    expect(displayed(row().querySelector('[data-testid="staff-edit"]'))).toBe(false);
    expect(displayed(row().querySelector('[data-testid="staff-remove"]'))).toBe(false);
    const menu = row().querySelector<HTMLButtonElement>('[data-testid="staff-menu"]')!;
    expect(displayed(menu)).toBe(true);
    menu.click();
    await fixture.whenStable();
    const items = [...document.querySelectorAll('hlm-dropdown-menu > *')].map((item) =>
      item.tagName === 'HLM-DROPDOWN-MENU-SEPARATOR' ? '|' : (item.textContent?.trim() ?? ''),
    );
    expect(items).toEqual(['Edit roles', '|', 'Remove access']);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    // 被收起的列在行展开里用 8rem 标签列的 dl 补出，标签与表头同源
    row().querySelector<HTMLButtonElement>('button[aria-expanded]')!.click();
    fixture.detectChanges();
    const detail = content().querySelector('[data-testid="staff-detail"]')!;
    expect(detail.classList).toContain('grid-cols-[8rem_minmax(0,1fr)]');
    expect([...detail.querySelectorAll('dt')].map((dt) => dt.textContent?.trim())).toEqual(
      LABELS.slice(1).filter((label) => !headers().includes(label)),
    );
    expect(detail.querySelector('[data-detail-column="linkedUsername"]')?.textContent).toContain(
      'platform.alice-old',
    );
  });

  it('offers grant access from the empty state and retries a failed first load', async () => {
    await setup();
    await flushLoad([]);
    const empty = content().querySelector('[data-testid="staff-empty"]')!;
    expect(empty.querySelector('[hlmEmptyTitle]')).not.toBeNull();
    expect(empty.querySelector('[hlmEmptyDescription]')).not.toBeNull();
    empty.querySelector<HTMLButtonElement>('[hlmEmptyContent] button')!.click();
    expect(fixture.componentInstance.dialogOpen()).toBe(true);
    expect(fixture.componentInstance.editing()).toBeNull();
    fixture.componentInstance.dialogOpen.set(false);

    query.next(convertToParamMap({ staff: 'tenant-2' }));
    fixture.detectChanges();
    http
      .expectOne('/api/v1/tenants/tenant-2')
      .flush({ title: 'Down' }, { status: 503, statusText: 'Unavailable' });
    http.expectOne('/api/v1/tenants/tenant-2/staff').flush([]);
    http.expectOne('/api/v1/tenants/tenant-2/staff/roles').flush([]);
    await expect.poll(() => fixture.componentInstance.error()).not.toBeNull();
    fixture.detectChanges();
    const error = content().querySelector('[data-testid="staff-error"]')!;
    expect(error.querySelector('[role="alert"]')).not.toBeNull();
    error.querySelector<HTMLButtonElement>('[hlmEmptyContent] button')!.click();
    await flushLoad(STAFF, 'tenant-2');
    expect(content().querySelector('[data-testid="staff-error"]')).toBeNull();
    expect(content().querySelectorAll('tbody tr').length).toBeGreaterThan(0);
  });

  it('keeps rows while reloading after a save and only marks the list busy', async () => {
    await setup();
    await flushLoad();
    fixture.componentInstance.openGrant(STAFF[1]);
    expect(fixture.componentInstance.editing()).toEqual(STAFF[1]);
    expect(fixture.componentInstance.grantedIds()).toEqual(new Set(['home-1', 'home-2']));

    fixture.componentInstance.onSaved();
    fixture.detectChanges();
    expect(fixture.componentInstance.dialogOpen()).toBe(false);
    expect(content().querySelector('tbody')?.classList).toContain('opacity-50');
    expect(content().querySelector('tbody')?.textContent).toContain('Alice');
    expect(content().querySelector('[aria-busy="true"]')).not.toBeNull();
    await flushLoad([STAFF[0]]);
    expect(content().querySelector('tbody')?.classList).not.toContain('opacity-50');
    expect(content().querySelector('[data-testid="staff-summary"]')?.textContent?.trim()).toBe(
      'Acme Corp · 1 granted',
    );
  });
});
//#endif
