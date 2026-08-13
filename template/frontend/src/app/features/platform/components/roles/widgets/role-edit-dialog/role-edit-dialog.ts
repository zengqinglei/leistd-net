// prettier-ignore
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  //#if (IncludeLocalization)
  inject,
  //#endif
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { FormField, disabled, form, required } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';

//#if (IncludeLocalization)
import { translationReady } from '../../../../../../core/i18n/translation-ready';
//#endif
import { CreateRoleInputDto, RoleOutputDto, UpdateRoleInputDto } from '../../../../models/role.dto';

interface RoleEditFormModel {
  name: string;
  displayName: string;
  description: string;
  sort: number;
  isDefault: boolean;
}

/**
 * 角色新建 / 编辑对话框。
 *
 * 角色名称是稳定的业务标识，创建后不可修改——用户赋权按 Id 提交，名称只用于展示与筛选。
 */
@Component({
  selector: 'app-role-edit-dialog',
  imports: [
    FormField,
    HlmButton,
    HlmInput,
    ...HlmDialogImports,
    ...HlmFieldImports,
    ...HlmCheckboxImports,
  ],
  templateUrl: './role-edit-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoleEditDialog {
  readonly open = model(false);
  readonly role = input<RoleOutputDto | null>(null);
  readonly save = output<CreateRoleInputDto | UpdateRoleInputDto>();
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);
  //#endif

  readonly isEdit = computed(() => this.role() !== null);

  protected readonly formModel = signal<RoleEditFormModel>({
    name: '',
    displayName: '',
    description: '',
    sort: 0,
    isDefault: false,
  });

  readonly roleForm = form(this.formModel, (path) => {
    // 角色名是稳定业务标识，创建后不可修改。
    disabled(path.name, () => this.isEdit());
    //#if (IncludeLocalization)
    required(path.name, { message: this.transloco.translate('common.validation.required') });
    required(path.displayName, { message: this.transloco.translate('common.validation.required') });
    //#else
    required(path.name, { message: 'This field is required.' });
    required(path.displayName, { message: 'This field is required.' });
    //#endif
  });

  constructor() {
    effect(() => {
      // 打开时按当前角色重置表单：新建走空白模型，编辑回填既有值。
      if (!this.open()) {
        return;
      }

      const role = this.role();
      this.formModel.set({
        name: role?.name ?? '',
        displayName: role?.displayName ?? '',
        description: role?.description ?? '',
        sort: role?.sort ?? 0,
        isDefault: role?.isDefault ?? false,
      });
    });
  }

  onSubmit(): void {
    if (this.roleForm().invalid()) {
      return;
    }

    const model = this.formModel();
    const description = model.description.trim() || undefined;

    if (this.isEdit()) {
      this.save.emit({
        displayName: model.displayName.trim(),
        description,
        sort: model.sort,
        isDefault: model.isDefault,
      } satisfies UpdateRoleInputDto);
      return;
    }

    this.save.emit({
      name: model.name.trim(),
      displayName: model.displayName.trim(),
      description,
      sort: model.sort,
      isDefault: model.isDefault,
    } satisfies CreateRoleInputDto);
  }

  //#if (IncludeLocalization)
  readonly title = computed(() => {
    this.translationReady();
    return this.transloco.translate(this.isEdit() ? 'roles.editTitle' : 'roles.createTitle');
  });

  readonly description = () => this.transloco.translate('roles.editDescription');
  readonly cancelLabel = () => this.transloco.translate('common.cancel');
  readonly saveLabel = () => this.transloco.translate('common.save');

  fieldLabel(field: 'name' | 'displayName' | 'description' | 'isDefault'): string {
    const keys = {
      name: 'roles.fieldName',
      displayName: 'roles.fieldDisplayName',
      description: 'roles.fieldDescription',
      isDefault: 'roles.fieldIsDefault',
    } as const;
    return this.transloco.translate(keys[field]);
  }
  //#else
  fieldLabel(field: 'name' | 'displayName' | 'description' | 'isDefault'): string {
    const labels = {
      name: 'Name',
      displayName: 'Display name',
      description: 'Description',
      isDefault: 'Assign to new users by default',
    } as const;
    return labels[field];
  }

  readonly title = computed(() => (this.isEdit() ? 'Edit role' : 'New role'));

  readonly description = () =>
    'Name is the stable identifier and cannot be changed after creation.';
  readonly cancelLabel = () => 'Cancel';
  readonly saveLabel = () => 'Save';
  //#endif
}
