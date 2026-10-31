import {
  viewChild,
  ElementRef,
  computed,
  effect,
  input,
  ChangeDetectionStrategy,
  Component,
  model,
  signal,
} from '@angular/core';
import { form, maxLength, minLength, required, FormField } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
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
import { firstValueFrom, Observable } from 'rxjs';

import { DialogClose } from '../../../../../../core/components/dialog-close/dialog-close';
import { PasswordLengthMeter } from '../../../../../../core/components/password-length-meter/password-length-meter';
import { injectFormSubmission } from '../../../../../../core/validation/form-submission';
import {
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
} from '../../../../../../core/validation/password-rule';
import { ImeGuard } from '../../../../../../shared/directives/ime-guard';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import { ResetUserPasswordInputDto } from '../../../../dtos/user-management.dto';

@Component({
  selector: 'app-reset-user-password-dialog',
  imports: [
    ImeGuard,
    DialogClose,
    FormField,
    NgIcon,
    HlmButton,
    HlmInputGroup,
    HlmInputGroupInput,
    HlmInputGroupButton,
    HlmSpinner,
    PasswordLengthMeter,
    ...HlmDialogImports,
    ...HlmFieldImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideEye, lucideEyeOff })],
  templateUrl: './reset-user-password-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResetUserPasswordDialog {
  private readonly resetFormRoot = viewChild.required<ElementRef<HTMLElement>>('resetFormRoot');
  private readonly submitForm = injectFormSubmission();
  readonly saving = computed(() => this.resetForm().submitting());
  readonly visible = model(false);
  readonly saveAction = input.required<(dto: ResetUserPasswordInputDto) => Observable<unknown>>();
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif

  protected readonly showPassword = signal(false);

  private readonly formModel = signal({ password: '' });

  readonly resetForm = form(this.formModel, (path) => {
    required(path.password);
    minLength(path.password, PASSWORD_MIN_LENGTH);
    maxLength(path.password, PASSWORD_MAX_LENGTH);
  });

  constructor() {
    effect(() => {
      if (this.visible()) {
        this.resetForm().reset();
        this.formModel.set({ password: '' });
      }
    });
  }

  /** 桥接 hlm-dialog 声明式 state 到对外 visible 契约；关闭时重置表单。 */
  onDialogStateChange(state: BrnDialogState): void {
    const open = state === 'open';
    this.visible.set(open);
    if (!open) {
      this.resetForm().reset();
      this.formModel.set({ password: '' });
      this.showPassword.set(false);
    }
  }

  onHide(): void {
    this.visible.set(false);
  }

  async save(): Promise<void> {
    await this.submitForm(this.resetForm, this.resetFormRoot().nativeElement, async () => {
      await firstValueFrom(this.saveAction()({ password: this.formModel().password }), {
        defaultValue: undefined,
      });
    });
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'users.resetDialog.header': 'Reset user password',
  'users.resetDialog.warning': 'After the reset, the user must sign in with the new password.',
  'users.resetDialog.newPasswordLabel': 'New password',
  'users.resetDialog.newPasswordPlaceholder': 'Enter a new password',
  'common.hidePassword': 'Hide password',
  'common.showPassword': 'Show password',
  'common.cancel': 'Cancel',
  'users.resetDialog.confirmButton': 'Confirm reset',
};
//#endif
