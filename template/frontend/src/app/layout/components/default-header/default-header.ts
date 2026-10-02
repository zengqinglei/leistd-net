import { ChangeDetectionStrategy, Component, DestroyRef, inject, input } from '@angular/core';
//#if (IncludeLocalization)
//#if (LocalIdentity)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#else
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
//#endif
//#if (LocalIdentity)
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideVenetianMask } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
//#endif
import { HlmBreadcrumbImports } from '@spartan-ng/helm/breadcrumb';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmKbdImports } from '@spartan-ng/helm/kbd';
//#if (LocalIdentity)
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
//#endif
import { HlmSeparatorImports } from '@spartan-ng/helm/separator';
import { HlmSidebarTrigger } from '@spartan-ng/helm/sidebar';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

//#if (LocalIdentity)
import { applicationErrorMessage } from '../../../core/errors/application-http-error';
import { ImpersonationService } from '../../../core/services/impersonation-service';
//#endif
//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../shared/components/language-switcher/language-switcher';
//#endif
import { ThemeModeToggle } from '../../../shared/components/theme-mode-toggle/theme-mode-toggle';
//#if (LocalIdentity)
import { PopoverAria } from '../../../shared/directives/popover-aria';
//#endif
//#if (!IncludeLocalization)
import { englishText } from '../../../shared/utils/english-text';
//#endif
import { LayoutService } from '../../services/layout-service';
//#if (IncludeNotifications)
import { Notifications } from '../notifications/notifications';
//#endif
import { UserMenu } from '../user-menu/user-menu';
import { WorkspaceNav } from '../workspace-nav/workspace-nav';

@Component({
  selector: 'app-default-header',
  standalone: true,
  imports: [
    // 基础布局依赖；通知 / 本地化组件按条件补充。
    HlmButton,
    ThemeModeToggle,
    HlmSidebarTrigger,
    ...HlmBreadcrumbImports,
    ...HlmKbdImports,
    ...HlmSeparatorImports,
    ...HlmTooltipImports,
    UserMenu,
    WorkspaceNav,
    //#if (LocalIdentity)
    NgIcon,
    ...HlmPopoverImports,
    PopoverAria,
    //#endif
    //#if (IncludeNotifications)
    Notifications,
    //#endif
    //#if (IncludeLocalization)
    LanguageSwitcher,
    TranslocoDirective,
    //#endif
  ],
  //#if (LocalIdentity)
  providers: [provideIcons({ lucideVenetianMask })],
  //#endif
  templateUrl: './default-header.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DefaultHeader {
  /**
   * `sidebar`：配合侧栏，左侧是折叠按钮与面包屑，头像在侧栏底部。
   * `topbar`：没有侧栏，左侧是品牌与导航，头像放在最右。
   */
  readonly layout = input<'sidebar' | 'topbar'>('sidebar');

  readonly layoutService = inject(LayoutService);
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
  //#if (LocalIdentity)
  readonly impersonation = inject(ImpersonationService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif
  //#endif

  constructor() {
    // 页面标题只在带页头的布局里有意义，离开布局时清掉：登录页、落地页等不设标题，
    // 不清的话浏览器标签页会一直挂着上一个布局页的标题。页头随布局销毁，早于下一页设置标题
    inject(DestroyRef).onDestroy(() => this.layoutService.title.set(''));
    //#if (LocalIdentity)

    // 退出模拟是整页跳转，提示只能在新页面上补（见 ImpersonationService 的一次性标记）。
    //#if (IncludeLocalization)
    this.impersonation.notifyAfterRenderIfJustExited(() =>
      toast.success(this.transloco.translate('impersonation.exited')),
    );
    //#else
    this.impersonation.notifyAfterRenderIfJustExited(() =>
      toast.success('Returned to your own account.'),
    );
    //#endif
    //#endif
  }
  //#if (LocalIdentity)

  /**
   * 结束模拟。失败时不跳转，让顶栏里的模拟状态留在原处，并说明原因——
   * 悄悄跳走会让人以为已经回到自己的账号，而会话其实还在租户里；
   * 什么都不说则像按钮没反应，用户会一直重试。
   */
  async endImpersonation(): Promise<void> {
    try {
      await this.impersonation.end();
    } catch (error) {
      //#if (IncludeLocalization)
      toast.error(this.transloco.translate('impersonation.exitFailed'), {
        description: applicationErrorMessage(error),
      });
      //#else
      toast.error('Could not end impersonation.', { description: applicationErrorMessage(error) });
      //#endif
    }
  }
  //#endif
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.toggleSidebar': 'Toggle sidebar',
  'menu.platform': 'Admin platform',
  'menu.workspace': 'Workspace',
  'impersonation.banner': 'Acting as tenant {{tenant}} · started by {{impersonator}}',
  'impersonation.badge': 'Impersonating · {{tenant}}',
  'impersonation.title': 'Impersonating a tenant',
  'impersonation.note': "Everything you do now is recorded under this tenant's account.",
  'impersonation.exit': 'Exit impersonation',
};
//#endif
