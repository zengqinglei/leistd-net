import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { provideIcons } from '@ng-icons/core';
import {
  lucideActivity,
  lucideArchive,
  lucideMail,
  lucideSettings,
  lucideShieldCheck,
  lucideUserPlus,
} from '@ng-icons/lucide';
import { filter, map } from 'rxjs';

//#if (IncludeLocalization)
import { translationReady } from '../../../core/i18n/translation-ready';
//#endif
import { LayoutService } from '../../../layout/services/layout-service';
// prettier-ignore
import {
  groupPath,
  groupSettings,
  settingsInScope,
  SettingsPageState,
} from '../settings-page-state';
import { SettingsPanelLink, SettingsShell } from '../settings-shell/settings-shell';

/**
 * 各分组在面板导航上的图标。只影响外观：没登记的分组照样出现，用通用图标。
 * 面板本身由后端分组决定，这里**不是**面板清单。
 */
const GROUP_ICONS: Readonly<Record<string, string>> = {
  Registration: 'lucideUserPlus',
  Security: 'lucideShieldCheck',
  Email: 'lucideMail',
  Operations: 'lucideActivity',
  Audit: 'lucideArchive',
};

/**
 * 系统设置：本租户（或宿主）下所有人的默认值与策略。
 *
 * **一个后端分组就是一个面板**，面板名与顺序都来自后端（URL 用分组标识的短横线写法）。
 * 不在前端另列面板清单：漏登记的后果是整组设置从界面上消失，既不报错也查不出来。
 * 能看到哪些分组也由后端按上下文裁剪——进程级分组只在宿主上下文下发，租户管理员自然看不到。
 */
@Component({
  selector: 'app-system-settings',
  imports: [SettingsShell],
  providers: [
    SettingsPageState,
    provideIcons({
      lucideActivity,
      lucideArchive,
      lucideMail,
      lucideSettings,
      lucideShieldCheck,
      lucideUserPlus,
    }),
  ],
  template: `
    <app-settings-shell
      [heading]="heading()"
      [description]="description()"
      [panels]="panels()"
      [navLabel]="navLabel()"
      [openNavLabel]="openNavLabel()"
    />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SystemSettings {
  private readonly layoutService = inject(LayoutService);
  private readonly pageState = inject(SettingsPageState);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);

  private readonly t = (key: string) => {
    this.translationReady();
    return this.transloco.translate(key);
  };

  protected readonly heading = computed(() => this.t('settings.system.title'));
  protected readonly description = computed(() => this.t('settings.system.description'));
  protected readonly navLabel = computed(() => this.t('settings.navigation'));
  protected readonly openNavLabel = computed(() => this.t('settings.openNavigation'));
  //#else
  protected readonly heading = computed(() => 'System settings');
  protected readonly description = computed(
    () => 'Defaults and policies for everyone here; each user may override their own preferences',
  );
  protected readonly navLabel = computed(() => 'Settings navigation');
  protected readonly openNavLabel = computed(() => 'Open settings navigation');
  //#endif

  protected readonly panels = computed<SettingsPanelLink[]>(() =>
    groupSettings(settingsInScope(this.pageState.settings(), 'system')).map((group) => ({
      path: groupPath(group.key),
      label: group.label,
      icon: GROUP_ICONS[group.key] ?? 'lucideSettings',
    })),
  );

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  constructor() {
    effect(() => this.layoutService.title.set(this.heading()));

    // 没带面板（直接进 /platform/settings）或带了一个不存在的面板（旧链接、换了上下文）时，
    // 落到第一个面板。面板要等设置取回才知道，所以不能写成路由表里的静态重定向。
    effect(() => {
      const panels = this.panels();
      if (!this.pageState.loaded() || panels.length === 0) {
        return;
      }

      const current = this.url().split(/[?#]/)[0].split('/').pop();
      if (!panels.some((panel) => panel.path === current)) {
        void this.router.navigate([panels[0].path], { relativeTo: this.route, replaceUrl: true });
      }
    });
  }
}
