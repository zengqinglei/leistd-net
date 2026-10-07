import { createEnvironmentInjector, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { DefaultHeader } from './default-header';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
//#if (Impersonation)
import { ImpersonationService } from '../../../core/services/impersonation-service';
//#endif
import { LayoutService } from '../../../core/services/layout-service';

/**
 * 页面标题只在带页头的布局里设置；离开布局（去登录页、落地页）时页头负责清掉，
 * 否则浏览器标签页会一直挂着上一个布局页的标题。
 */
describe('DefaultHeader page title', () => {
  it('clears the page title when the header leaves with its layout', () => {
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        provideRouter([]),
        //#if (Impersonation)
        // 模拟退出提示与本用例无关，真实实例会读会话存储
        {
          provide: ImpersonationService,
          useValue: { notifyAfterRenderIfJustExited: () => undefined },
        },
        //#endif
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
      ],
    });
    const layout = TestBed.inject(LayoutService);
    // 只构造类、不渲染模板：页头随布局销毁时，它所在的注入上下文随之销毁
    const scope = createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
    scope.runInContext(() => new DefaultHeader());

    layout.title.set('Users');
    expect(layout.title()).toBe('Users');

    scope.destroy();

    expect(layout.title()).toBe('');
  });
});
