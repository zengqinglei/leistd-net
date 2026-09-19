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
import {
  FormField,
  disabled,
  form,
  maxLength,
  minLength,
  pattern,
  required,
} from '@angular/forms/signals';
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

/** 与服务端 CreateRoleInputDto.Name 的 [RegularExpression] 一致。 */
const ROLE_NAME_PATTERN = /^[a-zA-Z0-9_]+$/;

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
    // 与 CreateRoleInputDto 的规则逐条对应（长度 2~64、字母数字下划线；显示名 128；描述 512）。
    // 只校验"必填"时，格式不对要等提交后由服务端拒绝，用户才第一次知道规则。
    //#if (IncludeLocalization)
    required(path.name, { message: this.transloco.translate('common.validation.required') });
    minLength(path.name, 2, {
      message: this.transloco.translate('common.validation.roleNamePattern'),
    });
    maxLength(path.name, 64, {
      message: this.transloco.translate('common.validation.roleNamePattern'),
    });
    pattern(path.name, ROLE_NAME_PATTERN, {
      message: this.transloco.translate('common.validation.roleNamePattern'),
    });
    required(path.displayName, { message: this.transloco.translate('common.validation.required') });
    maxLength(path.displayName, 128, {
      message: this.transloco.translate('common.validation.maxLength', { max: 128 }),
    });
    maxLength(path.description, 512, {
      message: this.transloco.translate('common.validation.maxLength', { max: 512 }),
    });
    //#else
    required(path.name, { message: 'This field is required.' });
    minLength(path.name, 2, { message: 'Must be 2–64 letters, digits, or underscores.' });
    maxLength(path.name, 64, { message: 'Must be 2–64 letters, digits, or underscores.' });
    pattern(path.name, ROLE_NAME_PATTERN, {
      message: 'Must be 2–64 letters, digits, or underscores.',
    });
    required(path.displayName, { message: 'This field is required.' });
    maxLength(path.displayName, 128, { message: 'Must not exceed 128 characters.' });
    maxLength(path.description, 512, { message: 'Must not exceed 512 characters.' });
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
  //#else
  readonly title = computed(() => (this.isEdit() ? 'Edit role' : 'New role'));

  readonly description = () =>
    'Name is the stable identifier and cannot be changed after creation.';
  readonly cancelLabel = () => 'Cancel';
  readonly saveLabel = () => 'Save';
  //#endif

  // 条件收在方法体内而不是写两个同名方法：模板源码本身也要能通过 lint，
  // 两份声明会触发 adjacent-overload-signatures——生成产物没事，坏的是贡献者的本地反馈。
  fieldLabel(field: 'name' | 'displayName' | 'description' | 'isDefault'): string {
    //#if (IncludeLocalization)
    const keys = {
      name: 'roles.fieldName',
      displayName: 'roles.fieldDisplayName',
      description: 'roles.fieldDescription',
      isDefault: 'roles.fieldIsDefault',
    } as const;
    return this.transloco.translate(keys[field]);
    //#else
    const labels = {
      name: 'Name',
      displayName: 'Display name',
      description: 'Description',
      isDefault: 'Assign to new users by default',
    } as const;
    return labels[field];
    //#endif
  }
}
