import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMenu } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSeparator } from '@spartan-ng/helm/separator';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { filter, map } from 'rxjs';

/** 设置页左侧的一个面板入口。 */
export interface SettingsPanelLink {
  /** 子路由段，同时是 URL 里的面板名。 */
  path: string;
  label: string;
  icon: string;
  description?: string;
}

/**
 * 设置页外壳：标题、面板导航（宽屏左侧列表，窄屏收进抽屉）与面板内容出口。
 *
 * 个人设置与系统设置共用它，面板清单由宿主组件给出。面板是**子路由**而不是页内状态：
 * 刷新、分享链接、头像菜单直达都落在同一面板，浏览器的后退也按面板走。
 */
@Component({
  selector: 'app-settings-shell',
  imports: [RouterLink, RouterOutlet, NgIcon, HlmButton, HlmSeparator, ...HlmSheetImports],
  providers: [provideIcons({ lucideMenu })],
  templateUrl: './settings-shell.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsShell {
  private readonly router = inject(Router);

  readonly heading = input.required<string>();
  readonly description = input('');
  readonly panels = input.required<readonly SettingsPanelLink[]>();
  /** 面板导航的可访问名。 */
  readonly navLabel = input('');
  /** 窄屏打开面板导航的按钮只有图标，可访问名必须显式给出。 */
  readonly openNavLabel = input('');

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  /** 当前面板：URL 最后一段对应的那一项；不在清单里时没有当前面板。 */
  protected readonly activePanel = computed(() => {
    const last = this.url().split(/[?#]/)[0].split('/').pop() ?? '';
    return this.panels().find((panel) => panel.path === last);
  });
}
