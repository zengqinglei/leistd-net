import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoModule } from '@jsverse/transloco';
//#endif
//#if (!IncludeNotifications)
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBell } from '@ng-icons/lucide';
//#endif
import { HlmBreadcrumbImports } from '@spartan-ng/helm/breadcrumb';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmKbdImports } from '@spartan-ng/helm/kbd';
import { HlmSeparatorImports } from '@spartan-ng/helm/separator';
import { HlmSidebarTrigger } from '@spartan-ng/helm/sidebar';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../shared/components/language-switcher/language-switcher';
//#endif
import { ThemeModeToggle } from '../../../shared/components/theme-mode-toggle/theme-mode-toggle';
import { LayoutService } from '../../services/layout-service';
//#if (IncludeNotifications)
import { Notifications } from '../notifications/notifications';
//#endif

@Component({
  selector: 'app-default-header',
  standalone: true,
  imports: [
    // 基础布局依赖；通知 / 本地化组件按条件补充。
    //#if (!IncludeNotifications)
    NgIcon,
    //#endif
    HlmButton,
    ThemeModeToggle,
    HlmSidebarTrigger,
    ...HlmBreadcrumbImports,
    ...HlmKbdImports,
    ...HlmSeparatorImports,
    ...HlmTooltipImports,
    //#if (IncludeNotifications)
    Notifications,
    //#endif
    //#if (IncludeLocalization)
    LanguageSwitcher,
    TranslocoModule,
    //#endif
  ],
  //#if (!IncludeNotifications)
  providers: [provideIcons({ lucideBell })],
  //#endif
  templateUrl: './default-header.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DefaultHeader {
  readonly layoutService = inject(LayoutService);

  // 面包屑首级文案：随当前区段（平台/工作区）切换；首页路由由 layoutService.homeRoute() 提供。
  //#if (IncludeLocalization)
  readonly homeLabel = computed(() =>
    this.layoutService.isPlatform() ? 'menu.platform' : 'menu.workspace',
  );
  //#else
  readonly homeLabel = computed(() =>
    this.layoutService.isPlatform() ? 'Admin platform' : 'Workspace',
  );
  //#endif
}
