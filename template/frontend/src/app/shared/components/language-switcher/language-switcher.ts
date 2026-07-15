import { ChangeDetectionStrategy, Component, computed, inject, ViewChild } from '@angular/core';
import { TranslocoModule } from '@jsverse/transloco';
import { MenuItem } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { Menu, MenuModule } from 'primeng/menu';

import { LanguageService } from '../../../core/services/language-service';

interface LangMenuItem extends MenuItem {
  active: boolean;
}

/**
 * 语言切换器：与主题切换一致的圆形图标按钮，点击弹出菜单（复用 PrimeNG p-menu popup，
 * 与顶栏用户菜单同款、无小三角），当前语言项高亮。
 *
 * 复用于各布局右上角操作区（default-header、landing、login、register）。
 */
@Component({
  selector: 'app-language-switcher',
  standalone: true,
  imports: [ButtonModule, MenuModule, TranslocoModule],
  // host 用 inline-flex 使其成为与相邻主题/铃铛按钮尺寸一致的单个 flex 子项；
  // p-menu(popup) appendTo=body，其占位元素不影响此盒尺寸，故间距、对齐与其他图标完全一致。
  host: { class: 'inline-flex items-center' },
  template: `
    <p-button
      icon="pi pi-globe"
      [text]="true"
      [rounded]="true"
      severity="secondary"
      [attr.aria-label]="'language.label' | transloco"
      (onClick)="menu.toggle($event)"
    >
    </p-button>

    <p-menu #menu [model]="items()" [popup]="true" appendTo="body" styleClass="min-w-36">
      <ng-template #item let-item>
        <div
          class="flex items-center gap-2 px-3 py-2 mx-1 my-0.5 rounded-md text-sm cursor-pointer transition-colors"
          [class]="
            item.active ? 'bg-primary text-primary-contrast font-medium' : 'text-color hover:bg-surface-100 dark:hover:bg-surface-800'
          "
        >
          <span class="w-1.5 h-1.5 rounded-full flex-none" [class]="item.active ? 'bg-primary-contrast' : 'bg-transparent'"></span>
          <span>{{ item.label }}</span>
        </div>
      </ng-template>
    </p-menu>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LanguageSwitcher {
  private readonly languageService = inject(LanguageService);

  @ViewChild('menu') menu!: Menu;

  readonly items = computed<LangMenuItem[]>(() => {
    const active = this.languageService.activeLang();
    return this.languageService.options.map(option => ({
      label: option.label,
      active: option.id === active,
      command: () => this.languageService.setActiveLang(option.id)
    }));
  });
}
