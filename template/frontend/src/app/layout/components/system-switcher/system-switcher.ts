//#if (OpenIddictServer || RemoteTokenAuth)
import { CdkMenuTrigger } from '@angular/cdk/menu';
//#endif
// prettier-ignore
import { ChangeDetectionStrategy, Component, inject,
  //#if (OpenIddictServer || RemoteTokenAuth)
  ElementRef, computed, viewChild,
  //#endif
} from '@angular/core';
import { RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
//#if (OpenIddictServer || RemoteTokenAuth)
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideChevronsUpDown } from '@ng-icons/lucide';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
//#else
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
//#endif

import { LayoutService } from '../../../core/services/layout-service';
//#if (OpenIddictServer || RemoteTokenAuth)
import { SystemDirectoryService } from '../../../core/services/system-directory-service';
//#endif
import { Logo } from '../../../shared/components/logo/logo';
//#if (!IncludeLocalization)
import { englishText } from '../../../shared/utils/english-text';
//#endif

@Component({
  selector: 'app-system-switcher',
  // prettier-ignore
  imports: [RouterLink, Logo, ...HlmSidebarImports,
    //#if (OpenIddictServer || RemoteTokenAuth)
    NgIcon, ...HlmDropdownMenuImports,
    //#endif
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  //#if (OpenIddictServer || RemoteTokenAuth)
  providers: [provideIcons({ lucideCheck, lucideChevronsUpDown })],
  //#endif
  templateUrl: './system-switcher.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SystemSwitcher {
  readonly layout = inject(LayoutService);
  //#if (OpenIddictServer || RemoteTokenAuth)
  private readonly sidebar = inject(HlmSidebarService);
  readonly side = computed(() => (this.sidebar.isMobile() ? 'bottom' : 'right'));
  //#endif
  //#if (OpenIddictServer || RemoteTokenAuth)
  readonly directory = inject(SystemDirectoryService);
  private readonly menuTrigger = viewChild(CdkMenuTrigger);
  private readonly menuButton = viewChild(CdkMenuTrigger, { read: ElementRef });

  /** 先消费嵌套菜单的 Escape，再经 CDK 公共入口关闭，避免同一按键继续关闭父抽屉。 */
  closeMenu(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.menuTrigger()?.close();
    this.menuButton()?.nativeElement.focus();
  }

  readonly currentName = computed(
    () => this.directory.systems().find((system) => system.isCurrent)?.displayName ?? '',
  );
  //#endif
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
}
//#if (!IncludeLocalization)

const ENGLISH: Record<string, string> = {
  'layout.sidebar.appTitle': 'Template Project',
  'layout.systemSwitcher.title': 'Switch system',
};
//#endif
