import { Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map, startWith } from 'rxjs/operators';

/**
 * 布局服务：页面标题 + 当前区段（平台/工作区）派生状态。
 *
 * 侧边栏的展开/折叠、移动端抽屉、响应式与持久化由 Spartan 的 `HlmSidebarService`
 * （`@spartan-ng/helm/sidebar`，providedIn: root）统一管理，不再由本服务维护。
 */
@Injectable({
  providedIn: 'root',
})
export class LayoutService {
  private readonly router = inject(Router);

  /**
   * 页面标题
   */
  // 初始占位值（各页在 ngOnInit/effect 里会立即覆盖为本页标题）
  title = signal<string>('Overview');

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
