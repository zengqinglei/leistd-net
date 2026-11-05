import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, inject } from '@angular/core';

/**
 * 整页离开当前文档：模板里所有整页跳转都经这里，测试据此观察跳转而不真的离开页面。
 *
 * 发起离开的页面已经处于"离开中"（守卫返回 false、按钮忙碌、表单已提交）。浏览器从往返缓存（bfcache）恢复它时
 * 守卫与初始化器不会重跑，页面会停在离开前的那一刻；所以离开之后再以 `persisted` 恢复就整页重载，
 * 由应用按真实会话重新启动。未离开时的恢复照常保留缓存。
 */
@Injectable({ providedIn: 'root' })
export class FullPageNavigationService {
  private readonly document = inject(DOCUMENT);
  private leaving = false;

  constructor() {
    const view = this.document.defaultView;
    const onPageShow = (event: PageTransitionEvent): void => {
      if (event.persisted && this.leaving) {
        this.document.location.reload();
      }
    };
    view?.addEventListener('pageshow', onPageShow);
    inject(DestroyRef).onDestroy(() => view?.removeEventListener('pageshow', onPageShow));
  }

  /**
   * 替换当前历史条目：当前页只是中转（守卫或 401 发起的登录、认证完成后的续跑、退出后的落点、模拟登录进出），
   * 后退不该回到它。
   */
  replace(url: string): void {
    this.leaving = true;
    this.document.location.replace(url);
  }
  //#if (ExternalLogin)

  /** 保留当前历史条目：用户在有意义的页面上主动离开（外部登录与绑定），在提供方页面后退应回到这里。 */
  assign(url: string): void {
    this.leaving = true;
    this.document.location.assign(url);
  }
  //#endif
  //#if (RemoteTokenAuth || OpenIddictServer)

  /**
   * 整页表单 POST：协议要求的离开（退出后的重定向链由浏览器跟随，不经 XHR）。表单提交没有替换历史的选项，
   * 后退回到这一页时由往返缓存兜底重载。
   * @param fields 隐藏字段，按给出的次序提交。
   */
  post(action: string, fields: Record<string, string> = {}): void {
    const form = this.document.createElement('form');
    form.method = 'POST';
    form.action = action;
    for (const [name, value] of Object.entries(fields)) {
      const input = this.document.createElement('input');
      input.type = 'hidden';
      input.name = name;
      input.value = value;
      form.appendChild(input);
    }
    this.document.body.appendChild(form);
    this.leaving = true;
    form.submit();
  }
  //#endif
}
