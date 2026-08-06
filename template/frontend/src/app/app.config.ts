import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  ErrorHandler,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import {
  provideRouter,
  RouterFeatures,
  withComponentInputBinding,
  withHashLocation,
  withInMemoryScrolling,
  withViewTransitions,
} from '@angular/router';
//#if (IncludeLocalization)
import { provideTransloco, TranslocoService } from '@jsverse/transloco';
//#endif
import { provideHlmSidebarConfig } from '@spartan-ng/helm/sidebar';
import { provideSpartanHlm } from '@spartan-ng/helm/utils';
//#if (IncludeLocalization)
import { firstValueFrom } from 'rxjs';
//#endif

import { routes } from './app.routes';
import { environment } from '../environments/environment';
import { GlobalErrorHandler } from './core/handlers/global-error-handler';
//#if (IncludeLocalization)
import { TranslocoHttpLoader } from './core/i18n/transloco-loader';
import { acceptLanguageInterceptor } from './core/interceptors/accept-language-interceptor';
//#endif
import { httpErrorInterceptor } from './core/interceptors/http-error-interceptor';
import { urlFormatInterceptor } from './core/interceptors/url-format-interceptor';
//#if (IncludeLocalization)
import { LanguageService } from './core/services/language-service';
//#endif
import { StartupService } from './core/services/startup-service';
import { mockInterceptor } from '../../_mock/core/interceptor';
import { provideMock, shouldProvideMock } from '../../_mock/core/providers';

// 定义路由特性，用于增强应用功能和用户体验
const routerFeatures: RouterFeatures[] = [
  // 启用路由参数到组件输入的自动绑定
  withComponentInputBinding(),
  // 启用基于浏览器 View Transitions API 的页面过渡动画
  withViewTransitions(),
  // 配置导航时的滚动行为，导航后滚动到页面顶部
  withInMemoryScrolling({ anchorScrolling: 'enabled', scrollPositionRestoration: 'enabled' }),
  // 根据环境配置决定是否启用哈希路由
  ...(environment.useHash ? [withHashLocation()] : []),
];

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    // Spartan：Angular 21+ 需注册，确保 CDK overlay 层级正确（避免盖过固定定位的 toaster）
    provideSpartanHlm(),
    // 移动端点击侧栏菜单项（导航）后自动收起遮罩侧栏；非导航的下拉触发器单独关闭该行为。
    provideHlmSidebarConfig({ closeMobileSidebarOnMenuButtonClick: true }),
    // 注册全局错误监听器
    provideBrowserGlobalErrorListeners(),
    // 注册全局错误处理器，替换 Angular 默认的 ErrorHandler
    { provide: ErrorHandler, useClass: GlobalErrorHandler },
    provideRouter(routes, ...routerFeatures),
    //#if (IncludeLocalization)
    provideTransloco({
      config: {
        availableLangs: ['en', 'zh-CN'],
        defaultLang: 'en',
        fallbackLang: 'en',
        reRenderOnLangChange: true,
        prodMode: environment.production,
      },
      loader: TranslocoHttpLoader,
    }),
    //#endif
    provideHttpClient(
      withInterceptors([
        //#if (IncludeLocalization)
        acceptLanguageInterceptor, // 注入 Accept-Language，须在 URL 改写等之前
        //#endif
        urlFormatInterceptor,
        httpErrorInterceptor, // 捕获所有 HTTP 错误并显示用户提示
        ...(shouldProvideMock(environment.useMock) ? [mockInterceptor] : []),
      ]),
    ),
    //#if (IncludeLocalization)
    // 首帧前预加载活动语言词条：LanguageService 构造时从 localStorage 解析活动语言并设为 active，
    // 随后加载对应 JSON。确保 shell 与各页首次渲染时 translate() 不命中未加载的裸键（消除首帧缺翻译告警）。
    provideAppInitializer(() => {
      inject(LanguageService);
      const transloco = inject(TranslocoService);
      return firstValueFrom(transloco.load(transloco.getActiveLang()));
    }),
    //#endif
    // 在应用初始化时加载关键数据
    provideAppInitializer(() => inject(StartupService).load()),
    // 注册 Mock 服务
    ...provideMock(environment.useMock),
  ],
};
