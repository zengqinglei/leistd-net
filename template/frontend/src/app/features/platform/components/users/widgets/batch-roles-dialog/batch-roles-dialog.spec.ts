import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';

import { ConfirmService } from '../../../../../../core/feedback/confirm-service';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import {
  renderSubmission,
  rejectedField,
} from '../../../../../../core/validation/form-submission.testing';
import { BatchRolesDialog } from './batch-roles-dialog';

/** 批量角色的业务前提与异步边界，零选择有可见说明、选项失败有重试。 */
describe('BatchRolesDialog', () => {
  let fixture: ComponentFixture<BatchRolesDialog>;
  const save = vi.fn(() => of(undefined));
  const confirm = { open: vi.fn().mockResolvedValue(false) };

  beforeEach(async () => {
    save.mockClear();
    confirm.open.mockClear();
    TestBed.configureTestingModule({
      imports: [BatchRolesDialog],
      // prettier-ignore
      providers: [
        { provide: ConfirmService, useValue: confirm },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });
    fixture = TestBed.createComponent(BatchRolesDialog);
    fixture.componentRef.setInput('saveAction', save);
    fixture.componentRef.setInput('availableRoles', [
      { id: 'role-1', name: 'editor', displayName: 'Editor' },
    ]);
    fixture.componentRef.setInput('userCount', 2);
    fixture.componentInstance.open.set(true);
    await fixture.whenStable();
  });

  afterEach(() => fixture.destroy());

  it('review R1 explains the zero-selection prerequisite and does not save', async () => {
    const button = document.querySelector<HTMLButtonElement>(
      'hlm-dialog-footer button[type="submit"]',
    )!;
    expect(button.disabled).toBe(true);
    expect(document.querySelector('[role="status"]')?.textContent?.trim()).toBeTruthy();
    await fixture.componentInstance.onSave();
    expect(save).not.toHaveBeenCalled();
  });

  it('announces field errors only with the visible submitted error and clears on reset', async () => {
    const fieldset = document.querySelector<HTMLElement>('fieldset')!;
    const state = () =>
      ['aria-invalid', 'aria-describedby', 'tabindex'].map((name) => fieldset.getAttribute(name));
    expect(state()).toEqual([null, null, null]);
    fixture.componentInstance.toggle('role-1');
    fixture.componentRef.setInput('saveAction', () => throwError(() => rejectedField('roleIds')));
    await renderSubmission(fixture, () => fixture.componentInstance.onSave());
    expect(state()).toEqual(['true', 'batch-role-errors', '0']);
    expect(document.activeElement).toBe(fieldset);
    expect(document.getElementById('batch-role-errors')?.textContent).toContain(
      'Server rejected this field',
    );
    fixture.componentInstance.toggle('role-1');
    await fixture.whenStable();
    expect(state()).toEqual([null, null, null]);
    fixture.componentInstance.selectionForm().reset();
    await fixture.whenStable();
    expect(state()).toEqual([null, null, null]);
  });

  it('review R1 shows option failure with a retry action and blocks submission', async () => {
    const reload = vi.fn();
    fixture.componentInstance.reloadRoles.subscribe(reload);
    fixture.componentRef.setInput('rolesError', 'Role options failed');
    await fixture.whenStable();
    document.querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    expect(reload).toHaveBeenCalledOnce();
    expect(document.querySelector('[role="alert"]')!.textContent).toContain('Role options failed');
    fixture.componentInstance.toggle('role-1');
    await fixture.componentInstance.onSave();
    expect(save).not.toHaveBeenCalled();
  });

  it('review R1 owns the selected data through one pending request and confirms discarding it', async () => {
    const pending = new Subject<unknown>();
    fixture.componentRef.setInput(
      'saveAction',
      vi.fn(() => pending),
    );
    fixture.componentInstance.toggle('role-1');
    await fixture.whenStable();
    const submitted = fixture.componentInstance.onSave();
    expect(fixture.componentInstance.selectionForm().submitting()).toBe(true);
    pending.next(undefined);
    pending.complete();
    await submitted;
    await fixture.whenStable();
    document.querySelector<HTMLButtonElement>('app-dialog-close button')!.click();
    await fixture.whenStable();
    expect(confirm.open).toHaveBeenCalledOnce();
    expect(fixture.componentInstance.open()).toBe(true);
  });
});
