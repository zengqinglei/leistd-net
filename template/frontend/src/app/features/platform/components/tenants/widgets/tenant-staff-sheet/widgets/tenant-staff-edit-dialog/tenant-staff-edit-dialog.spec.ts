//#if (TenantWorkspace && LocalIdentity)
import {
  HttpErrorResponse,
  HttpInterceptorFn,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { toast } from '@spartan-ng/brain/sonner';
// prettier-ignore
import { catchError, throwError,
  //#if (IncludeLocalization)
  of,
  //#endif
} from 'rxjs';
import { page, userEvent } from 'vitest/browser';

import { ApplicationHttpError } from '../../../../../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../../../../../core/feedback/confirm-service';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../../../core/i18n/transloco.testing';
//#endif
import { renderSubmission } from '../../../../../../../../core/validation/form-submission.testing';
import { focusWithKeyboard } from '../../../../../../../../shared/utils/keyboard-focus.testing';
import { RoleBriefDto } from '../../../../../../dtos/role.dto';
import { TenantStaffOutputDto } from '../../../../../../dtos/tenant-staff.dto';
import { TENANT_STAFF_CANDIDATE_LIMIT } from '../../../../../../dtos/tenant-staff.dto';
import { TenantStaffEditDialog } from './tenant-staff-edit-dialog';

import type { MockInstance } from 'vitest';

const ROLES = [
  { id: 'admin', name: 'Admin', displayName: 'Administrator', isStatic: true },
  { id: 'support', name: 'support', displayName: 'Support', isStatic: false },
];
const CANDIDATES_URL = '/api/v1/tenants/tenant-1/staff/candidates';
const MEMBER: TenantStaffOutputDto = {
  homeUserId: 'home-1',
  memberId: 'member-1',
  username: 'alice',
  linkedUsername: 'platform.alice-old',
  displayName: 'Alice',
  isActive: false,
  creationTime: '2026-11-16T00:00:00Z',
  roles: [ROLES[0]],
};
/** 测试里把错误响应归一化为 `ApplicationHttpError`，与应用里的错误拦截器一致。 */
const normalizeErrors: HttpInterceptorFn = (request, next) =>
  next(request).pipe(
    catchError((error: unknown) =>
      throwError(() =>
        error instanceof HttpErrorResponse ? ApplicationHttpError.from(error) : error,
      ),
    ),
  );

describe('TenantStaffEditDialog', () => {
  let fixture: ComponentFixture<TenantStaffEditDialog>;
  let http: HttpTestingController;
  let success: MockInstance<(typeof toast)['success']>;

  async function setup(
    member: TenantStaffOutputDto | null = null,
    roles: RoleBriefDto[] = ROLES,
  ): Promise<void> {
    TestBed.configureTestingModule({
      imports: [TenantStaffEditDialog],
      // prettier-ignore
      providers: [
        provideHttpClient(withInterceptors([normalizeErrors])),
        provideHttpClientTesting(),
        { provide: ConfirmService, useValue: { open: vi.fn().mockResolvedValue(true) } },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en'], {
          getTranslation: () =>
            of({
              common: { save: 'Save' },
              validation: { required: 'This field is required.' },
              tenants: {
                staff: {
                  grant: 'Grant access',
                  editTitle: 'Edit roles',
                  granted: 'Already granted',
                  grantSucceeded: 'Granted {{person}} access to {{name}}',
                  rolesSaved: 'Roles updated for {{person}}',
                  linkedPreview: 'A new linked account uses {{username}}',
                  roleCount: '{{count}} selected',
                  rolesRequired: 'Select at least one role.',
                  candidateLimit: 'Showing up to 200 people. Keep typing to narrow the results.',
                },
              },
            }),
        }),
        //#endif
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TenantStaffEditDialog);
    fixture.componentRef.setInput('tenantId', 'tenant-1');
    fixture.componentRef.setInput('tenantName', 'acme');
    fixture.componentRef.setInput('roles', roles);
    fixture.componentRef.setInput('member', member);
    fixture.componentRef.setInput('grantedIds', new Set(['home-2']));
    fixture.componentInstance.open.set(true);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function flushCandidates(count = 2, first = { username: 'alice', displayName: 'Alice' }): void {
    const request = http.expectOne((req) => req.url === CANDIDATES_URL);
    expect(request.request.params.get('keyword')).toBe('');
    request.flush(
      Array.from({ length: count }, (_, index) =>
        index === 0
          ? { id: 'home-1', ...first }
          : { id: `home-${index + 1}`, username: `user${index + 1}` },
      ),
    );
  }

  const dialog = () => document.querySelector('hlm-dialog-content')!;
  const trigger = () => dialog().querySelector<HTMLButtonElement>('#tenant-staff-person')!;
  const submit = () => dialog().querySelector<HTMLButtonElement>('button[type="submit"]')!;
  const roleFieldset = () => dialog().querySelector<HTMLFieldSetElement>('fieldset')!;

  async function openPicker(): Promise<void> {
    trigger().click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    success = vi.spyOn(toast, 'success').mockImplementation(() => '');
  });
  afterEach(() => {
    success.mockRestore();
    http.verify();
    fixture.destroy();
  });

  it('reports a missing person and missing roles under their fields, focuses the first and sends nothing', async () => {
    await setup();
    flushCandidates();
    await fixture.whenStable();
    expect(submit().disabled).toBe(false);
    expect(submit().textContent).toContain('Grant access');

    await renderSubmission(fixture, () => fixture.componentInstance.save());

    http.expectNone((request) => request.method === 'POST');
    expect(trigger().getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(trigger());
    expect(
      trigger().closest('[hlmField]')?.querySelector('hlm-field-error')?.textContent,
    ).toContain('This field is required.');
    expect(roleFieldset().getAttribute('aria-invalid')).toBe('true');
    expect(document.getElementById('tenant-staff-role-errors')?.textContent).toContain(
      'Select at least one role.',
    );
    expect(submit().disabled).toBe(false);
  });

  it('searches in the popover and a real Enter there does not submit a ready form', async () => {
    await setup();
    flushCandidates();
    await fixture.whenStable();
    await openPicker();
    const options = [...document.querySelectorAll<HTMLElement>('[role="option"]')];
    expect(options).toHaveLength(2);
    expect(options[1].getAttribute('aria-disabled')).toBe('true');
    expect(options[1].textContent).toContain('Already granted');
    expect(options[0].textContent).not.toContain('Already granted');
    expect(document.querySelector('[data-testid="staff-candidate-limit"]')).toBeNull();

    // 先选好人员和角色，表单处于可提交状态
    options[0].click();
    fixture.componentInstance.toggleRole('admin');
    await fixture.whenStable();
    const form = dialog().querySelector('form')!;
    const submitted = vi.fn((event: Event) => event.preventDefault());
    form.addEventListener('submit', submitted);
    const save = vi.spyOn(fixture.componentInstance, 'save');

    // 在弹出层的搜索框里真实键入，经防抖后按关键字请求服务端
    await openPicker();
    const search = document.querySelector<HTMLInputElement>('input[brnComboboxInput]')!;
    await userEvent.click(search);
    await userEvent.keyboard('ali');
    const searched = () =>
      http.match((req) => req.url === CANDIDATES_URL && req.params.get('keyword') === 'ali');
    await expect.poll(searched).toHaveLength(1);

    await userEvent.keyboard('{Enter}');
    await fixture.whenStable();
    expect(submitted).not.toHaveBeenCalled();
    expect(save).not.toHaveBeenCalled();
    http.expectNone((request) => request.method === 'POST');
    expect(fixture.componentInstance.open()).toBe(true);
    expect(fixture.componentInstance.accessForm.homeUserId().value()).toBe('home-1');
    // 关闭弹出层会清空搜索词，候选列表回到全部
    await expect
      .poll(() =>
        http.match((req) => req.url === CANDIDATES_URL && req.params.get('keyword') === ''),
      )
      .toHaveLength(1);

    // 对照：同一个表单用提交按钮确实能提交，上面的"没有提交"不是因为表单本身交不出去
    save.mockRestore();
    await userEvent.click(submit());
    expect(submitted).toHaveBeenCalledTimes(1);
    http.expectOne('/api/v1/tenants/tenant-1/staff').flush(null);
    await expect.poll(() => success.mock.calls.length).toBe(1);
  });

  it('keeps long, similar role names and the linked account readable at 390px', async () => {
    await page.viewport(390, 844);
    onTestFinished(() => page.viewport(1280, 800));
    const roles = [
      {
        id: 'east-read',
        name: 'regional_operations_supervisor_east_china_readonly',
        displayName: 'Regional operations supervisor (East China, read only)',
      },
      {
        id: 'east-edit',
        name: 'regional_operations_supervisor_east_china_editor',
        displayName: 'Regional operations supervisor (East China, editor)',
      },
    ];
    const linkedUsername = 'platform.alexandra.konstantinopoulou.montgomery.previous';
    await setup(
      {
        ...MEMBER,
        displayName: 'Alexandra Konstantinopoulou-Montgomery',
        linkedUsername,
        roles: [],
      },
      roles,
    );
    // 每段文字完整出现在一个元素里，且没有被省略号或溢出裁掉
    const readable = (element: HTMLElement, text: string) => {
      expect(element.textContent?.trim()).toBe(text);
      expect(getComputedStyle(element).textOverflow).not.toBe('ellipsis');
      expect(element.scrollWidth).toBeLessThanOrEqual(element.clientWidth);
    };
    const rows = [...dialog().querySelectorAll<HTMLElement>('[data-testid="staff-role"]')];
    rows.forEach((row, index) => {
      const [name, code] = [...row.querySelectorAll<HTMLElement>('label > span')];
      readable(name, roles[index].displayName);
      readable(code, roles[index].name);
    });
    const member = dialog().querySelector<HTMLElement>('[data-testid="staff-member"]')!;
    const [person, account] = [...member.querySelectorAll<HTMLElement>('div > span')];
    readable(person, 'Alexandra Konstantinopoulou-Montgomery');
    readable(account, linkedUsername);
  });

  it('gives the single-line person trigger a focusable full-text tooltip when it truncates', async () => {
    await page.viewport(390, 844);
    onTestFinished(() => page.viewport(1280, 800));
    await setup();
    flushCandidates(2, {
      username: 'alexandra.konstantinopoulou.montgomery',
      displayName: 'Alexandra Konstantinopoulou-Montgomery',
    });
    await fixture.whenStable();
    await openPicker();
    document.querySelector<HTMLElement>('[role="option"][data-value="home-1"]')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    await focusWithKeyboard(trigger());
    await expect
      .poll(() => document.querySelector('[role="tooltip"]')?.textContent?.trim() ?? '')
      .toBe('Alexandra Konstantinopoulou-Montgomery alexandra.konstantinopoulou.montgomery');
    trigger().blur();
  });

  it('hints at the 200 limit, previews the linked account and grants with a toast', async () => {
    await setup();
    const saved = vi.fn();
    fixture.componentInstance.saved.subscribe(saved);
    flushCandidates(TENANT_STAFF_CANDIDATE_LIMIT);
    await fixture.whenStable();
    await openPicker();
    expect(document.querySelector('[data-testid="staff-candidate-limit"]')?.textContent).toContain(
      'Showing up to 200 people',
    );
    document.querySelector<HTMLElement>('[role="option"][data-value="home-1"]')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(dialog().querySelector('[data-testid="staff-picked"]')?.textContent).toContain('Alice');
    expect(dialog().querySelector('[data-testid="staff-picked"]')?.textContent).toContain('alice');
    expect(dialog().querySelector('[data-testid="staff-linked-preview"]')?.textContent).toContain(
      'platform.alice',
    );

    const rows = [...dialog().querySelectorAll<HTMLElement>('[data-testid="staff-role"]')];
    expect(rows.map((row) => row.querySelector('.font-mono')?.textContent?.trim())).toEqual([
      'Admin',
      'support',
    ]);
    expect(rows.map((row) => !!row.querySelector('[hlmBadge]'))).toEqual([true, false]);
    rows[1].querySelector<HTMLLabelElement>('label')!.click();
    await fixture.whenStable();
    expect(dialog().querySelector('[data-testid="staff-role-count"]')?.textContent).toContain(
      '1 selected',
    );

    const saving = fixture.componentInstance.save();
    await fixture.whenStable();
    expect(submit().disabled).toBe(true);
    const grant = http.expectOne('/api/v1/tenants/tenant-1/staff');
    expect(grant.request.body).toEqual({ homeUserId: 'home-1', roleIds: ['support'] });
    grant.flush(null);
    await saving;
    expect(success).toHaveBeenCalledWith('Granted Alice access to acme');
    expect(saved).toHaveBeenCalledTimes(1);
  });

  it('puts a taken linked username under the person field', async () => {
    await setup();
    flushCandidates();
    await fixture.whenStable();
    await openPicker();
    document.querySelector<HTMLElement>('[role="option"][data-value="home-1"]')!.click();
    fixture.componentInstance.toggleRole('admin');
    await fixture.whenStable();
    await renderSubmission(fixture, async () => {
      const saving = fixture.componentInstance.save();
      http
        .expectOne('/api/v1/tenants/tenant-1/staff')
        .flush(
          { code: 'User:UsernameTaken', detail: 'platform.alice is taken' },
          { status: 409, statusText: 'Conflict' },
        );
      await saving;
    });
    expect(
      trigger().closest('[hlmField]')?.querySelector('hlm-field-error')?.textContent,
    ).toContain('platform.alice is taken');
    expect(document.activeElement).toBe(trigger());
    expect(success).not.toHaveBeenCalled();
  });

  it('edits roles for a named member without loading candidates and confirms separately', async () => {
    await setup(MEMBER);
    http.expectNone((req) => req.url === CANDIDATES_URL);
    expect(dialog().querySelector('h2')?.textContent).toContain('Edit roles');
    expect(trigger()).toBeNull();
    const member = dialog().querySelector('[data-testid="staff-member"]')!;
    expect(member.textContent).toContain('Alice');
    expect(member.textContent).toContain('platform.alice-old');
    expect(member.querySelector('[hlmBadge][appStatusDot]')).not.toBeNull();
    expect(dialog().querySelector('[data-testid="staff-role-count"]')?.textContent).toContain(
      '1 selected',
    );
    expect(submit().textContent).toContain('Save');

    fixture.componentInstance.toggleRole('admin');
    await renderSubmission(fixture, () => fixture.componentInstance.save());
    http.expectNone((request) => request.method === 'POST');
    expect(document.activeElement).toBe(roleFieldset());

    fixture.componentInstance.toggleRole('support');
    const saving = fixture.componentInstance.save();
    const edit = http.expectOne('/api/v1/tenants/tenant-1/staff');
    expect(edit.request.body).toEqual({ homeUserId: 'home-1', roleIds: ['support'] });
    edit.flush(null);
    await saving;
    expect(success).toHaveBeenCalledWith('Roles updated for Alice');
  });
});
//#endif
