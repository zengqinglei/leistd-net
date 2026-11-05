import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw, lucideWifiOff } from '@ng-icons/lucide';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButton } from '@spartan-ng/helm/button';

import { SignalRService } from '../../../core/services/signalr-service';
//#if (!IncludeLocalization)
import { englishText } from '../../../shared/utils/english-text';
//#endif

/** 重连持续多久才提示：SignalR 首次重试立即发起，短暂抖动在这之前就恢复了。 */
export const RECONNECTING_NOTICE_DELAY_MS = 2000;

/**
 * 实时连接异常提示条：连接正常（或还没请求连接）时不占位；断开立即显示并给「重新连接」，重连持续
 * {@link RECONNECTING_NOTICE_DELAY_MS} 以上才显示。宿主常驻并带 `role="status"`，提示条插入其中：
 * 随内容一起插入的 live region 多数读屏不播报。挂载位置见 docs/standards/frontend-navigation.md。
 */
@Component({
  selector: 'app-realtime-status',
  // prettier-ignore
  imports: [
    NgIcon,
    ...HlmAlertImports,
    HlmButton,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideRefreshCw, lucideWifiOff })],
  host: { class: 'block', role: 'status' },
  templateUrl: './realtime-status.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RealtimeStatus {
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
  private readonly signalR = inject(SignalRService);
  private readonly reconnectingLongEnough = signal(false);

  /** 当前要显示的提示，`null` 即不显示。 */
  protected readonly notice = computed(() => {
    const status = this.signalR.connectionStatus();
    if (status === 'disconnected') {
      return 'disconnected';
    }
    return status === 'reconnecting' && this.reconnectingLongEnough() ? 'reconnecting' : null;
  });

  constructor() {
    effect((onCleanup) => {
      this.reconnectingLongEnough.set(false);
      if (this.signalR.connectionStatus() !== 'reconnecting') {
        return;
      }

      const timer = setTimeout(
        () => this.reconnectingLongEnough.set(true),
        RECONNECTING_NOTICE_DELAY_MS,
      );
      onCleanup(() => clearTimeout(timer));
    });
  }

  protected reconnect(): void {
    void this.signalR.connect();
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'layout.realtime.reconnecting': 'Live updates interrupted. Reconnecting…',
  'layout.realtime.disconnected': 'Live updates disconnected. This page may be out of date.',
  'layout.realtime.reconnect': 'Reconnect',
};
//#endif
