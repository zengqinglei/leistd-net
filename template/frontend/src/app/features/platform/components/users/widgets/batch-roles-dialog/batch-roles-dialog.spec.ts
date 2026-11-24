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

  // 高度在打开时确定：加载中只有行骨架，不叠加空态、选择提示与裸 spinner；加载完成（含为空）弹窗高度不变
  it('keeps the dialog height from loading to an empty or filled role list', async () => {
    const roles = [{ id: 'role-1', name: 'editor', displayName: 'Editor' }];
    fixture.componentRef.setInput('availableRoles', []);
    fixture.componentRef.setInput('rolesLoading', true);
    await fixture.whenStable();
    const content = document.querySelector<HTMLElement>('[data-slot="dialog-content"]')!;
    const shown = () =>
      ['app-list-loading', '[data-testid="roles-empty"]', 'p[role="status"]', 'fieldset'].map(
        (selector) => !!content.querySelector(selector),
      );
    expect(shown()).toEqual([true, false, false, false]);
    expect(content.querySelector('[data-dialog-body] hlm-spinner')).toBeNull();
    const height = content.offsetHeight;

    fixture.componentRef.setInput('rolesLoading', false);
    await fixture.whenStable();
    expect(shown()).toEqual([false, true, false, false]);
    expect(content.offsetHeight).toBe(height);

    fixture.componentRef.setInput('availableRoles', roles);
    await fixture.whenStable();
    expect(shown()).toEqual([false, false, true, true]);
    expect(content.offsetHeight).toBe(height);
  });

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
    document.querySelector<HTMLButtonElement>('[data-testid="roles-retry"]')!.click();
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
