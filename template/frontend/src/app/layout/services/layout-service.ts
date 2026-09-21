import { Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map, startWith } from 'rxjs/operators';

/**
 * 布局服务：页面标题 + 当前区段（平台/工作区）派生状态。
 *
 * 本服务只维护页面标题与当前区段；侧边栏状态由 Spartan 的 `HlmSidebarService` 统一管理。
 */
@Injectable({
  providedIn: 'root',
})
export class LayoutService {
  private readonly router = inject(Router);

  /**
   * 页面标题（面包屑末级）。各页在 ngOnInit/effect 里设置本页标题。
   */
  // 初值为空，不给字面量：占位文案在任何语言下都不正确，多语言变体下会先闪一下英文。
  // 空标题时 default-header 整个末级（含分隔符）不渲染，因此不会留下断裂的面包屑。
  title = signal<string>('');

  /** 当前路由（跟随导航结束刷新）。Header/Sidebar/UserMenu 共享，避免各自重复接线。 */
  readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects ?? event.url),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /** 是否处于平台（后台管理）区段。 */
  readonly isPlatform = computed(() => this.currentUrl().startsWith('/platform'));

  /** 当前区段首页路由（供品牌头 / 面包屑首级指向本区段，避免跨区段跳转）。 */
  readonly homeRoute = computed(() => (this.isPlatform() ? '/platform' : '/workspace/dashboard'));
}
