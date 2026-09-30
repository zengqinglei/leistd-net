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
  //#if (LocalIdentity)
  withHashLocation,
  //#endif
  withInMemoryScrolling,
  withViewTransitions,
} from '@angular/router';
//#if (IncludeLocalization)
import { provideTransloco } from '@jsverse/transloco';
//#endif
import { provideHlmSidebarConfig } from '@spartan-ng/helm/sidebar';
import { provideSpartanHlm } from '@spartan-ng/helm/utils';
//#if (RemoteTokenAuth)
import { LogLevel, provideAuth } from 'angular-auth-oidc-client';
//#endif

import { appInterceptors } from './app.interceptors';
import { routes } from './app.routes';
import { environment } from '../environments/environment';
import { GlobalErrorHandler } from './core/handlers/global-error-handler';
//#if (IncludeLocalization)
import { provideAppA11yLabels } from './core/i18n/a11y-labels';
import { TranslocoHttpLoader } from './core/i18n/transloco-loader';
import {
  provideLanguageFallbackStrategy,
  provideLanguageInitializer,
} from './core/services/language-service';
//#endif
import { StartupService } from './core/services/startup-service';
import { provideMock } from '../../_mock/core/providers';

// 定义路由特性，用于增强应用功能和用户体验
const routerFeatures: RouterFeatures[] = [
  // 启用路由参数到组件输入的自动绑定
  withComponentInputBinding(),
  // 启用基于浏览器 View Transitions API 的页面过渡动画
  withViewTransitions(),
  // 配置导航时的滚动行为，导航后滚动到页面顶部
  withInMemoryScrolling({ anchorScrolling: 'enabled', scrollPositionRestoration: 'enabled' }),
  //#if (LocalIdentity)
  // 根据环境配置决定是否启用哈希路由
  ...(environment.useHash ? [withHashLocation()] : []),
  //#endif
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
    //#if (RemoteTokenAuth)
    provideAuth({
      config: {
        authority: environment.oidc.authority,
        clientId: environment.oidc.clientId,
        redirectUrl: `${window.location.origin}/auth/callback`,
        postLogoutRedirectUri: window.location.origin,
        responseType: 'code',
        scope: environment.oidc.scope,
        silentRenew: false,
        useRefreshToken: false,
        secureRoutes: [`${window.location.origin}/api`, '/api'],
        // 回调后的导航归 OidcCallback（回到登录前的地址）；不打开时库会自行跳到 postLoginRoute（默认 /），两处导航互相竞争
        triggerAuthorizationResultEvent: true,
        logLevel: environment.production ? LogLevel.Error : LogLevel.Warn,
      },
    }),
    //#endif
    //#if (IncludeLocalization)
    provideTransloco({
      config: {
        availableLangs: ['en', 'zh-CN'],
        defaultLang: 'en',
        fallbackLang: 'en',
        reRenderOnLangChange: true,
        scopes: { autoPrefixKeys: false },
        prodMode: environment.production,
        // 生产构建的词条已由 postbuild 的 transloco-optimize 预先展平，运行时不必再展平一遍。
        // 前提是生产构建走 npm run build（它才会触发 postbuild）；直接 ng build 出来的是未展平的原文件，词条会全部找不到
        flatten: { aot: environment.production },
      },
      loader: TranslocoHttpLoader,
    }),
    // 加载失败不让 Transloco 自行回落并激活，由 LanguageService 处理（理由见 provideLanguageFallbackStrategy）
    provideLanguageFallbackStrategy(),
    provideAppA11yLabels(),
    //#endif
    provideHttpClient(withInterceptors(appInterceptors)),
    //#if (IncludeLocalization)
    // 首帧前加载活动语言词条，首次渲染不出裸键（理由见 provideLanguageInitializer）。
    provideLanguageInitializer(),
    //#endif
    // 在应用初始化时加载关键数据
    provideAppInitializer(() => inject(StartupService).load()),
    // 注册 Mock 服务
    ...provideMock(environment.useMock),
  ],
};
