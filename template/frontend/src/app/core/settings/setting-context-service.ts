import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { SettingService } from './setting-service';
import { SETTINGS } from './setting.constants';
import { SettingOutputDto } from './setting.dto';

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
  private readonly settings = signal<readonly SettingOutputDto[]>([]);

  /**
   * 展示时区（IANA 名）；未设置时为 `undefined`，日期按浏览器本地时区展示。
   *
   * 时间一律以 UTC 存储，只在展示时换算——这也是它必须有个统一读取点的原因：
   * 每处各自 new Date() 会让同一时刻在不同页面显示成不同时间。
   */
  readonly timeZone = computed(() => this.valueOf(SETTINGS.display.timeZone));

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

  clear(): void {
    this.settings.set([]);
  }

  /** 按回落顺序取生效值：用户覆盖 → 系统默认 → 代码默认。 */
  valueOf(name: string): string | undefined {
    const setting = this.settings().find((s) => s.name === name);
    return setting?.userValue ?? setting?.tenantValue ?? setting?.defaultValue ?? undefined;
  }
}
