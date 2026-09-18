import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { provideIcons } from '@ng-icons/core';
// prettier-ignore
import {
  //#if (LocalIdentity)
  //#if (IncludeNotifications)
  lucideBell,
  //#endif
  lucideShieldCheck,
  lucideUserRound,
  //#endif
  lucideSlidersHorizontal,
} from '@ng-icons/lucide';

//#if (IncludeLocalization)
import { translationReady } from '../../../core/i18n/translation-ready';
//#endif
import { LayoutService } from '../../../layout/services/layout-service';
import { SettingsPageState } from '../settings-page-state';
import { SettingsPanelLink, SettingsShell } from '../settings-shell/settings-shell';

/**
 * 个人设置：关于"我自己"的一切，所有登录用户同一处。
 *
 * 管理人员也是用户，从头像菜单进这里，管理平台不另放一份——两个入口就是两份状态。
 * 面板清单是固定的：个人资料与账户安全是专门的表单，「通知」是通知偏好分组，「偏好」收纳其余所有允许用户覆盖的设置分组
 * （新增一项用户级设置会自动出现在那里，不需要在这里登记）。
 */
@Component({
  selector: 'app-personal-settings',
  imports: [SettingsShell],
  // prettier-ignore
  providers: [
    SettingsPageState,
    provideIcons({
      //#if (LocalIdentity)
      //#if (IncludeNotifications)
      lucideBell,
      //#endif
      lucideShieldCheck,
      lucideUserRound,
      //#endif
      lucideSlidersHorizontal,
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
export class PersonalSettings {
  private readonly layoutService = inject(LayoutService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);

  private readonly t = (key: string) => {
    this.translationReady();
    return this.transloco.translate(key);
  };

  protected readonly heading = computed(() => this.t('settings.personal.title'));
  protected readonly description = computed(() => this.t('settings.personal.description'));
  protected readonly navLabel = computed(() => this.t('settings.navigation'));
  protected readonly openNavLabel = computed(() => this.t('settings.openNavigation'));

  protected readonly panels = computed<SettingsPanelLink[]>(() => [
    //#if (LocalIdentity)
    {
      path: 'profile',
      icon: 'lucideUserRound',
      label: this.t('settings.panels.profile.title'),
      description: this.t('settings.panels.profile.description'),
    },
    {
      path: 'security',
      icon: 'lucideShieldCheck',
      label: this.t('settings.panels.security.title'),
      description: this.t('settings.panels.security.description'),
    },
    //#if (IncludeNotifications)
    {
      path: 'notifications',
      icon: 'lucideBell',
      label: this.t('settings.panels.notifications.title'),
      description: this.t('settings.panels.notifications.description'),
    },
    //#endif
    //#endif
    {
      path: 'preferences',
      icon: 'lucideSlidersHorizontal',
      label: this.t('settings.panels.preferences.title'),
      description: this.t('settings.panels.preferences.description'),
    },
  ]);
  //#else
  protected readonly heading = computed(() => 'Personal settings');
  protected readonly description = computed(
    () => 'Manage your profile, account security and preferences',
  );
  protected readonly navLabel = computed(() => 'Settings navigation');
  protected readonly openNavLabel = computed(() => 'Open settings navigation');

  protected readonly panels = computed<SettingsPanelLink[]>(() => [
    //#if (LocalIdentity)
    {
      path: 'profile',
      icon: 'lucideUserRound',
      label: 'Profile',
      description: 'Your avatar, name and contact details',
    },
    {
      path: 'security',
      icon: 'lucideShieldCheck',
      label: 'Account & security',
      description: 'Your password and how you sign in',
    },
    //#if (IncludeNotifications)
    {
      path: 'notifications',
      icon: 'lucideBell',
      label: 'Notifications',
      description:
        'Choose where each kind of notification reaches you. In-app security alerts are always on; email only goes to a verified address',
    },
    //#endif
    //#endif
    {
      path: 'preferences',
      icon: 'lucideSlidersHorizontal',
      label: 'Preferences',
      description: 'Language and time zone. Applies to you only; leave blank to follow your system',
    },
  ]);
  //#endif

  constructor() {
    // 面包屑末级文案由页面自行设置，与其他页保持同一约定。
    effect(() => this.layoutService.title.set(this.heading()));
  }
}
