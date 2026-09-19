import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';
import { catchError, EMPTY, finalize } from 'rxjs';

import { applicationErrorMessage } from '../../core/errors/application-http-error';
import { SettingService } from '../../core/settings/setting-service';
import { SettingOutputDto } from '../../core/settings/setting.dto';

/**
 * 设置作用域。
 *
 * `account` 写当前用户自己的偏好，`system` 写当前上下文的默认值——登录后租户已经确定，
 * 这里写的就是「本租户（或宿主）下所有人的默认值」，不是在管理别的租户；
 * 给指定租户配置属于租户管理的事。两者的写入授权也不同，因此分开。
 */
export type SettingScope = 'account' | 'system';

/** 一个分组及归到它下面的设置项；分组标识、顺序与显示名都来自后端。 */
export interface SettingGroup {
  key: string;
  label: string;
  settings: SettingOutputDto[];
}

/** 本作用域下能看到的设置。 */
export function settingsInScope(
  settings: readonly SettingOutputDto[],
  scope: SettingScope,
): SettingOutputDto[] {
  // 进程级设置（日志级别之类）两个层级标记都是 false，只按 allowsTenantScope 过滤会把它们全漏掉——
  // 后端只在宿主上下文下发它们，所以能看到就代表能改。
  return scope === 'account'
    ? settings.filter((s) => s.allowsUserScope)
    : settings.filter((s) => s.allowsTenantScope || s.allowsHostScope);
}

/**
 * 按分组切分，分组标识与顺序都来自后端。
 *
 * 分类不在前端另列一份：漏登记一项设置的后果是它从界面上消失，而这既不报错也查不出来。
 * 后端已经把未分组的归入 `Other`，所以这里不需要再兜一次底。
 */
export function groupSettings(settings: readonly SettingOutputDto[]): SettingGroup[] {
  const byKey = new Map<string, SettingGroup>();
  for (const setting of settings) {
    const group = byKey.get(setting.group) ?? {
      key: setting.group,
      label: setting.groupDisplayName || setting.group,
      settings: [],
    };
    group.settings.push(setting);
    byKey.set(setting.group, group);
  }

  return [...byKey.values()];
}

/** 分组标识在 URL 里的写法：`RegistrationSecurity` → `registration-security`。 */
export function groupPath(key: string): string {
  return key.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();
}

/**
 * 一个设置页（个人设置或系统设置）共用的设置快照。
 *
 * 由设置页外壳提供、各面板注入：外壳要用它决定有哪些面板，面板要用它渲染与写入，
 * 各自请求一次既浪费，也会在其中一次失败时让导航和内容对不上。
 */
@Injectable()
export class SettingsPageState {
  private readonly settingService = inject(SettingService);
  private readonly destroyRef = inject(DestroyRef);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  readonly settings = signal<SettingOutputDto[]>([]);
  readonly loading = signal(false);
  /** 至少成功取到过一次。外壳据此决定何时把空地址导向第一个面板。 */
  readonly loaded = signal(false);

  constructor() {
    this.load();
    //#if (IncludeLocalization)

    // 设置项与分组的显示名由**后端**按请求语言本地化。切换语言只重绘视图没用——手里那份 DTO
    // 仍是旧语言文案，必须重新取一次让服务端按新语言渲染。
    //
    // langChanges$ 在订阅那一刻也会发出**当前**语言（背后是 BehaviorSubject），
    // 所以要跟上一次见到的语言比一下，否则每次进页面都白发一次请求。
    let seenLang = this.transloco.getActiveLang();
    this.transloco.langChanges$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((lang) => {
      if (lang === seenLang) {
        return;
      }

      seenLang = lang;
      this.load();
    });
    //#endif
  }

  load(): void {
    this.loading.set(true);
    this.settingService
      .getSettings()
      .pipe(
        catchError((error: unknown) => {
          toast.error(applicationErrorMessage(error));
          return EMPTY;
        }),
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((settings) => {
        this.settings.set(settings);
        this.loaded.set(true);
      });
  }
}
