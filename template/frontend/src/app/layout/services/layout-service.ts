import { Injectable, signal } from '@angular/core';

/**
 * 布局服务：仅保留页面标题状态。
 *
 * 侧边栏的展开/折叠、移动端抽屉、响应式与持久化由 Spartan 的 `HlmSidebarService`
 * （`@spartan-ng/helm/sidebar`，providedIn: root）统一管理，不再由本服务维护。
 */
@Injectable({
  providedIn: 'root',
})
export class LayoutService {
  /**
   * 页面标题
   */
  // 初始占位值（各页在 ngOnInit/effect 里会立即覆盖为本页标题）
  title = signal<string>('Overview');
}
