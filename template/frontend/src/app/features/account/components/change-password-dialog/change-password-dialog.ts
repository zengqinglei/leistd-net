import { ChangeDetectionStrategy, Component, inject, model, signal } from '@angular/core';
import { form, required, pattern, validate, FormField } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEye, lucideEyeOff, lucideLock } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import {
  HlmInputGroup,
  HlmInputGroupInput,
  HlmInputGroupButton,
} from '@spartan-ng/helm/input-group';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { finalize } from 'rxjs/operators';

import { notify } from '../../../../core/notifications/notify';
import { AccountService } from '../../services/account-service';

const PASSWORD_RULE = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{8,20}$/;

@Component({
  selector: 'app-change-password-dialog',
  standalone: true,
  imports: [
    FormField,
    NgIcon,
    HlmButton,
    HlmSpinner,
    HlmInputGroup,
    HlmInputGroupInput,
    HlmInputGroupButton,
    ...HlmDialogImports,
    ...HlmFieldImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [provideIcons({ lucideEye, lucideEyeOff, lucideLock })],
  templateUrl: './change-password-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChangePasswordDialogComponent {
  readonly visible = model(false);

  private readonly accountService = inject(AccountService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  readonly dialogHeader = () => this.transloco.translate('account.changePassword.header');
  //#else
  readonly dialogHeader = () => 'Change Password';
  //#endif

  readonly saving = signal(false);

  // 密码可见性
  protected readonly showCurrentPassword = signal(false);
  protected readonly showNewPassword = signal(false);
  protected readonly showConfirmPassword = signal(false);

  // 表单模型（Signal Forms）
  private readonly model_ = signal({
    currentPassword: '',
    newPassword: '',
    confirmPassword: '',
  });

  //#if (IncludeLocalization)
  readonly changeForm = form(this.model_, (path) => {
    required(path.currentPassword, {
      message: this.transloco.translate('account.changePassword.currentPasswordRequired'),
    });
    required(path.newPassword, { message: '' });
    pattern(path.newPassword, PASSWORD_RULE, {
      message: this.transloco.translate('account.changePassword.newPasswordRuleError'),
    });
    validate(path.newPassword, (ctx) => {
      const newPassword = ctx.value();
      const currentPassword = ctx.valueOf(path.currentPassword);
      if (currentPassword && newPassword && currentPassword === newPassword) {
        return {
          kind: 'sameAsCurrent',
          message: this.transloco.translate('account.changePassword.sameAsCurrentError'),
        };
      }
      return null;
    });
    required(path.confirmPassword, {
      message: this.transloco.translate('account.changePassword.confirmPasswordRequired'),
    });
    validate(path.confirmPassword, (ctx) => {
      const confirm = ctx.value();
      const newPassword = ctx.valueOf(path.newPassword);
      if (newPassword && confirm && newPassword !== confirm) {
        return {
          kind: 'passwordMismatch',
          message: this.transloco.translate('account.changePassword.mismatchError'),
        };
      }
      return null;
    });
  });
  //#else
  readonly changeForm = form(this.model_, (path) => {
    required(path.currentPassword, { message: 'Please enter your current password' });
    required(path.newPassword, { message: '' });
    pattern(path.newPassword, PASSWORD_RULE, {
      message:
        'The new password does not meet the requirements; it must include uppercase and lowercase letters, numbers, and special characters',
    });
    validate(path.newPassword, (ctx) => {
      const newPassword = ctx.value();
      const currentPassword = ctx.valueOf(path.currentPassword);
      if (currentPassword && newPassword && currentPassword === newPassword) {
        return {
          kind: 'sameAsCurrent',
          message: 'The new password cannot be the same as the current password',
        };
      }
      return null;
    });
    required(path.confirmPassword, { message: 'Please re-enter the new password' });
    validate(path.confirmPassword, (ctx) => {
      const confirm = ctx.value();
      const newPassword = ctx.valueOf(path.newPassword);
      if (newPassword && confirm && newPassword !== confirm) {
        return { kind: 'passwordMismatch', message: 'The two new passwords do not match' };
      }
      return null;
    });
  });
  //#endif

  /** 桥接 hlm-dialog 声明式 state 到对外 visible 契约；打开时重置表单。 */
  onDialogStateChange(state: BrnDialogState): void {
    const open = state === 'open';
    this.visible.set(open);
    if (open) {
      this.resetForm();
    } else {
      this.showCurrentPassword.set(false);
      this.showNewPassword.set(false);
      this.showConfirmPassword.set(false);
    }
  }

  private resetForm(): void {
    this.model_.set({ currentPassword: '', newPassword: '', confirmPassword: '' });
  }

  onHide(): void {
    this.visible.set(false);
  }

  onSubmit(): void {
    if (this.changeForm().invalid()) {
      this.changeForm().markAsTouched();
      return;
    }

    this.saving.set(true);

    const { currentPassword, newPassword, confirmPassword } = this.model_();

    this.accountService
      .changePassword({ currentPassword, newPassword, confirmPassword })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          //#if (IncludeLocalization)
          notify.success(this.transloco.translate('common.success'), {
            detail: this.transloco.translate('account.changePassword.updateSuccess'),
          });
          //#else
          notify.success('Success', { detail: 'Password updated' });
          //#endif
          this.visible.set(false);
        },
      });
  }
}
