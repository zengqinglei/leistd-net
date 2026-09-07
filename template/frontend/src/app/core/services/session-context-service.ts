import { Injectable, inject } from '@angular/core';

import { AuthService } from './auth-service';
import { AuthorizationService } from './authorization-service';
//#if (IncludeLocalization)
import { SUPPORTED_LANGS, Lang, LanguageService } from './language-service';
//#endif
import { SettingContextService } from '../settings/setting-context-service';
//#if (IncludeLocalization)
import { SETTINGS } from '../settings/setting.constants';
//#endif
import { SettingOutputDto } from '../settings/setting.dto';

/**
 * 认证会话上下文：主体确立与主体离开这两件事的唯一入口。
 *
 * 一次会话里跟着主体走的东西有三份——当前用户、权限、设置。它们必须一起建立、一起清空：
 * 分散在各处「各自记得调」的做法迟早漏掉一条，而漏掉的表现都很隐蔽。已经踩过的两个：
 *
 * - 登录成功后只加载用户和权限，设置没载入。SPA 内跳转不会重跑应用初始化器，
 *   于是保存过的显示偏好要硬刷新才生效——「设置真的生效」在最常见的路径上不成立。
 * - 会话过期只清了认证数据，权限和设置留着。下一个用户登进来若设置请求失败，
 *   界面会继续用前一个人的偏好。已经从设置**应用出去**的那部分尤其要当心：
 *   它不在快照里，清快照收不回来，得显式退回（见 {@link clearSettings}）。
 *
 * 因此主体的建立与离开一律走 {@link establish} 与 {@link clear}，设置的刷新走
 * {@link refreshSettings}，不要再单独调下层的 load/clear。
 */
@Injectable({ providedIn: 'root' })
export class SessionContextService {
  private readonly authService = inject(AuthService);
  private readonly authorizationService = inject(AuthorizationService);
  private readonly settingContext = inject(SettingContextService);
  //#if (IncludeLocalization)
  private readonly languageService = inject(LanguageService);
  //#endif

  /**
   * 主体确立后建立会话上下文：权限与设置就位，并应用由设置派生的界面状态。
   *
   * 权限失败向上抛：Guard 和菜单按它裁剪，拿不到就只能当无权限，让调用方决定怎么办。
   * 设置失败只清空不抛：它不是进入应用的硬依赖，各消费端有自己的默认行为。
   */
  async establish(): Promise<void> {
    await this.authorizationService.initialize();

    try {
      await this.refreshSettings();
    } catch {
      // 不保留上一份快照：主体已经换人，旧快照属于上一个用户。
      this.clearSettings();
    }
  }

  /**
   * 重新载入设置并应用由它派生的界面状态，返回本次取到的快照。
   *
   * 设置页保存后必须走这里而不是直接调 `SettingContextService.load()`：有的设置除了
   * 进快照还要被**应用出去**，只刷新数据不重新应用，界面会停在旧值——接口和字段都显示
   * 保存成功，唯独看得见的那部分没变，正是最难被发现的一类假开关。
   *
   * 返回快照让调用方复用同一份结果刷新自己的视图，不必再发一次请求。
   */
  async refreshSettings(): Promise<readonly SettingOutputDto[]> {
    const settings = await this.settingContext.load();
    //#if (IncludeLocalization)
    this.applyLanguageSetting();
    //#endif
    return settings;
  }

  /**
   * 主体离开：认证数据、权限、设置一起清掉。
   *
   * 非静默 401、启动流进登录页、SPA 内进入登录页都走这里。`logout()` 是刻意的例外——
   * 它之后是整页跳转，内存状态随页面重建，不必再走一遍。
   */
  clear(): void {
    this.authService.clearAuthData();
    this.authorizationService.clear();
    this.clearSettings();
  }

  /**
   * 清掉跟着主体走的设置状态。
   *
   * 收在一个方法里，是为了让「又多了一样跟主体走的派生状态」只有一处要改——
   * 分成两处迟早只改一处，而漏掉的表现是「下一个人接着用上一个人的偏好」。
   */
  private clearSettings(): void {
    this.settingContext.clear();
    //#if (IncludeLocalization)
    // 语言不在快照里：它已经应用到 LanguageService 上了，清快照收不回来，
    // 于是共享机器上前一个人的语言会留给下一个人。
    this.languageService.resetToDeviceLang();
    //#endif
  }
  //#if (IncludeLocalization)

  /**
   * 把账户设置里的界面语言应用到本次会话。
   *
   * 登录后以账户设置为准，而不是本设备偏好：设置页改了语言必须真的生效，否则那就是个假开关。
   * 反向的一致性由语言切换器保证——已登录时它会把选择写回设置。未登录的访客用设备偏好。
   */
  private applyLanguageSetting(): void {
    const value = this.settingContext.valueOf(SETTINGS.display.language);
    if (value && (SUPPORTED_LANGS as readonly string[]).includes(value)) {
      this.languageService.applyAccountLang(value as Lang);
      return;
    }

    // 取不到值、或值不是本端支持的语言（存量数据、绕过接口直写、服务端先支持了本端
    // 还没有的语言），都退回设备偏好。这一支「什么都不做」等于沿用上一个主体的语言——
    // 请求成功反而绕过了清理，是同一个泄漏换了个门进来。
    this.languageService.resetToDeviceLang();
  }
  //#endif
}
