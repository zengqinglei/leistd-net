import {
  viewChild,
  ElementRef,
  computed,
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { form } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { firstValueFrom } from 'rxjs';

import { DialogClose } from '../../../../../../core/components/dialog-close/dialog-close';
import { injectFormSubmission } from '../../../../../../core/validation/form-submission';
import { ImeGuard } from '../../../../../../shared/directives/ime-guard';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import { RoleBriefDto } from '../../../../dtos/role.dto';
import { UserManagementOutputDto } from '../../../../dtos/user-management.dto';
import { UserManagementService } from '../../../../services/user-management-service';

/** 用户角色分配对话框：与资料编辑分离，只调用 `PUT /api/v1/users/{id}/roles`（要求 `App.Users.ManageRoles`）。 */
@Component({
  selector: 'app-user-roles-dialog',
  // prettier-ignore
  imports: [ImeGuard, DialogClose,
    HlmButton,
    HlmSpinner,
    ...HlmDialogImports,
    ...HlmCheckboxImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  templateUrl: './user-roles-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserRolesDialog {
  private readonly selectionFormRoot =
    viewChild.required<ElementRef<HTMLElement>>('selectionFormRoot');
  readonly open = model(false);
  readonly user = input<UserManagementOutputDto | null>(null);
  readonly rolesLoading = input(false);
  readonly rolesError = input<string | null>(null);
  readonly reloadRoles = output<void>();
  readonly availableRoles = input<RoleBriefDto[]>([]);
  readonly saved = output<void>();

  private readonly userService = inject(UserManagementService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif

  private readonly submitForm = injectFormSubmission();
  private readonly submitModel = signal({});
  readonly selectionForm = form(this.submitModel);
  readonly saving = computed(() => this.selectionForm().submitting());
  readonly selectionDirty = computed(
    () =>
      [...this.selected()].sort().join(',') !==
      (this.user()?.roles ?? [])
        .map((role) => role.id)
        .sort()
        .join(','),
  );
  private readonly selected = signal<ReadonlySet<string>>(new Set<string>());

  constructor() {
    effect(() => {
      const user = this.user();
      if (!this.open() || !user) {
        return;
      }

      this.selected.set(new Set((user.roles ?? []).map((role) => role.id)));
    });
  }

  isSelected(roleId: string): boolean {
    return this.selected().has(roleId);
  }

  toggle(roleId: string): void {
    const next = new Set(this.selected());
    if (next.has(roleId)) {
      next.delete(roleId);
    } else {
      next.add(roleId);
    }
    this.selected.set(next);
  }

  async onSave(): Promise<void> {
    if (this.rolesLoading() || this.rolesError()) return;
    const user = this.user();
    if (!user) {
      return;
    }

    await this.submitForm(this.selectionForm, this.selectionFormRoot().nativeElement, async () => {
      await firstValueFrom(
        this.userService.replaceUserRoles(user.id, { roleIds: [...this.selected()] }),
      );

      //#if (IncludeLocalization)
      toast.success(this.transloco.translate('users.rolesDialog.saved'));
      //#else
      toast.success('Roles updated');
      //#endif
      this.saved.emit();
    });
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'app.startup.loading': 'Loading...',
  'common.retry': 'Retry',
  'users.rolesDialog.title': 'Roles · {{name}}',
  'users.rolesDialog.description':
    'The user inherits every permission granted to the selected roles.',
  'common.noData': 'No data',
  'common.cancel': 'Cancel',
  'common.save': 'Save',
};
//#endif
