import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { SettingService } from './setting-service';
import { SETTINGS } from './setting.constants';
import { SettingOutputDto } from './setting.dto';
//#if (IncludeLocalization)
import { LanguageService } from '../services/language-service';
//#endif

/**
 * 本次会话生效的设置值。
 *
 * 设置在启动时加载一次并放进 signal，界面直接读它，而不是每处消费都发一次请求。
 * 设置页改完值后调用 {@link load} 刷新，界面随 signal 同步更新。
 *
 * 未登录时为空，读取方一律拿到 `undefined` 并回落到自己的默认行为。
 */
@Injectable({ providedIn: 'root' })
export class SettingContextService {
  private readonly settingService = inject(SettingService);
  //#if (IncludeLocalization)
  private readonly languageService = inject(LanguageService);
  //#endif
  private readonly settings = signal<readonly SettingOutputDto[]>([]);

  /**
   * 展示时区（IANA 名）；未设置时为 `undefined`，日期按浏览器本地时区展示。
   *
   * 时间一律以 UTC 存储，只在展示时换算——这也是它必须有个统一读取点的原因：
   * 每处各自 new Date() 会让同一时刻在不同页面显示成不同时间。
   */
  readonly timeZone = computed(() => this.valueOf(SETTINGS.display.timeZone));

  /**
   * 日期书写用的 locale：决定字段顺序、月份写法与 12/24 小时制。
   *
   * 刻意由**界面语言**驱动而不是时区：时区决定"哪一刻"，locale 才编码"日期怎么读"。
   * 取不到时为 `undefined`，由 `appDate` 回落到固定的 `YYYY-MM-DD` 写法。
   *
   * 没有配套的"日期格式"设置：精度由调用点的槽位决定，写法由这里的 locale 决定，
   * 两者都不需要用户再拧一个开关。
   */
  readonly displayLocale = computed(() => this.resolveLocale());

  /**
   * 载入设置并返回本次取到的快照。
   *
   * **失败时向上抛出，并保留上一份有效快照**。降级策略由调用点决定：启动阶段可以吞掉
   * （设置不是启动的硬依赖），而保存后的刷新失败必须让用户看见——在这里统一吞掉并清空，
   * 会让「保存成功但页面变空、时区退回浏览器」看起来像是保存本身出了问题。
   *
   * 返回值让调用方复用同一份结果刷新自己的视图，不必各自再发一次请求。
   */
  async load(): Promise<readonly SettingOutputDto[]> {
    const settings = await firstValueFrom(this.settingService.getSettings());
    this.settings.set(settings);
    return settings;
  }

  //#if (IncludeLocalization)
  /**
   * 当前界面语言。
   *
   * 读的是**运行时的活动语言**，而不是语言设置那一项。两者不是同一个东西：设置是这份
   * 偏好的持久化，活动语言才是此刻界面正在用的那个，而它还有另外两个来源——未登录时的
   * 设备选择、以及都没有时的系统语言。
   *
   * 从设置去推导等于凭快照再算一份"当前语言"：切语言后文案立刻变了、日期却要等快照回来
   * 才跟上，写回失败时更是一直分叉，访客在登录页切的语言则根本推不出来。
   */
  //#else
  /**
   * 当前界面语言：本项目是单语言的，直接取浏览器语言。
   *
   * 取不到（浏览器不报语言）时返回 `undefined`，由 `appDate` 回落到固定写法。
   */
  //#endif
  private resolveLocale(): string | undefined {
    //#if (IncludeLocalization)
    return this.languageService.activeLang();
    //#else
    try {
      return navigator.language || undefined;
    } catch {
      return undefined;
    }
    //#endif
  }

  clear(): void {
    this.settings.set([]);
  }

  /** 按回落顺序取生效值：用户覆盖 → 系统默认 → 代码默认。 */
  valueOf(name: string): string | undefined {
    const setting = this.settings().find((s) => s.name === name);
    return setting?.userValue ?? setting?.tenantValue ?? setting?.defaultValue ?? undefined;
  }
}
