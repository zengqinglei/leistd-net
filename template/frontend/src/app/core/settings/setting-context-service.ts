import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { SettingService } from './setting-service';
import { SettingOutputDto } from './setting.dto';

/**
 * 本次会话生效的设置值：启动时加载一次放进 signal，设置页改完调用 {@link load} 刷新；
 * 未登录时为空，读取方回落到自己的默认行为。账户偏好（语言、时区、外观）不在这里，
 * 见 `AccountPreferencesService`。
 */
@Injectable({ providedIn: 'root' })
export class SettingContextService {
  private readonly settingService = inject(SettingService);
  private readonly settings = signal<readonly SettingOutputDto[]>([]);
  /** 每次载入与清空都换一代：只有最新一代的响应能写快照，晚到的旧响应与清空前发出的请求一律作废。 */
  private generation = 0;

  /** 当前快照（只读）。 */
  readonly snapshot = this.settings.asReadonly();

  /**
   * 建立范围时静默读取，失败则使用默认值。保存后的刷新保留有效快照，失败交给调用方。
   */
  async load(mode: 'entry' | 'refresh' = 'refresh'): Promise<readonly SettingOutputDto[]> {
    const generation = ++this.generation;
    const entering = mode === 'entry';
    try {
      const settings = await firstValueFrom(this.settingService.getSettings(entering));
      if (generation === this.generation) this.settings.set(settings);
    } catch (error) {
      if (!entering) throw error;
      if (generation === this.generation) this.settings.set([]);
    }
    return this.settings();
  }

  clear(): void {
    this.generation++;
    this.settings.set([]);
  }

  /** 按回落顺序取生效值：用户覆盖 → 系统默认 → 代码默认。 */
  valueOf(name: string): string | undefined {
    const setting = this.settings().find((s) => s.name === name);
    return setting?.userValue ?? setting?.tenantValue ?? setting?.defaultValue ?? undefined;
  }
}
