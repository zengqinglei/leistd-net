import { HttpErrorResponse } from '@angular/common/http';
import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject, throwError, Observable, of } from 'rxjs';
import { userEvent } from 'vitest/browser';

import { RoleEditDialog } from './role-edit-dialog';
import { ApplicationHttpError } from '../../../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../../../core/feedback/confirm-service';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { CreateRoleInputDto, RoleOutputDto, UpdateRoleInputDto } from '../../../../dtos/role.dto';

/**
 * 角色新建与编辑共用一个对话框：名称是稳定业务标识，只在新建时可填、按服务端同一规则校验，
 * 编辑载荷里不能再带名称。对话框只负责校验与组装载荷，父级提供 HTTP 动作和成功副作用，弹窗持有提交与字段错误。
 */
@Component({
  imports: [RoleEditDialog],
  template: `<app-role-edit-dialog [(open)]="open" [role]="role()" [saveAction]="save" />`,
})
class HostComponent {
  readonly open = signal(true);
  readonly role = signal<RoleOutputDto | null>(null);
  readonly saved: (CreateRoleInputDto | UpdateRoleInputDto)[] = [];
  request: Observable<unknown> = of(undefined);
  readonly save = (dto: CreateRoleInputDto | UpdateRoleInputDto) => {
    this.saved.push(dto);
    return this.request;
  };
}

describe('RoleEditDialog', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  const existing: RoleOutputDto = {
    id: '019ff8ed-221b-7673-9ba8-6b6dd5a638ab',
    name: 'auditor',
    displayName: 'Auditor',
    description: 'Reads audit trails',
    sort: 3,
    isDefault: true,
    isStatic: false,
    userCount: 0,
    permissionCount: 0,
    creationTime: '2026-08-14T00:00:00Z',
  };

  /** 对话框实例：表单值只能从它的 roleForm 写入，才能覆盖真实的校验规则。 */
  function dialog(): RoleEditDialog {
    return fixture.debugElement.query(By.directive(RoleEditDialog))
      .componentInstance as RoleEditDialog;
  }

  /** 对话框渲染在 document 上的浮层里，不在宿主元素下面。 */
  function saveButton(): HTMLButtonElement {
    return document.querySelector<HTMLButtonElement>('hlm-dialog-footer button:last-of-type')!;
  }

  function errorKinds(field: 'name' | 'displayName' | 'description'): string[] {
    return dialog()
      .roleForm[field]()
      .errors()
      .map((error) => error.kind);
  }

  async function fill(values: { name?: string; displayName?: string; description?: string }) {
    for (const [key, value] of Object.entries(values)) {
      dialog().roleForm[key as 'name' | 'displayName' | 'description']().value.set(value);
    }
    await fixture.whenStable();
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      // prettier-ignore
      providers: [
        provideZonelessChangeDetection(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });

    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    await fixture.whenStable();
  });

  afterEach(() => fixture.destroy());

  it('associates every field label with its control', () => {
    for (const id of ['role-name', 'role-display-name', 'role-description', 'role-is-default']) {
      expect(document.querySelector(`label[for="${id}"]`)).not.toBeNull();
      expect(document.getElementById(id)).not.toBeNull();
    }
  });

  it('allows submitting an empty form and focuses its first invalid control', async () => {
    expect(errorKinds('name')).toEqual(['required']);
    expect(errorKinds('displayName')).toEqual(['required']);
    expect(saveButton().disabled).toBe(false);

    dialog().onSubmit();
    await fixture.whenStable();
    expect(host.saved).toEqual([]);
    expect(document.activeElement?.id).toBe('role-name');
  });

  // 与服务端 CreateRoleInputDto 同一规则：只校验必填的话，格式不对要等提交被拒才第一次知道
  it.each(['a', 'role name', 'role-name', '角色', 'a'.repeat(65)])(
    'rejects %j as a role name with a single rule message',
    async (name) => {
      await fill({ name, displayName: 'Role' });

      expect(errorKinds('name')).toEqual(['roleNamePattern']);
      expect(saveButton().disabled).toBe(false);
      dialog().onSubmit();
      expect(host.saved).toEqual([]);
    },
  );

  it('rejects an overlong display name or description', async () => {
    await fill({ name: 'auditor', displayName: 'x'.repeat(129), description: 'x'.repeat(513) });

    expect(errorKinds('displayName')).toEqual(['maxLength']);
    expect(errorKinds('description')).toEqual(['maxLength']);
    dialog().onSubmit();
    expect(host.saved).toEqual([]);
  });

  it('sends the trimmed create payload and omits a blank description', async () => {
    await fill({ name: 'report_viewer', displayName: '  Report viewer  ', description: '   ' });

    saveButton().click();

    expect(host.saved).toEqual([
      {
        name: 'report_viewer',
        displayName: 'Report viewer',
        description: undefined,
        sort: 0,
        isDefault: false,
      },
    ]);
  });

  it('normalizes a pasted identifier before validation and submission', async () => {
    await fill({ name: '  auditor  ', displayName: 'Auditor' });
    dialog().onSubmit();
    await fixture.whenStable();
    expect(host.saved[0]).toMatchObject({ name: 'auditor' });
  });

  it('refills the form from the role in edit mode and locks the name', async () => {
    host.role.set(existing);
    await fixture.whenStable();

    expect(dialog().isEdit()).toBe(true);
    expect(dialog().roleForm.name().value()).toBe('auditor');
    expect(dialog().roleForm.name().disabled()).toBe(true);
    expect(dialog().roleForm.displayName().value()).toBe('Auditor');
    expect((document.getElementById('role-name') as HTMLInputElement).disabled).toBe(true);
  });

  // 编辑载荷带上名称，等于给了一个改不掉却看似能改的字段
  it('sends the update payload without the name', async () => {
    host.role.set(existing);
    await fixture.whenStable();
    await fill({ displayName: '  Senior auditor ', description: '' });

    saveButton().click();

    expect(host.saved).toEqual([
      { displayName: 'Senior auditor', description: undefined, sort: 3, isDefault: true },
    ]);
  });

  // 对话框不销毁：编辑过的值留到下一次新建，用户会在不知情时建出一个重名角色
  it('resets to a blank form when reopened for a new role after editing', async () => {
    host.role.set(existing);
    await fixture.whenStable();
    host.open.set(false);
    await fixture.whenStable();

    host.role.set(null);
    host.open.set(true);
    await fixture.whenStable();

    expect(dialog().isEdit()).toBe(false);
    expect(dialog().roleForm.name().value()).toBe('');
    expect(dialog().roleForm.name().disabled()).toBe(false);
    expect(dialog().roleForm.displayName().value()).toBe('');
  });
  it('submits through a real Enter key and keeps the request in flight', async () => {
    const request = new Subject<unknown>();
    host.request = request;
    await userEvent.fill(document.getElementById('role-name')!, 'keyboard_role');
    await userEvent.fill(document.getElementById('role-display-name')!, 'Keyboard role');
    await userEvent.keyboard('{Enter}');
    await fixture.whenStable();
    expect(host.saved).toHaveLength(1);
    expect(dialog().roleForm().submitting()).toBe(true);
    await userEvent.keyboard('{Enter}');
    expect(host.saved).toHaveLength(1);
    request.next(undefined);
    request.complete();
    await fixture.whenStable();
    await expect.poll(() => dialog().roleForm().submitting()).toBe(false);
  });

  it('renders a server field rejection and focuses that field', async () => {
    host.request = throwError(() =>
      ApplicationHttpError.from(
        new HttpErrorResponse({
          status: 400,
          error: { errors: [{ field: 'DisplayName', detail: 'Choose another display name' }] },
        }),
      ),
    );
    await fill({ name: 'valid_role', displayName: 'Taken' });
    await dialog().onSubmit();
    await fixture.whenStable();
    expect(dialog().roleForm.displayName().errors()[0].message).toBe('Choose another display name');
    expect(document.activeElement?.id).toBe('role-display-name');
    expect(document.body.textContent).toContain('Choose another display name');
  });

  it.each(['escape', 'close', 'cancel'])(
    'confirms dirty %s exits and preserves the draft when editing continues',
    async (entry) => {
      const confirm = vi.spyOn(TestBed.inject(ConfirmService), 'open').mockResolvedValue(false);
      await userEvent.fill(document.getElementById('role-name')!, 'unsaved_role');
      if (entry === 'escape') await userEvent.keyboard('{Escape}');
      else if (entry === 'close')
        (document.querySelector('app-dialog-close button') as HTMLButtonElement).click();
      else (document.querySelector('hlm-dialog-footer button') as HTMLButtonElement).click();
      await fixture.whenStable();
      expect(confirm).toHaveBeenCalledTimes(1);
      expect(host.open()).toBe(true);
      expect(dialog().roleForm.name().value()).toBe('unsaved_role');
      confirm.mockRestore();
    },
  );

  it('locks all four exits on the active dialog during a delayed save', async () => {
    const request = new Subject<unknown>();
    host.request = request;
    await fill({ name: 'valid_role', displayName: 'Valid' });
    const saving = dialog().onSubmit();
    await fixture.whenStable();
    (document.querySelector('.cdk-overlay-backdrop') as HTMLElement).click();
    document.getElementById('role-name')!.focus();
    await userEvent.keyboard('{Escape}');
    (document.querySelector('app-dialog-close button') as HTMLButtonElement).click();
    (document.querySelector('hlm-dialog-footer button') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(host.open()).toBe(true);
    expect(document.getElementById('role-name')).not.toBeNull();
    request.next(undefined);
    request.complete();
    await saving;
  });
  it('opens one discard confirmation and closes only after discard is accepted', async () => {
    let decide!: (value: boolean) => void;
    const confirm = vi.spyOn(TestBed.inject(ConfirmService), 'open').mockReturnValue(
      new Promise<boolean>((resolve) => {
        decide = resolve;
      }),
    );
    await userEvent.fill(document.getElementById('role-name')!, 'unsaved_role');
    const cancel = document.querySelector('hlm-dialog-footer button') as HTMLButtonElement;
    cancel.click();
    cancel.click();
    await fixture.whenStable();
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(host.open()).toBe(true);
    decide(true);
    await expect.poll(() => host.open()).toBe(false);
    confirm.mockRestore();
  });

  it('resets touched and dirty when a new editing session opens', async () => {
    await userEvent.fill(document.getElementById('role-name')!, 'old_draft');
    dialog().roleForm().markAsTouched();
    expect(dialog().roleForm().dirty()).toBe(true);
    host.open.set(false);
    await fixture.whenStable();
    host.open.set(true);
    await fixture.whenStable();
    expect(dialog().roleForm().dirty()).toBe(false);
    expect(dialog().roleForm().touched()).toBe(false);
    expect(dialog().roleForm.name().value()).toBe('');
  });
});
