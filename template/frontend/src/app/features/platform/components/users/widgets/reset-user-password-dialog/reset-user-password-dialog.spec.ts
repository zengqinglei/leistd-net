import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { throwError, Observable, Subject, of } from 'rxjs';

import { ResetUserPasswordDialog } from './reset-user-password-dialog';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import {
  renderSubmission,
  rejectedField,
} from '../../../../../../core/validation/form-submission.testing';
import {
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
} from '../../../../../../core/validation/password-rule';
import { ResetUserPasswordInputDto } from '../../../../dtos/user-management.dto';

/**
 * 管理员重置用户口令：对话框负责口令规则与提交防重，重置请求与失败提示由用户页处理。
 *
 * 关闭后必须清空：对话框不销毁，上一次输入的口令会留给下一个被重置的用户。
 */
@Component({
  imports: [ResetUserPasswordDialog],
  template: ` <app-reset-user-password-dialog [(visible)]="visible" [saveAction]="saveAction" /> `,
})
class HostComponent {
  readonly visible = signal(true);
  readonly saving = signal(false);
  readonly saved: ResetUserPasswordInputDto[] = [];
  request: Observable<unknown> = of(undefined);
  readonly saveAction = (dto: ResetUserPasswordInputDto) => {
    this.saved.push(dto);
    return this.request;
  };
}

describe('ResetUserPasswordDialog', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  function dialog(): ResetUserPasswordDialog {
    return fixture.debugElement.query(By.directive(ResetUserPasswordDialog))
      .componentInstance as ResetUserPasswordDialog;
  }

  /** 对话框渲染在 document 上的浮层里。 */
  function passwordInput(): HTMLInputElement {
    return document.getElementById('reset-password') as HTMLInputElement;
  }

  function submitButton(): HTMLButtonElement {
    return document.querySelector<HTMLButtonElement>('hlm-dialog-footer button[type="submit"]')!;
  }

  /** 显示出来的校验提示：未触碰的字段错误元素在，但处于 hidden。 */
  function shownErrors(): number {
    return Array.from(
      passwordInput().closest('[hlmField]')!.querySelectorAll<HTMLElement>('hlm-field-error'),
    ).filter((element) => !element.hidden).length;
  }

  async function typePassword(value: string): Promise<void> {
    dialog().resetForm.password().value.set(value);
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

  it('associates the password label with its input', () => {
    expect(document.querySelector('label[for="reset-password"]')).not.toBeNull();
    expect(passwordInput()).not.toBeNull();
  });

  it('does not submit an empty password and reveals the error on submit', async () => {
    expect(shownErrors()).toBe(0);

    submitButton().click();
    await fixture.whenStable();

    expect(host.saved).toEqual([]);
    // 未触碰的字段不显示错误；点了提交就要让人看到为什么没反应
    expect(dialog().resetForm.password().touched()).toBe(true);
    expect(shownErrors()).toBeGreaterThan(0);
  });

  it.each([
    ['too short', 'x'.repeat(PASSWORD_MIN_LENGTH - 1), 'minLength'],
    ['too long', 'x'.repeat(PASSWORD_MAX_LENGTH + 1), 'maxLength'],
  ])('rejects a %s password', async (_, password, kind) => {
    await typePassword(password);

    expect(
      dialog()
        .resetForm.password()
        .errors()
        .map((error) => error.kind),
    ).toEqual([kind]);
    submitButton().click();
    expect(host.saved).toEqual([]);
  });

  // 口令里的空白是口令的一部分，被 trim 掉用户就再也登不进去
  it('submits the password exactly as typed', async () => {
    await typePassword(' Reset!Passw0rd ');

    submitButton().click();

    expect(host.saved).toEqual([{ password: ' Reset!Passw0rd ' }]);
  });

  it('blocks a second submit while the reset is in flight', async () => {
    await typePassword('Reset!Passw0rd');
    host.request = new Subject<unknown>();
    dialog().save();
    await fixture.whenStable();

    expect(submitButton().disabled).toBe(true);
    submitButton().click();
    expect(host.saved).toHaveLength(1);
  });

  it('toggles the password visibility', async () => {
    expect(passwordInput().type).toBe('password');

    passwordInput().parentElement!.querySelector<HTMLButtonElement>('button')!.click();
    await fixture.whenStable();

    expect(passwordInput().type).toBe('text');
  });

  it('clears the password and hides it again when the dialog closes', async () => {
    await typePassword('Reset!Passw0rd');
    passwordInput().parentElement!.querySelector<HTMLButtonElement>('button')!.click();
    await fixture.whenStable();

    dialog().onDialogStateChange('closed');
    await fixture.whenStable();
    expect(host.visible()).toBe(false);

    host.visible.set(true);
    await fixture.whenStable();

    expect(dialog().resetForm.password().value()).toBe('');
    expect(passwordInput().value).toBe('');
    expect(passwordInput().type).toBe('password');
  });

  it('review R1 focuses reset errors and preserves the exact password', async () => {
    await renderSubmission(fixture, () => dialog().save());
    expect(document.activeElement?.id).toBe('reset-password');
    dialog().resetForm.password().value.set(' Password123! strong ');
    host.request = throwError(() => rejectedField('Password'));
    await renderSubmission(fixture, () => dialog().save());
    expect(host.saved[0]).toEqual({ password: ' Password123! strong ' });
    expect(document.activeElement?.id).toBe('reset-password');
  });
});
