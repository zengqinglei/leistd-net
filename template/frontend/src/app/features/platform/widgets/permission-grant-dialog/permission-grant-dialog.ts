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
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucideChevronRight, lucideSearch } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import {
  HlmInputGroup,
  HlmInputGroupAddon,
  HlmInputGroupInput,
} from '@spartan-ng/helm/input-group';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSpinner } from '@spartan-ng/helm/spinner';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import {
  PermissionDefinitionGroupOutputDto,
  PermissionDefinitionOutputDto,
  PermissionGrantEffect,
  PermissionGrantInputDto,
  PermissionGrantState,
  ReplacePermissionGrantsInputDto,
} from '../../../../shared/models/permission';
import { PermissionManagementService } from '../../services/permission-management-service';

/** 授予主体类型。与后端 `PermissionGrantProviderNames` 一致。 */
export type PermissionGrantProvider = 'Role' | 'User';

/** 权限树展平后的一行，depth 用于缩进渲染。 */
interface PermissionRow {
  name: string;
  displayName: string;
  depth: number;
  parentName?: string;
}

interface PermissionGroupView {
  name: string;
  displayName: string;
  rows: PermissionRow[];
}

/** 渲染用分组：带过滤后的行与本组授予统计。 */
interface PermissionGroupRender extends PermissionGroupView {
  grantedCount: number;
  prohibitedCount: number;
}

/**
 * 权限授予编辑器，角色与用户共用。
 *
 * 三态：继承（未设置）/ 允许 / 拒绝。拒绝优先于任何来源的允许。
 * 选中子权限时自动补齐父级、把父级设为拒绝时清理其子孙——这与后端写入时的归一化一致，
 * 这里只是即时反馈，最终仍以后端归一化结果为准。
 * 保存一次请求完成，并携带版本号；版本冲突时提示重新加载而不是覆盖对方的修改。
 *
 * 用户主体额外呈现「继承自角色」一列：用户的三态编辑的是**例外**，
 * 不显示继承来源就无法判断某一项该设成拒绝还是留在继承。
 */
@Component({
  selector: 'app-permission-grant-dialog',
  imports: [
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmSpinner,
    HlmInputGroup,
    HlmInputGroupAddon,
    HlmInputGroupInput,
    ...HlmDialogImports,
    ...HlmRadioGroupImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [provideIcons({ lucideSearch, lucideChevronDown, lucideChevronRight })],
  templateUrl: './permission-grant-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PermissionGrantDialog {
  readonly open = model(false);
  readonly providerName = input<PermissionGrantProvider>('Role');
  readonly providerKey = input<string | null>(null);
  /** 主体展示名，仅用于标题。 */
  readonly subjectName = input('');
  readonly saved = output<void>();

  private readonly permissionService = inject(PermissionManagementService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);
  //#endif

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly groups = signal<PermissionGroupView[]>([]);
  readonly revision = signal(0);

  /** 权限名 -> 三态选择值（该主体的直接授予）。 */
  private readonly states = signal<Record<string, PermissionGrantState>>({});

  /** 权限名 -> 继承而来的效果。角色主体没有上游来源，恒为空。 */
  private readonly inheritedEffects = signal<Record<string, PermissionGrantEffect>>({});

  /** 父子关系缓存，用于自动补齐祖先与清理子孙。 */
  private ancestors: Record<string, string[]> = {};
  private descendants: Record<string, string[]> = {};

  /** 权限搜索关键字：权限多起来后没有搜索就只能靠肉眼扫，这里按显示名与权限名同时匹配。 */
  readonly keyword = signal('');

  /** 只有用户主体有上游来源，角色主体不渲染继承列。 */
  readonly showsInherited = computed(() => this.providerName() === 'User');

  readonly grantedCount = computed(
    () => Object.values(this.states()).filter((state) => state === 'Granted').length,
  );
  readonly prohibitedCount = computed(
    () => Object.values(this.states()).filter((state) => state === 'Prohibited').length,
  );

  /**
   * 过滤并统计后的分组。
   *
   * 只有一个组时不再套一层手风琴——那一层在单组场景下纯属多点一次；
   * 多组时才折叠，并在组标题上显示本组的授予数，便于在几十上百个权限里快速定位。
   */
  readonly visibleGroups = computed<PermissionGroupRender[]>(() => {
    const keyword = this.keyword().trim().toLowerCase();
    const states = this.states();

    return this.groups()
      .map((group) => {
        const rows = keyword
          ? group.rows.filter(
              (row) =>
                row.displayName.toLowerCase().includes(keyword) ||
                row.name.toLowerCase().includes(keyword),
            )
          : group.rows;

        return {
          ...group,
          rows,
          grantedCount: group.rows.filter((row) => states[row.name] === 'Granted').length,
          prohibitedCount: group.rows.filter((row) => states[row.name] === 'Prohibited').length,
        };
      })
      .filter((group) => group.rows.length > 0);
  });

  /** 单组时不折叠：避免为唯一的分区多加一次点击。 */
  readonly collapsible = computed(() => this.groups().length > 1);

  readonly hasNoMatch = computed(
    () => this.keyword().trim() !== '' && this.visibleGroups().length === 0,
  );

  onKeywordChange(value: string): void {
    this.keyword.set(value);
  }

  /** 折叠状态：默认全展开；搜索时强制展开，否则命中项会被折叠层藏住。 */
  private readonly collapsedGroups = signal<ReadonlySet<string>>(new Set<string>());

  isExpanded(groupName: string): boolean {
    return (
      !this.collapsible() || this.keyword().trim() !== '' || !this.collapsedGroups().has(groupName)
    );
  }

  toggleGroup(groupName: string): void {
    if (!this.collapsible()) {
      return;
    }

    const next = new Set(this.collapsedGroups());
    if (next.has(groupName)) {
      next.delete(groupName);
    } else {
      next.add(groupName);
    }
    this.collapsedGroups.set(next);
  }

  /** 整组置为允许：逐条走同一条归一化逻辑，父子规则不会被批量操作绕过。 */
  allowGroup(group: PermissionGroupRender): void {
    for (const row of group.rows) {
      this.onStateChange(row.name, 'Granted');
    }
  }

  /** 整组重置为继承。 */
  resetGroup(group: PermissionGroupRender): void {
    for (const row of [...group.rows].reverse()) {
      this.onStateChange(row.name, 'Inherit');
    }
  }

  constructor() {
    effect(() => {
      const providerKey = this.providerKey();
      if (!this.open() || !providerKey) {
        return;
      }

      this.loadFor(providerKey);
    });
  }

  stateOf(name: string): PermissionGrantState {
    return this.states()[name] ?? 'Inherit';
  }

  /** 该权限从角色继承到的效果；没有继承来源时返回 null。 */
  inheritedOf(name: string): PermissionGrantEffect | null {
    return this.inheritedEffects()[name] ?? null;
  }

  onStateChange(name: string, next: PermissionGrantState): void {
    const states = { ...this.states() };
    states[name] = next;

    if (next === 'Granted') {
      // 允许向上补齐：父权限是子权限的前置条件。
      for (const ancestor of this.ancestors[name] ?? []) {
        if (states[ancestor] !== 'Prohibited') {
          states[ancestor] = 'Granted';
        }
      }
    } else if (next === 'Prohibited') {
      // 拒绝向下传播：子孙回落为继承，运行时同样被拒绝。
      for (const descendant of this.descendants[name] ?? []) {
        states[descendant] = 'Inherit';
      }
    } else {
      // 取消父级时清理其子孙，避免出现"子有父无"的悬空授予。
      for (const descendant of this.descendants[name] ?? []) {
        states[descendant] = 'Inherit';
      }
    }

    this.states.set(states);
  }

  onSave(): void {
    const providerKey = this.providerKey();
    if (!providerKey) {
      return;
    }

    const grants: PermissionGrantInputDto[] = Object.entries(this.states())
      .filter(([, state]) => state !== 'Inherit')
      .map(([name, state]) => ({ name, effect: state as PermissionGrantInputDto['effect'] }));

    this.saving.set(true);
    this.replaceGrants(providerKey, { expectedRevision: this.revision(), grants }).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success(this.savedMessage());
        this.saved.emit();
      },
      error: (error) => {
        this.saving.set(false);
        // 409 说明另一位管理员抢先保存：提示重新加载，不静默覆盖。
        if (error?.status === 409) {
          toast.error(this.conflictMessage());
          this.loadFor(providerKey);
          return;
        }
        toast.error(applicationErrorMessage(error));
      },
    });
  }

  private replaceGrants(providerKey: string, data: ReplacePermissionGrantsInputDto) {
    return this.providerName() === 'User'
      ? this.permissionService.replaceUserGrants(providerKey, data)
      : this.permissionService.replaceRoleGrants(providerKey, data);
  }

  private loadFor(providerKey: string): void {
    this.loading.set(true);

    this.permissionService.getDefinitions().subscribe({
      next: (definitionGroups) => {
        this.buildTree(definitionGroups);

        const grants$ =
          this.providerName() === 'User'
            ? this.permissionService.getUserGrants(providerKey)
            : this.permissionService.getRoleGrants(providerKey);

        grants$.subscribe({
          next: (grants) => {
            this.revision.set(grants.revision);

            const states: Record<string, PermissionGrantState> = {};
            const inherited: Record<string, PermissionGrantEffect> = {};
            for (const grant of grants.grants) {
              states[grant.name] = grant.direct ?? 'Inherit';
              if (grant.inherited) {
                inherited[grant.name] = grant.inherited;
              }
            }
            this.states.set(states);
            this.inheritedEffects.set(inherited);
            this.loading.set(false);
          },
          error: (error) => {
            this.loading.set(false);
            toast.error(applicationErrorMessage(error));
          },
        });
      },
      error: (error) => {
        this.loading.set(false);
        toast.error(applicationErrorMessage(error));
      },
    });
  }

  private buildTree(definitionGroups: PermissionDefinitionGroupOutputDto[]): void {
    this.ancestors = {};
    this.descendants = {};

    const groups = definitionGroups.map((group) => {
      const rows: PermissionRow[] = [];
      for (const permission of group.permissions) {
        this.flatten(permission, 0, [], rows);
      }
      return { name: group.name, displayName: group.displayName, rows };
    });

    this.groups.set(groups);
  }

  private flatten(
    definition: PermissionDefinitionOutputDto,
    depth: number,
    ancestorChain: string[],
    rows: PermissionRow[],
  ): void {
    rows.push({
      name: definition.name,
      displayName: definition.displayName,
      depth,
      parentName: definition.parentName,
    });

    this.ancestors[definition.name] = [...ancestorChain];
    this.descendants[definition.name] = [];
    for (const ancestor of ancestorChain) {
      this.descendants[ancestor].push(definition.name);
    }

    for (const child of definition.children) {
      this.flatten(child, depth + 1, [definition.name, ...ancestorChain], rows);
    }
  }

  //#if (IncludeLocalization)
  readonly title = computed(() => {
    this.translationReady();
    return this.transloco.translate('permissions.dialogTitle', { name: this.subjectName() });
  });
  readonly description = () =>
    this.transloco.translate(
      this.showsInherited() ? 'permissions.descriptionForUser' : 'permissions.descriptionForRole',
    );
  readonly searchPlaceholder = () => this.transloco.translate('permissions.searchPlaceholder');
  readonly noMatchLabel = () => this.transloco.translate('common.noResults');
  readonly allowAllLabel = () => this.transloco.translate('permissions.allowAll');
  readonly resetAllLabel = () => this.transloco.translate('permissions.resetAll');
  readonly stateInheritLabel = () => this.transloco.translate('permissions.stateInherit');
  readonly stateGrantedLabel = () => this.transloco.translate('permissions.stateGranted');
  readonly stateProhibitedLabel = () => this.transloco.translate('permissions.stateProhibited');
  readonly cancelLabel = () => this.transloco.translate('common.cancel');
  readonly saveLabel = () => this.transloco.translate('common.save');
  readonly grantedLabel = (count: number) =>
    this.transloco.translate('permissions.grantedCount', { count });
  readonly prohibitedLabel = (count: number) =>
    this.transloco.translate('permissions.prohibitedCount', { count });
  readonly groupSummary = (granted: number, total: number) =>
    this.transloco.translate('permissions.groupSummary', { granted, total });
  readonly inheritedLabel = (effect: PermissionGrantEffect) =>
    this.transloco.translate(
      effect === 'Granted' ? 'permissions.inheritedGranted' : 'permissions.inheritedProhibited',
    );
  private savedMessage = () => this.transloco.translate('permissions.saved');
  private conflictMessage = () => this.transloco.translate('permissions.conflict');
  //#else
  readonly title = computed(() => `Permissions · ${this.subjectName()}`);
  readonly description = () =>
    this.showsInherited()
      ? 'Inherit follows the roles assigned to this user. Deny always wins over any grant, including grants inherited from roles.'
      : 'Inherit leaves the permission unset. Deny always wins over any grant, including grants that come from other sources.';
  readonly searchPlaceholder = () => 'Search permissions';
  readonly noMatchLabel = () => 'No results';
  readonly allowAllLabel = () => 'Allow all';
  readonly resetAllLabel = () => 'Reset';
  readonly stateInheritLabel = () => 'Inherit';
  readonly stateGrantedLabel = () => 'Allow';
  readonly stateProhibitedLabel = () => 'Deny';
  readonly cancelLabel = () => 'Cancel';
  readonly saveLabel = () => 'Save';
  readonly grantedLabel = (count: number) => `${count} allowed`;
  readonly prohibitedLabel = (count: number) => `${count} denied`;
  readonly groupSummary = (granted: number, total: number) => `${granted}/${total}`;
  readonly inheritedLabel = (effect: PermissionGrantEffect) =>
    effect === 'Granted' ? 'Allowed by role' : 'Denied by role';
  private savedMessage = () => 'Permissions saved';
  private conflictMessage = () =>
    'Someone else changed these permissions. The latest values have been reloaded.';
  //#endif
}
