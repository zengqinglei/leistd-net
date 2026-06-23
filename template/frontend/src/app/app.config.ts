import { provideHttpClient, withInterceptors, withInterceptorsFromDi } from '@angular/common/http';
import {
  ApplicationConfig,
  ErrorHandler,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection
} from '@angular/core';
import {
  provideRouter,
  RouterFeatures,
  withComponentInputBinding,
  withHashLocation,
  withInMemoryScrolling,
  withViewTransitions
} from '@angular/router';
import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';
import { MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';

const TemplatePreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#eef1ff',
      100: '#d9e0ff',
      200: '#bccfff',
      300: '#8eb0ff',
      400: '#5a8aff',
      500: '#134cff',
      600: '#0f3de6',
      700: '#0d31c2',
      800: '#0e289e',
      900: '#11247e',
      950: '#091550'
    },
    surface: {
      0: '#ffffff',
      50: '#f7f8fa',
      100: '#ebedf0',
      200: '#d4d7dd',
      300: '#b4b8c1',
      400: '#8d929e',
      500: '#767680',
      600: '#5e5e66',
      700: '#484a58',
      800: '#353842',
      900: '#2a2a2e',
      950: '#131212'
    },
    formField: {
      paddingX: '0.625rem',
      paddingY: '0.375rem'
    }
  }
});

import { routes } from './app.routes';
import { environment } from '../environments/environment';
import { GlobalErrorHandler } from './core/handlers/global-error-handler';
import { httpErrorInterceptor } from './core/interceptors/http-error-interceptor';
import { urlFormatInterceptor } from './core/interceptors/url-format-interceptor';
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
  // 根据环境配置决定是否启用哈希路由
  ...(environment.useHash ? [withHashLocation()] : [])
];

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    // 注册全局错误监听器
    provideBrowserGlobalErrorListeners(),
    // 注册全局错误处理器，替换 Angular 默认的 ErrorHandler
    { provide: ErrorHandler, useClass: GlobalErrorHandler },
    provideRouter(routes, ...routerFeatures),
    providePrimeNG({
      theme: {
        preset: TemplatePreset,
        options: {
          darkModeSelector: '.dark',
          cssLayer: {
            name: 'primeng',
            order: 'theme, base, primeng'
          }
        }
      }
    }),
    // 注册 Mock 服务
    ...provideMock(environment.useMock),
    provideHttpClient(
      withInterceptors([
        urlFormatInterceptor,
        httpErrorInterceptor // 捕获所有 HTTP 错误并显示用户提示
      ]),
      withInterceptorsFromDi() // 启用对基于类的拦截器的支持
    ),
    // 在应用初始化时加载关键数据
    provideAppInitializer(() => inject(StartupService).load()),
    // 注册 PrimeNG MessageService
    MessageService
  ]
};
