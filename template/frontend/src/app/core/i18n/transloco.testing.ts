import { EnvironmentProviders, Provider } from '@angular/core';
import { provideTransloco, TRANSLOCO_LOADER, TranslocoLoader } from '@jsverse/transloco';
import { of } from 'rxjs';

import { provideLanguageFallbackStrategy } from '../services/language-service';

/** 空词条：文案回落成键名，那正是用例里能安全断言的东西。 */
const emptyTranslations: TranslocoLoader = { getTranslation: () => of({}) };

/**
 * 单测用的 Transloco 装配。
 *
 * 词条一律为空，而不是去加载 `public/i18n/*.json`：组件用例断言的是**行为**
 * （点了保存有没有发请求、失败有没有就地报错），不是文案。真去加载会把每个用例都绑在
 * 词条文件上——改一个键让一批不相关的用例变红——而且加载是异步的，等词条到位会让每个
 * 用例都多一层异步。文案因此回落成键名，这是 Transloco 的既定行为。
 *
 * 同时关掉缺词条日志：空词条下**每个**键都"缺失"，那些告警对"是否真的缺词条"零信息量，
 * 却会把真正的告警（弃用 API、变更检测问题）埋在几十行噪声里。词条完整性由 i18n 静态闸门
 * （引用的键必须存在于资源、en/zh 键集与占位符一致）和跑真实词条的浏览器闭环负责。
 *
 * 手写 `TranslocoService` 桩不是替代品：模板里有 `*transloco` 结构指令，TS 里有 `translate()`
 * 与 `translateSignal()`，桩要把 config、`langChanges$`、scope 解析全补齐才跑得起来，还会在真实实现新增
 * 依赖时（例如 `LanguageService` 会调 `setActiveLang`）静默失配。
 *
 * 页面级用例若在首次渲染途中才创建 `LanguageService`（经 `SettingContextService` 等间接依赖），
 * 它构造时同步切换语言，会让结构指令在渲染中重入，报 "max number of directives"。应用里它由
 * `provideLanguageInitializer()` 在启动时创建，不会遇到；用例在 `createComponent` 之前先注入它
 * （或依赖它的服务）即可。这里不统一预建：它会读本机语言偏好（同源 localStorage、浏览器语言），
 * 在只装配了 `['en']` 的用例里切到别的语言，把确定性的报错换成随环境变化的失败。
 *
 * @param langs 可用语言，第一个同时作为默认与回落语言。
 * @param loader 需要控制词条到达时机的用例传自己的加载器（见 language-service.spec）。
 */
export function provideTranslocoTesting(
  langs: readonly [string, ...string[]] = ['en'],
  loader: TranslocoLoader = emptyTranslations,
): (Provider | EnvironmentProviders)[] {
  return [
    provideTransloco({
      config: {
        availableLangs: [...langs],
        defaultLang: langs[0],
        fallbackLang: langs[0],
        // 与应用配置一致：关掉时结构指令只取第一次的语言，切换语言的用例就测不出任何东西
        reRenderOnLangChange: true,
        missingHandler: { logMissingKey: false },
      },
    }),
    { provide: TRANSLOCO_LOADER, useValue: loader },
    // 与应用配置一致：加载失败的处理归 LanguageService，用默认策略就测不到应用里的失败路径
    provideLanguageFallbackStrategy(),
  ];
}
