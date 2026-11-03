import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  Event,
  NavigationCancel,
  NavigationCancellationCode,
  NavigationEnd,
  NavigationError,
  NavigationSkipped,
  NavigationStart,
  provideRouter,
  Router,
} from '@angular/router';
import { Subject } from 'rxjs';

import { NavigationProgressService } from './navigation-progress-service';

describe('NavigationProgressService', () => {
  let events: Subject<Event>;
  let service: NavigationProgressService;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    events = new Subject<Event>();
    TestBed.configureTestingModule({ providers: [{ provide: Router, useValue: { events } }] });
    service = TestBed.inject(NavigationProgressService);
  });

  afterEach(() => vi.useRealTimers());

  it('delays all work for 300ms and hides immediately on the last release', () => {
    const release = service.begin();
    expect(service.visible()).toBe(false);
    vi.advanceTimersByTime(299);
    expect(service.visible()).toBe(false);
    vi.advanceTimersByTime(1);
    expect(service.visible()).toBe(true);
    release();
    expect(service.visible()).toBe(false);
  });

  it('cancels a fast operation and starts a fresh delay for the next wave', () => {
    const first = service.begin();
    vi.advanceTimersByTime(200);
    first();
    vi.advanceTimersByTime(200);
    expect(service.visible()).toBe(false);
    const second = service.begin();
    vi.advanceTimersByTime(299);
    expect(service.visible()).toBe(false);
    vi.advanceTimersByTime(1);
    expect(service.visible()).toBe(true);
    second();
  });

  it('makes each release idempotent without consuming another operation', () => {
    const first = service.begin();
    const second = service.begin();
    vi.advanceTimersByTime(300);
    first();
    first();
    expect(service.visible()).toBe(true);
    second();
    second();
    expect(service.visible()).toBe(false);
    const next = service.begin();
    vi.advanceTimersByTime(300);
    expect(service.visible()).toBe(true);
    next();
  });

  it('keeps manual work visible after navigation ends', () => {
    events.next(new NavigationStart(1, '/first'));
    const release = service.begin();
    vi.advanceTimersByTime(300);
    events.next(new NavigationEnd(1, '/first', '/first'));
    expect(service.visible()).toBe(true);
    release();
    expect(service.visible()).toBe(false);
  });

  it('keeps navigation visible after manual work ends', () => {
    const release = service.begin();
    events.next(new NavigationStart(1, '/first'));
    vi.advanceTimersByTime(300);
    release();
    expect(service.visible()).toBe(true);
    events.next(new NavigationEnd(1, '/first', '/first'));
    expect(service.visible()).toBe(false);
  });

  // 路由先发被取代导航的取消、再发新导航的开始（见下方真实路由用例），中间不能断开也不能重新计时。
  it.each([
    ['superseded', NavigationCancellationCode.SupersededByNewNavigation],
    ['redirected', NavigationCancellationCode.Redirect],
  ] as const)('holds one navigation slot across a %s navigation', (_name, code) => {
    events.next(new NavigationStart(1, '/first'));
    vi.advanceTimersByTime(200);
    events.next(new NavigationCancel(1, '/first', 'Replaced', code));
    events.next(new NavigationStart(2, '/second'));
    vi.advanceTimersByTime(100);
    expect(service.visible()).toBe(true);
    events.next(new NavigationEnd(2, '/second', '/second'));
    expect(service.visible()).toBe(false);
  });

  it.each([
    ['cancel', new NavigationCancel(1, '/first', 'Cancelled')],
    ['error', new NavigationError(1, '/first', new Error('Failed'))],
    ['skip', new NavigationSkipped(1, '/first', 'Skipped')],
  ] as const)('releases navigation on %s', (_name, terminal) => {
    events.next(new NavigationStart(1, '/first'));
    vi.advanceTimersByTime(300);
    expect(service.visible()).toBe(true);
    events.next(terminal);
    expect(service.visible()).toBe(false);
  });

  it('cleans up its delay and router subscription on destruction', () => {
    service.begin();
    TestBed.resetTestingModule();
    vi.advanceTimersByTime(300);
    events.next(new NavigationStart(1, '/first'));
    vi.advanceTimersByTime(300);
    expect(service.visible()).toBe(false);
  });
});

describe('NavigationProgressService with the real router', () => {
  afterEach(() => vi.useRealTimers());

  it('stays visible when a slow navigation is superseded', async () => {
    const gates = { first: new Subject<boolean>(), second: new Subject<boolean>() };
    const resolvers: Record<string, () => void> = {};
    const entered = {
      first: new Promise<void>((resolve) => (resolvers['first'] = resolve)),
      second: new Promise<void>((resolve) => (resolvers['second'] = resolve)),
    };
    const slow = (name: keyof typeof gates) => () => {
      resolvers[name]();
      return gates[name];
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'first', component: Page, resolve: { ready: slow('first') } },
          { path: 'second', component: Page, resolve: { ready: slow('second') } },
        ]),
      ],
    });
    const service = TestBed.inject(NavigationProgressService);
    const router = TestBed.inject(Router);
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });

    void router.navigateByUrl('/first');
    await entered.first;
    vi.advanceTimersByTime(300);
    expect(service.visible()).toBe(true);

    const second = router.navigateByUrl('/second');
    await entered.second;
    // 不推进时钟：若取消事件释放了占用，这里会回到隐藏并重新等 300ms。
    expect(service.visible()).toBe(true);

    gates.second.next(true);
    gates.second.complete();
    expect(await second).toBe(true);
    expect(service.visible()).toBe(false);
  });
});

@Component({ template: '' })
class Page {}
