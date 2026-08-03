import { ChangeDetectionStrategy, Component, inject, model, signal } from '@angular/core';
import { form, required, pattern, validate, FormField } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEye, lucideEyeOff, lucideLock } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
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

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
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
export class ChangePasswordDialog {
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
  private readonly formModel = signal({
    currentPassword: '',
    newPassword: '',
    confirmPassword: '',
  });

  //#if (IncludeLocalization)
  readonly changeForm = form(this.formModel, (path) => {
    required(path.currentPassword, {
      message: this.transloco.translate('common.validation.required'),
    });
    required(path.newPassword, {
      message: this.transloco.translate('common.validation.required'),
    });
    pattern(path.newPassword, PASSWORD_RULE, {
      message: this.transloco.translate('common.validation.passwordRule'),
    });
    validate(path.newPassword, (ctx) => {
      const newPassword = ctx.value();
      const currentPassword = ctx.valueOf(path.currentPassword);
      if (currentPassword && newPassword && currentPassword === newPassword) {
        return {
          kind: 'sameAsCurrent',
          message: this.transloco.translate('common.validation.passwordSameAsCurrent'),
        };
      }
      return null;
    });
    required(path.confirmPassword, {
      message: this.transloco.translate('common.validation.required'),
    });
    validate(path.confirmPassword, (ctx) => {
      const confirm = ctx.value();
      const newPassword = ctx.valueOf(path.newPassword);
      if (newPassword && confirm && newPassword !== confirm) {
        return {
          kind: 'passwordMismatch',
          message: this.transloco.translate('common.validation.passwordMismatch'),
        };
      }
      return null;
    });
  });
  //#else
  readonly changeForm = form(this.formModel, (path) => {
    required(path.currentPassword, { message: 'This field is required.' });
    required(path.newPassword, { message: 'This field is required.' });
    pattern(path.newPassword, PASSWORD_RULE, {
      message:
        'Password must be 8–20 characters and include uppercase, lowercase, digits, and special characters.',
    });
    validate(path.newPassword, (ctx) => {
      const newPassword = ctx.value();
      const currentPassword = ctx.valueOf(path.currentPassword);
      if (currentPassword && newPassword && currentPassword === newPassword) {
        return {
          kind: 'sameAsCurrent',
          message: 'The new password must differ from the current one.',
        };
      }
      return null;
    });
    required(path.confirmPassword, { message: 'This field is required.' });
    validate(path.confirmPassword, (ctx) => {
      const confirm = ctx.value();
      const newPassword = ctx.valueOf(path.newPassword);
      if (newPassword && confirm && newPassword !== confirm) {
        return { kind: 'passwordMismatch', message: 'The two passwords do not match.' };
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
    this.formModel.set({ currentPassword: '', newPassword: '', confirmPassword: '' });
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

    const { currentPassword, newPassword, confirmPassword } = this.formModel();

    this.accountService
      .changePassword({ currentPassword, newPassword, confirmPassword })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          //#if (IncludeLocalization)
          toast.success(this.transloco.translate('common.success'), {
            description: this.transloco.translate('account.changePassword.updateSuccess'),
          });
          //#else
          toast.success('Success', { description: 'Password updated' });
          //#endif
          this.visible.set(false);
        },
        error: (error) => {
          //#if (IncludeLocalization)
          toast.error(this.transloco.translate('common.requestError'), {
            description: applicationErrorMessage(error),
          });
          //#else
          toast.error('Request failed', { description: applicationErrorMessage(error) });
          //#endif
        },
      });
  }
}
