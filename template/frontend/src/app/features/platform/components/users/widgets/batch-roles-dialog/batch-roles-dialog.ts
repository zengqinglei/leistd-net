import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { form } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { firstValueFrom, Observable } from 'rxjs';

import { DialogClose } from '../../../../../../core/components/dialog-close/dialog-close';
import { injectFormSubmission } from '../../../../../../core/validation/form-submission';
import { ImeGuard } from '../../../../../../shared/directives/ime-guard';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import { RoleBriefDto } from '../../../../dtos/role.dto';

/**
 * 给选中的一批用户追加角色：只负责选角色，提交由用户页发起（逐条结果在那里汇总）。只追加不覆盖，
 * 用户已有的角色保持不变。
 */
@Component({
  selector: 'app-batch-roles-dialog',
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
  templateUrl: './batch-roles-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BatchRolesDialog {
  readonly open = model(false);
  readonly userCount = input(0);
  readonly rolesLoading = input(false);
  readonly rolesError = input<string | null>(null);
  readonly reloadRoles = output<void>();
  readonly availableRoles = input<RoleBriefDto[]>([]);
  readonly saveAction = input.required<(ids: string[]) => Observable<unknown>>();
  private readonly submitModel = signal({});
  readonly selectionForm = form(this.submitModel);
  readonly saving = computed(() => this.selectionForm().submitting());
  private readonly submitForm = injectFormSubmission();
  async onSave(): Promise<void> {
    if (this.rolesLoading() || this.rolesError()) return;
    if (!this.selectedIds().length) return;
    await this.submitForm(this.selectionForm, 'batch-roles-form', async () => {
      await firstValueFrom(this.saveAction()(this.selectedIds()));
    });
  }
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif

  private readonly selected = signal<ReadonlySet<string>>(new Set<string>());
  readonly selectedIds = computed(() => [...this.selected()]);

  constructor() {
    // 每次打开都从空白开始：上一批选过的角色不该悄悄带到下一批
    effect(() => {
      if (this.open()) {
        this.selected.set(new Set<string>());
      }
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
}
//#if (!IncludeLocalization)

const ENGLISH: Record<string, string> = {
  'common.retry': 'Retry',
  'users.batch.selectRolesHint': 'Select at least one role to add.',
  'users.batch.rolesTitle': 'Add roles to {{count}} users',
  'users.batch.rolesDescription':
    'The selected roles are added. Roles the users already have are kept.',
  'users.batch.assign': 'Add roles',
  'common.noData': 'No data',
  'common.cancel': 'Cancel',
};
//#endif
