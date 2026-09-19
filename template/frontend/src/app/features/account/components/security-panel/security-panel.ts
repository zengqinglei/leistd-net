import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
import { form, required, pattern, validate, FormField } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEye, lucideEyeOff, lucideLock } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import {
  HlmInputGroup,
  HlmInputGroupInput,
  HlmInputGroupButton,
} from '@spartan-ng/helm/input-group';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { finalize } from 'rxjs/operators';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { PASSWORD_RULE } from '../../../../core/validation/password-rule';
import { AccountService } from '../../services/account-service';
//#if (ExternalLogin)
import { ExternalLogins } from '../external-logins/external-logins';
//#endif
import { LoginDevices } from '../login-devices/login-devices';
import { TwoFactorSettings } from '../two-factor-settings/two-factor-settings';

/**
 * 个人设置 ·「账户与安全」面板。
 *
 * 每节一个卡片：修改密码、两步验证、已绑定的外部账号（启用外部登录时）、登录设备。
 */
@Component({
  selector: 'app-security-panel',
  standalone: true,
  imports: [
    FormField,
    NgIcon,
    LoginDevices,
    TwoFactorSettings,
    //#if (ExternalLogin)
    ExternalLogins,
    //#endif
    HlmButton,
    HlmSpinner,
    HlmInputGroup,
    HlmInputGroupInput,
    HlmInputGroupButton,
    ...HlmFieldImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [provideIcons({ lucideEye, lucideEyeOff, lucideLock })],
  templateUrl: './security-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SecurityPanel {
  private readonly accountService = inject(AccountService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#else
  //#endif

  protected readonly loginDevices = viewChild(LoginDevices);

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
        'Password must be at least 12 characters (up to 256). A longer passphrase is stronger than a short complex one.',
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

  /**
   * 清空表单并收起明文显示。
   *
   * 改密成功后必须清：面板不像弹窗那样一关就销毁，旧密码与新密码会一直留在页面上。
   */
  private resetForm(): void {
    this.formModel.set({ currentPassword: '', newPassword: '', confirmPassword: '' });
    this.changeForm().reset();
    this.showCurrentPassword.set(false);
    this.showNewPassword.set(false);
    this.showConfirmPassword.set(false);
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
          toast.success('Success', {
            description: 'Password updated. Other devices have been signed out.',
          });
          //#endif
          this.resetForm();
          // 服务端已让其他设备退出，列表跟着刷新
          this.loginDevices()?.reload();
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
