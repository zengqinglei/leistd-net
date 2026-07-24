//#if (IncludeLocalization)
import { ChangeDetectionStrategy, Component, inject, model, output, signal } from '@angular/core';
//#else
import { ChangeDetectionStrategy, Component, model, output, signal } from '@angular/core';
//#endif
import { form, required, pattern, FormField } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEye, lucideEyeOff } from '@ng-icons/lucide';
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

import { ResetUserPasswordInputDto } from '../../../../models/user-management.dto';

const PASSWORD_RULE = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{8,20}$/;

@Component({
  selector: 'app-reset-user-password-dialog',
  imports: [
    FormField,
    NgIcon,
    HlmButton,
    HlmInputGroup,
    HlmInputGroupInput,
    HlmInputGroupButton,
    HlmSpinner,
    ...HlmDialogImports,
    ...HlmFieldImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [provideIcons({ lucideEye, lucideEyeOff })],
  templateUrl: './reset-user-password-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResetUserPasswordDialogComponent {
  visible = model(false);
  saving = model(false);
  readonly saved = output<ResetUserPasswordInputDto>();

  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  readonly dialogHeader = () => this.transloco.translate('users.resetDialog.header');
  //#else
  readonly dialogHeader = () => 'Reset user password';
  //#endif

  // 密码可见性
  protected readonly showPassword = signal(false);

  // 表单模型（Signal Forms）
  private readonly model_ = signal({ password: '' });

  //#if (IncludeLocalization)
  readonly resetForm = form(this.model_, (path) => {
    required(path.password, {
      message: this.transloco.translate('users.resetDialog.newPasswordRequired'),
    });
    pattern(path.password, PASSWORD_RULE, {
      message: this.transloco.translate('users.resetDialog.newPasswordRule'),
    });
  });
  //#else
  readonly resetForm = form(this.model_, (path) => {
    required(path.password, { message: 'Enter a new password' });
    pattern(path.password, PASSWORD_RULE, {
      message:
        'The new password does not meet the rules: it must include uppercase and lowercase letters, digits and special characters',
    });
  });
  //#endif

  /** 桥接 hlm-dialog 声明式 state 到对外 visible 契约；关闭时重置表单。 */
  onDialogStateChange(state: BrnDialogState): void {
    const open = state === 'open';
    this.visible.set(open);
    if (!open) {
      this.model_.set({ password: '' });
      this.showPassword.set(false);
    }
  }

  onHide(): void {
    this.visible.set(false);
  }

  save(): void {
    if (this.resetForm().invalid()) {
      this.resetForm().markAsTouched();
      return;
    }
    this.saved.emit({ password: this.model_().password });
  }
}
