import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif

//#if (IncludeLocalization)
import enTexts from '../../../../../public/i18n/en.json';
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import {
  type RealtimeConnectionStatus,
  SignalRService,
} from '../../../core/services/signalr-service';
import { RealtimeStatus } from './realtime-status';

/**
 * 实时连接提示：正常时不显示，重连持续 3 秒以上才出现；等待重试给倒计时与「立即重试」，胶囊出现过才在连回时
 * 报告「已恢复」。假计时器推进本组件的显示延时与倒计时时钟。文案逐字写出、不从词条推导：多语言形态渲染
 * `public/i18n` 的英文词条。
 */
describe('RealtimeStatus', () => {
  let fixture: ComponentFixture<RealtimeStatus>;
  let status: ReturnType<typeof signal<RealtimeConnectionStatus>>;
  let nextRetryAt: ReturnType<typeof signal<number | null>>;
  let retryNow: ReturnType<typeof vi.fn<() => void>>;

  const host = () => fixture.nativeElement as HTMLElement;
  const notice = () => host().querySelector<HTMLElement>('[data-testid="realtime-status-notice"]');
  const visibleText = () =>
    notice()!.querySelector<HTMLElement>('span.truncate')!.textContent!.replace(/\s+/g, ' ').trim();
  const spokenText = () => notice()!.querySelector<HTMLElement>('.sr-only')!.textContent!.trim();
  const retryButton = () =>
    notice()!.querySelector<HTMLButtonElement>('[data-testid="realtime-retry"]');

  beforeEach(async () => {
    status = signal<RealtimeConnectionStatus>('connected');
    nextRetryAt = signal<number | null>(null);
    retryNow = vi.fn();
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        { provide: SignalRService, useValue: { connectionStatus: status, nextRetryAt, retryNow } },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });
    //#if (IncludeLocalization)
    TestBed.inject(TranslocoService).setTranslation(enTexts, 'en');
    //#endif
    fixture = TestBed.createComponent(RealtimeStatus);
    fixture.detectChanges();
    await fixture.whenStable();
    vi.useFakeTimers({
      toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'],
    });
  });

  afterEach(() => vi.useRealTimers());

  /** 推进假时钟后渲染一次：effect 在变更检测里执行，计时器回调写入的信号在下一次检测里生效。 */
  function advance(ms: number): void {
    vi.advanceTimersByTime(ms);
    fixture.detectChanges();
  }

  function set(next: RealtimeConnectionStatus, retryInMs?: number): void {
    nextRetryAt.set(retryInMs === undefined ? null : Date.now() + retryInMs);
    status.set(next);
    fixture.detectChanges();
  }

  it('shows nothing and takes no space while connected or not yet connecting', () => {
    expect(notice()).toBeNull();
    set('idle');
    expect(notice()).toBeNull();
    expect(host().getBoundingClientRect().height).toBe(0);
  });

  // 随内容一起插入的 live region 多数读屏不播报：状态区要在变化之前就在，胶囊插进同一个元素
  it('announces the notice inside a status region that exists before the change', () => {
    expect(host().getAttribute('role')).toBe('status');

    set('reconnecting');
    advance(3000);

    expect(host().contains(notice())).toBe(true);
    expect(notice()!.hasAttribute('role')).toBe(false);
  });

  it('floats over the page instead of pushing it down', () => {
    set('reconnecting', 18_000);
    advance(3000);

    expect(notice()).not.toBeNull();
    expect(host().getBoundingClientRect().height).toBe(0);
    expect(getComputedStyle(notice()!).position).toBe('absolute');
  });

  it('shows the reconnecting notice only after three seconds of reconnecting', () => {
    set('reconnecting');
    advance(2999);
    expect(notice()).toBeNull();

    advance(1);
    expect(notice()!.dataset['notice']).toBe('reconnecting');
    expect(visibleText()).toBe('Live updates interrupted. Reconnecting…');
    expect(retryButton()).toBeNull();
  });

  it('shows nothing, not even a restored notice, when a reconnect recovers within three seconds', () => {
    set('reconnecting');
    advance(2000);
    set('connected');

    advance(5000);
    expect(notice()).toBeNull();
  });

  it('restarts the delay when a new reconnect begins', () => {
    set('reconnecting');
    advance(2500);
    set('connected');
    set('reconnecting');

    advance(2500);
    expect(notice()).toBeNull();
    advance(500);
    expect(notice()).not.toBeNull();
  });

  it('counts down to the next retry and offers to retry now', () => {
    set('reconnecting', 20_000);
    advance(3000);

    expect(notice()!.dataset['notice']).toBe('waiting');
    expect(notice()!.querySelector('span.sm\\:inline')!.textContent!.trim()).toBe(
      'Live updates interrupted. Retrying in 17s',
    );
    expect(notice()!.querySelector('span.sm\\:hidden')!.textContent!.trim()).toBe(
      'Retrying in 17s',
    );

    advance(1000);
    expect(notice()!.querySelector('span.sm\\:hidden')!.textContent!.trim()).toBe(
      'Retrying in 16s',
    );

    retryButton()!.click();
    expect(retryNow).toHaveBeenCalledTimes(1);
  });

  // 倒计时逐秒变化：可见文案对读屏隐藏，读屏只读一句不随秒数变化的文案
  it('keeps the countdown away from screen readers behind one stable sentence', () => {
    set('reconnecting', 20_000);
    advance(3000);
    const spoken = spokenText();

    advance(2000);
    expect(spokenText()).toBe(spoken);
    expect(spoken).toBe('Live updates interrupted. Retrying automatically.');
    expect(notice()!.querySelector('span.truncate')!.getAttribute('aria-hidden')).toBe('true');
  });

  it('keeps the notice and spins the retry button while a retry is in flight', () => {
    set('reconnecting', 5000);
    advance(3000);
    expect(notice()!.dataset['notice']).toBe('waiting');

    // 倒计时走完：服务开始建连，下次重试时刻已过去
    advance(2000);
    expect(notice()!.dataset['notice']).toBe('retrying');
    expect(visibleText()).toContain('Retrying…');
    expect(retryButton()!.disabled).toBe(false);
    expect(retryButton()!.querySelector('ng-icon')).not.toBeNull();
    // 读屏文案与等待时相同，不因转入重试再播报一次
    expect(spokenText()).toBe('Live updates interrupted. Retrying automatically.');

    // 失败后进入下一轮等待
    nextRetryAt.set(Date.now() + 10_000);
    fixture.detectChanges();
    expect(notice()!.dataset['notice']).toBe('waiting');
  });

  it('reports restoration for two seconds after the notice was shown', () => {
    set('reconnecting', 10_000);
    advance(3000);
    set('connected');

    expect(notice()!.dataset['notice']).toBe('restored');
    expect(visibleText()).toBe('Live updates restored');
    expect(retryButton()).toBeNull();

    advance(1999);
    expect(notice()).not.toBeNull();
    advance(1);
    expect(notice()).toBeNull();
  });

  it('dismisses a restored notice when another outage begins', () => {
    set('reconnecting');
    advance(3000);
    set('connected');
    expect(notice()!.dataset['notice']).toBe('restored');

    advance(1000);
    set('reconnecting');
    expect(notice()).toBeNull();

    advance(2999);
    expect(notice()).toBeNull();
    advance(1);
    expect(notice()!.dataset['notice']).toBe('reconnecting');

    set('connected');
    expect(notice()!.dataset['notice']).toBe('restored');
    expect(visibleText()).toBe('Live updates restored');

    advance(1999);
    expect(notice()!.dataset['notice']).toBe('restored');
    advance(1);
    expect(notice()).toBeNull();
  });

  it('hides everything when the session ends while reconnecting', () => {
    set('reconnecting', 10_000);
    advance(3000);
    set('idle');

    expect(notice()).toBeNull();
    advance(5000);
    expect(notice()).toBeNull();
  });
});
