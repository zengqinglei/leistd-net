import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideLoaderCircle, lucideWifiOff } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';

import { SignalRService } from '../../../core/services/signalr-service';
//#if (!IncludeLocalization)
import { englishText } from '../../../shared/utils/english-text';
//#endif

/** 重连持续多久才提示：服务的快速重试（0、1、2 秒）在这之前悄悄连回，短暂抖动不打扰用户。 */
export const RECONNECTING_NOTICE_DELAY_MS = 3000;

/** 连回后「已恢复」停留多久。 */
export const RESTORED_NOTICE_MS = 2000;

/** 倒计时的刷新间隔：比 1 秒短，秒数跳变不会晚出半拍。 */
const COUNTDOWN_TICK_MS = 250;

export type RealtimeNotice = 'reconnecting' | 'waiting' | 'retrying' | 'restored';

/**
 * 实时连接提示：悬浮在页头下边沿正中的胶囊，不占布局、不推开页面。连接正常（或还没请求连接）时不显示；
 * 重连持续 {@link RECONNECTING_NOTICE_DELAY_MS} 以上才出现，等待重试时给倒计时与「立即重试」，连回后显示
 * 「已恢复」{@link RESTORED_NOTICE_MS} 后消失。宿主零高度、常驻并带 `role="status"`：随内容一起插入的
 * live region 多数读屏不播报。倒计时逐秒变化，对读屏隐藏，另给一句稳定文案。挂载位置见
 * docs/standards/frontend-navigation.md。
 */
@Component({
  selector: 'app-realtime-status',
  // prettier-ignore
  imports: [
    NgIcon,
    HlmButton,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideCheck, lucideLoaderCircle, lucideWifiOff })],
  host: { class: 'relative z-40 block h-0', role: 'status' },
  templateUrl: './realtime-status.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RealtimeStatus {
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
  private readonly signalR = inject(SignalRService);
  private readonly now = signal(Date.now());
  private readonly reconnectingLongEnough = signal(false);
  private readonly restored = signal(false);

  /** 本轮断线里胶囊已经出现过：连回时才报告「已恢复」。 */
  private readonly shown = signal(false);

  /** 本轮断线里已经显示过倒计时：之后的建连显示为「重试中」，按钮保留并转圈。 */
  private readonly waited = signal(false);

  private readonly remainingMs = computed(() => {
    const at = this.signalR.nextRetryAt();
    return at === null ? 0 : at - this.now();
  });

  /** 倒计时秒数，至少 1：到 0 时已转为「重试中」。 */
  protected readonly seconds = computed(() => Math.max(1, Math.ceil(this.remainingMs() / 1000)));

  /** 当前要显示的提示，`null` 即不显示。 */
  protected readonly notice = computed<RealtimeNotice | null>(() => {
    if (this.restored()) {
      return 'restored';
    }
    if (this.signalR.connectionStatus() !== 'reconnecting' || !this.reconnectingLongEnough()) {
      return null;
    }
    if (this.remainingMs() > 0) {
      return 'waiting';
    }
    return this.waited() ? 'retrying' : 'reconnecting';
  });

  constructor() {
    // 重连持续够久才显示；每次进入 reconnecting 重新计时
    effect((onCleanup) => {
      if (this.signalR.connectionStatus() !== 'reconnecting') {
        this.reconnectingLongEnough.set(false);
        return;
      }

      const timer = setTimeout(
        () => this.reconnectingLongEnough.set(true),
        RECONNECTING_NOTICE_DELAY_MS,
      );
      onCleanup(() => clearTimeout(timer));
    });

    // 只在重连时走时钟；新的重试时刻到来时立刻对时，倒计时从整数秒开始
    effect((onCleanup) => {
      this.signalR.nextRetryAt();
      if (this.signalR.connectionStatus() !== 'reconnecting') {
        return;
      }

      this.now.set(Date.now());
      const timer = setInterval(() => this.now.set(Date.now()), COUNTDOWN_TICK_MS);
      onCleanup(() => clearInterval(timer));
    });

    effect(() => {
      const notice = this.notice();
      if (notice && notice !== 'restored') {
        this.shown.set(true);
      }
      if (notice === 'waiting') {
        this.waited.set(true);
      }
    });

    // 胶囊出现过才报告恢复；离开 reconnecting 即结束本轮
    effect((onCleanup) => {
      const status = this.signalR.connectionStatus();
      if (status === 'reconnecting') {
        return;
      }

      const shown = untracked(() => this.shown());
      this.shown.set(false);
      this.waited.set(false);
      if (status !== 'connected' || !shown) {
        this.restored.set(false);
        return;
      }

      this.restored.set(true);
      const timer = setTimeout(() => this.restored.set(false), RESTORED_NOTICE_MS);
      onCleanup(() => clearTimeout(timer));
    });
  }

  protected retryNow(): void {
    this.signalR.retryNow();
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'layout.realtime.reconnecting': 'Live updates interrupted. Reconnecting…',
  'layout.realtime.waiting': 'Live updates interrupted. Retrying in {{seconds}}s',
  'layout.realtime.waitingShort': 'Retrying in {{seconds}}s',
  'layout.realtime.retrying': 'Live updates interrupted. Retrying…',
  'layout.realtime.retryingShort': 'Retrying…',
  'layout.realtime.restored': 'Live updates restored',
  'layout.realtime.retryNow': 'Retry now',
  'layout.realtime.interrupted': 'Live updates interrupted. Retrying automatically.',
};
//#endif
