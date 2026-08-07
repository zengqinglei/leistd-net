import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmSpinner } from '@spartan-ng/helm/spinner';

import { applicationErrorMessage } from '../../../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { translationReady } from '../../../../../../core/i18n/translation-ready';
//#endif
import { RoleBriefDto } from '../../../../models/role.dto';
import { UserManagementOutputDto } from '../../../../models/user-management.dto';
import { UserManagementService } from '../../../../services/user-management-service';

/**
 * 用户角色分配对话框。
 *
 * 与用户资料编辑分离：本对话框只调用 `PUT /api/v1/users/{id}/roles`，
 * 该端点要求 `App.Users.ManageRoles`。持有资料编辑权限但没有角色分配权限的主体
 * 看不到入口，即便直接调用接口也会被服务端拒绝。
 */
@Component({
  selector: 'app-user-roles-dialog',
  imports: [HlmButton, HlmSpinner, ...HlmDialogImports, ...HlmCheckboxImports],
  templateUrl: './user-roles-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserRolesDialog {
  readonly open = model(false);
  readonly user = input<UserManagementOutputDto | null>(null);
  readonly availableRoles = input<RoleBriefDto[]>([]);
  readonly saved = output<void>();

  private readonly userService = inject(UserManagementService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);
  //#endif

  readonly saving = signal(false);
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

  onSave(): void {
    const user = this.user();
    if (!user) {
      return;
    }

    this.saving.set(true);
    this.userService.replaceUserRoles(user.id, { roleIds: [...this.selected()] }).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success(this.savedMessage());
        this.saved.emit();
      },
      error: (error) => {
        this.saving.set(false);
        toast.error(applicationErrorMessage(error));
      },
    });
  }

  //#if (IncludeLocalization)
  readonly title = computed(() => {
    this.translationReady();
    return this.transloco.translate('users.rolesDialog.title', {
      name: this.user()?.username ?? '',
    });
  });
  readonly description = () => this.transloco.translate('users.rolesDialog.description');
  readonly emptyLabel = () => this.transloco.translate('common.noData');
  readonly cancelLabel = () => this.transloco.translate('common.cancel');
  readonly saveLabel = () => this.transloco.translate('common.save');
  private savedMessage = () => this.transloco.translate('users.rolesDialog.saved');
  //#else
  readonly title = computed(() => `Roles · ${this.user()?.username ?? ''}`);

  readonly description = () => 'The user inherits every permission granted to the selected roles.';
  readonly emptyLabel = () => 'No roles available';
  readonly cancelLabel = () => 'Cancel';
  readonly saveLabel = () => 'Save';
  private savedMessage = () => 'Roles updated';
  //#endif
}
