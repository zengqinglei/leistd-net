import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideGlobe } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';

import { applicationErrorMessage } from '../../../core/errors/application-http-error';
import { AuthService } from '../../../core/services/auth-service';
import { Lang, LanguageService } from '../../../core/services/language-service';
import { SettingService } from '../../../core/settings/setting-service';
import { SETTINGS } from '../../../core/settings/setting.constants';

interface LangOption {
  id: Lang;
  label: string;
  active: boolean;
}

/**
 * 语言切换器：圆形图标按钮，点击弹出下拉菜单（Spartan dropdown-menu，模板驱动），
 * 当前语言项高亮并显示勾选。
 *
 * 复用于各布局右上角操作区（default-header、landing、login、register）。
 */
@Component({
  selector: 'app-language-switcher',
  standalone: true,
  imports: [HlmButton, NgIcon, TranslocoModule, ...HlmDropdownMenuImports],
  providers: [provideIcons({ lucideGlobe, lucideCheck })],
  host: { class: 'inline-flex items-center' },
  template: `
    <button
      hlmBtn
      variant="outline"
      size="icon"
      class="w-auto gap-1.5 px-2.5"
      [attr.aria-label]="'language.label' | transloco"
      [hlmDropdownMenuTrigger]="langMenu"
      align="end"
    >
      <ng-icon name="lucideGlobe" />
      {{ shortLabel() }}
    </button>

    <ng-template #langMenu>
      <hlm-dropdown-menu sideOffset="2" class="min-w-36">
        @for (option of items(); track option.id) {
          <button hlmDropdownMenuItem (click)="select(option.id)">
            <ng-icon
              name="lucideCheck"
              data-icon="inline-start"
              [class.invisible]="!option.active"
            />
            <span>{{ option.label }}</span>
          </button>
        }
      </hlm-dropdown-menu>
    </ng-template>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LanguageSwitcher {
  private readonly languageService = inject(LanguageService);
  private readonly authService = inject(AuthService);
  private readonly settingService = inject(SettingService);
  private readonly transloco = inject(TranslocoService);

  /** 当前语言的短标识（EN / 中），显示在全球化图标右侧；随语言切换响应式更新。 */
  readonly shortLabel = computed(() => this.languageService.currentMeta().short);

  readonly items = computed<LangOption[]>(() => {
    const active = this.languageService.activeLang();
    return this.languageService.options.map((option) => ({
      id: option.id,
      label: option.label,
      active: option.id === active,
    }));
  });

  select(lang: Lang): void {
    // 已登录的选择属于账户：写回设置，本次只在内存生效。会话建立时以账户设置为准
    // （见 SessionContextService），不写回的话这里切过的语言下次登录就被覆盖，
    // 切换器看起来会失灵。
    if (this.authService.isAuthenticated()) {
      this.languageService.applyAccountLang(lang);
      this.settingService
        .setForCurrentUser({ name: SETTINGS.display.language, value: lang })
        .subscribe({
          // 不回滚本次切换——界面已经变了，再变回去更像出错。但必须说出来：
          // 静默吞掉的话，用户会以为账户偏好存好了，下次登录却发现回到旧语言。
          error: (error: unknown) =>
            toast.error(this.transloco.translate('language.saveFailed'), {
              description: applicationErrorMessage(error),
            }),
        });
      return;
    }

    // 未登录的选择属于这台设备：落盘，作为访客页面与主体离开后的回落值。
    this.languageService.setDeviceLang(lang);
  }
}
