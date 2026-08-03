import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  output,
  signal,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideListFilter } from '@ng-icons/lucide';
import { BrnCommandImports } from '@spartan-ng/brain/command';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmCommandImports } from '@spartan-ng/helm/command';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmSeparatorImports } from '@spartan-ng/helm/separator';

/** faceted filter 选项。 */
export interface FacetedFilterOption {
  value: string | boolean | null;
  label: string;
}

/**
 * 分面筛选器（对齐参考站）：虚线描边按钮 + 命令面板（可搜索 + 清除）。
 * - 单选（默认）：命中项打勾、选中即关闭；用 `value` / `valueChange`。
 * - 多选（`multiple`）：勾选框 + 选中项各显一枚 Badge、连续勾选不关闭；用 `values` / `valuesChange`。
 * 所有文案由父级以已翻译字符串传入，组件本身与 i18n 无关。
 */
@Component({
  selector: 'app-faceted-filter',
  standalone: true,
  imports: [
    NgIcon,
    HlmBadge,
    HlmButton,
    ...HlmPopoverImports,
    ...HlmSeparatorImports,
    ...HlmCheckboxImports,
    BrnCommandImports,
    ...HlmCommandImports,
  ],
  providers: [provideIcons({ lucideListFilter, lucideCheck })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-popover sideOffset="5" align="start" [state]="state()" (stateChanged)="state.set($event)">
      <button type="button" hlmBtn hlmPopoverTrigger variant="outline" class="border-dashed">
        <ng-icon name="lucideListFilter" />
        {{ label() }}
        @if (multiple()) {
          @if (selectedOptions().length) {
            <hlm-separator class="mx-2" orientation="vertical" />
            <div class="flex gap-1">
              @for (opt of selectedOptions(); track opt.label) {
                <span hlmBadge>{{ opt.label }}</span>
              }
            </div>
          }
        } @else if (selectedOption(); as sel) {
          <hlm-separator class="mx-2" orientation="vertical" />
          <span hlmBadge>{{ sel.label }}</span>
        }
      </button>
      <hlm-command *hlmPopoverPortal hlmPopoverContent class="w-50 p-0">
        <hlm-command-input [placeholder]="searchPlaceholder() || label()" />
        <div *brnCommandEmpty hlmCommandEmpty>{{ emptyLabel() }}</div>
        <hlm-command-list>
          <hlm-command-group>
            @for (opt of options(); track opt.label) {
              <button type="button" hlm-command-item [value]="opt.label" (selected)="onSelect(opt)">
                @if (multiple()) {
                  <hlm-checkbox class="mr-2" [checked]="isChecked(opt)" />
                } @else {
                  <ng-icon name="lucideCheck" [class.opacity-0]="opt.value !== value()" />
                }
                <span>{{ opt.label }}</span>
              </button>
            }
            @if (hasSelection()) {
              <hlm-command-separator />
              <button
                type="button"
                hlm-command-item
                [value]="clearLabel()"
                class="mt-1 flex justify-center"
                (selected)="clear()"
              >
                {{ clearLabel() }}
              </button>
            }
          </hlm-command-group>
        </hlm-command-list>
      </hlm-command>
    </hlm-popover>
  `,
})
export class FacetedFilter {
  readonly label = input.required<string>();
  readonly options = input.required<FacetedFilterOption[]>();
  // 单选值。
  readonly value = input<string | boolean | null>(null);
  // 多选开关及其取值（仅字符串值场景，如角色）。
  readonly multiple = input(false, { transform: booleanAttribute });
  readonly values = input<string[]>([]);
  readonly searchPlaceholder = input<string>('');
  readonly clearLabel = input<string>('Clear filter');
  readonly emptyLabel = input<string>('No results');

  readonly valueChange = output<string | boolean | null>();
  readonly valuesChange = output<string[]>();

  readonly state = signal<'open' | 'closed'>('closed');

  readonly selectedOption = computed(
    () => this.options().find((o) => o.value === this.value()) ?? null,
  );
  readonly selectedOptions = computed(() =>
    this.options().filter((o) => this.values().includes(String(o.value))),
  );
  readonly hasSelection = computed(() =>
    this.multiple() ? this.selectedOptions().length > 0 : this.selectedOption() !== null,
  );

  isChecked(option: FacetedFilterOption): boolean {
    return this.values().includes(String(option.value));
  }

  onSelect(option: FacetedFilterOption): void {
    if (this.multiple()) {
      const key = String(option.value);
      const current = this.values();
      const next = current.includes(key) ? current.filter((v) => v !== key) : [...current, key];
      this.valuesChange.emit(next);
      // 多选：保持面板打开，便于连续勾选。
      return;
    }
    this.valueChange.emit(option.value);
    this.state.set('closed');
  }

  clear(): void {
    if (this.multiple()) {
      this.valuesChange.emit([]);
    } else {
      this.valueChange.emit(null);
    }
    this.state.set('closed');
  }
}
