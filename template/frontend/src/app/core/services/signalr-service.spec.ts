import { TestBed } from '@angular/core/testing';
import * as signalR from '@microsoft/signalr';

import { SignalRService } from './signalr-service';

import type { Mock } from 'vitest';

/**
 * 连接生命周期：通知与业务事件共用一条连接，主体切换、失败与重连时不泄漏、不串人。
 *
 * 这里只替换 HubConnectionBuilder，不去 mock 网络：要锁住的是本服务对"部分失败"
 * 与"重复调用"的处理，而不是 SignalR 客户端自身的行为。
 */
describe('SignalRService connection lifecycle', () => {
  let service: SignalRService;
  let built: FakeConnection[];
  let failing: Set<string>;
  let deferStart = false;
  let withUrl: Mock;

  class FakeConnection {
    startCount = 0;
    stopCount = 0;
    state: signalR.HubConnectionState = signalR.HubConnectionState.Disconnected;
    readonly handlers = new Map<string, (...args: unknown[]) => void>();

    // 保存自动重连回调，测试据此模拟"掉线—恢复"时序。
    private reconnecting: (() => void) | null = null;
    private reconnected: (() => void | Promise<void>) | null = null;

    constructor(readonly url: string) {}

    on(name: string, handler: (...args: unknown[]) => void): void {
      this.handlers.set(name, handler);
    }

    onreconnecting(handler: () => void): void {
      this.reconnecting = handler;
    }

    onreconnected(handler: () => void | Promise<void>): void {
      this.reconnected = handler;
    }

    // 本用例集不模拟"连接彻底关闭"，注册即可。
    readonly onclose = (): void => undefined;

    /** 只触发 onreconnected，并把它的 Promise 交回给测试。 */
    triggerReconnected(): Promise<void> {
      this.state = signalR.HubConnectionState.Connected;
      return Promise.resolve(this.reconnected?.()).then(() => undefined);
    }

    /** 模拟自动重连：掉线 → 恢复。 */
    dropAndRecover(): void {
      this.state = signalR.HubConnectionState.Reconnecting;
      this.reconnecting?.();
      this.state = signalR.HubConnectionState.Connected;
      this.reconnected?.();
    }

    /** 模拟掉线但尚未恢复。 */
    drop(): void {
      this.state = signalR.HubConnectionState.Reconnecting;
      this.reconnecting?.();
    }

    /** 置为 true 时 start() 挂起，直到 completeStart() 被调用。 */
    private startGate: (() => void) | null = null;

    async start(): Promise<void> {
      this.startCount++;

      // 立即完成的 start 构造不出"连接已写入字段、但尚未通过最终代际校验"的窗口，
      // 而那正是旧请求的推送可以穿透的地方。
      if (deferStart) {
        await new Promise<void>((resolve) => {
          this.startGate = resolve;
        });
      }

      if ([...failing].some((fragment) => this.url.includes(fragment))) {
        throw new Error(`start failed: ${this.url}`);
      }
      this.state = signalR.HubConnectionState.Connected;
    }

    completeStart(): void {
      this.startGate?.();
      this.startGate = null;
    }

    async stop(): Promise<void> {
      this.stopCount++;
      this.state = signalR.HubConnectionState.Disconnected;
    }

    // 订阅走 invoke：缺了它，subscribeResource 会直接抛错进 catch，
    // 相关用例在改坏实现时也照样绿——那种"通过"什么都证明不了。
    readonly invocations: {
      method: string;
      args: unknown[];
    }[] = [];

    /** 置为 true 时 invoke() 挂起，直到 releaseInvoke() 被调用。 */
    gateInvoke = false;
    private invokeGates: (() => void)[] = [];

    invoke(method: string, ...args: unknown[]): Promise<void> {
      this.invocations.push({ method, args });

      // 立即完成的 invoke 让重订阅循环在一个微任务里跑完，
      // 制造不出"循环卡在某一轮时主体切换"的竞态。
      if (!this.gateInvoke) {
        return Promise.resolve();
      }

      return new Promise<void>((resolve) => this.invokeGates.push(resolve));
    }

    releaseInvoke(): void {
      const gates = this.invokeGates;
      this.invokeGates = [];
      gates.forEach((resolve) => resolve());
    }
  }

  afterEach(() => {
    // 兜底：正常路径由各用例的 finally 释放。afterEach 只在测试体提前抛出时生效——
    // 断言失败后若还有 await 卡在未释放的 gate 上，失败会退化成用例超时。
    built.forEach((connection) => {
      connection.completeStart();
      connection.releaseInvoke();
    });
  });

  beforeEach(() => {
    built = [];
    failing = new Set<string>();
    deferStart = false;

    vi.spyOn(console, 'error').mockReturnValue(undefined);

    // 只替换 build，让真正的 builder 负责链式调用；URL 从 withUrl 的调用记录里取。
    // 不去 spy 类导出本身：那要求 fake 与构造签名兼容，类型上通不过，
    // 而且会把"构造 builder"这件事也一并接管，测试就开始验证 SignalR 客户端而不是本服务。
    withUrl = vi.spyOn(signalR.HubConnectionBuilder.prototype, 'withUrl');
    vi.spyOn(signalR.HubConnectionBuilder.prototype, 'build').mockImplementation(() => {
      const url = vi.mocked(withUrl).mock.lastCall?.[0];
      if (!url) {
        throw new Error('withUrl must be called before build');
      }
      const connection = new FakeConnection(url);
      built.push(connection);

      return connection as unknown as signalR.HubConnection;
    });

    service = TestBed.inject(SignalRService);
  });

  it('enters the connected state after connecting to the real-time hub', async () => {
    await service.connect();

    expect(built.length).toBe(1);
    expect(built[0].url).toContain(SignalRService.hubPath);
    expect(service.isConnected()).toBe(true);
  });

  it('leaves no live connection when connecting fails', async () => {
    failing.add(SignalRService.hubPath);

    await service.connect();

    expect(service.isConnected()).toBe(false);
    expect(built[0].stopCount).toBe(1);
  });

  it('concurrent calls share one connection attempt instead of each creating a connection', async () => {
    await Promise.all([service.connect(), service.connect(), service.connect()]);

    expect(built.length).toBe(1);
  });

  it('returns immediately when already connected, without rebuilding or leaking', async () => {
    await service.connect();
    await service.connect();

    // 只去重"进行中"的调用是不够的：串行第二次会新建一条并覆盖字段引用，
    // 旧连接连同 handler 继续往同一个 signal 里推。通知组件重挂载就会走到这里。
    expect(built.length).toBe(1);
    expect(built[0].stopCount).toBe(0);
    expect(service.isConnected()).toBe(true);
  });

  it('rebuilds when the held connection is fully disconnected', async () => {
    await service.connect();
    built[0].state = signalR.HubConnectionState.Disconnected;

    await service.connect();

    // 自动重连耗尽后一味早退，会把应用永久留在断线状态。
    expect(built.length).toBe(2);
    expect(service.isConnected()).toBe(true);
  });

  it('can retry after a failure without stacking on the previous connection', async () => {
    failing.add(SignalRService.hubPath);
    await service.connect();
    expect(service.isConnected()).toBe(false);

    failing.clear();
    await service.connect();

    expect(service.isConnected()).toBe(true);
    expect(built.filter((connection) => connection.stopCount === 0).length).toBe(1);
    expect(built.every((connection) => connection.startCount === 1)).toBe(true);
  });

  it('does not report connected while dropped and reports it again after recovery', async () => {
    await service.connect();

    built[0].drop();
    expect(service.isConnected()).toBe(false);

    built[0].dropAndRecover();
    expect(service.isConnected()).toBe(true);
  });

  it('notifications and resource events on one connection each trigger only their own handler', async () => {
    service.registerResourceEvent('OrderChanged');
    await service.connect();
    const connection = built[0];

    connection.handlers.get(SignalRService.notificationReceived)!({
      id: 'n1',
      title: '通知',
      type: 'info',
      isRead: false,
      creationTime: '2026-01-01',
    });
    expect(service.notifications().length).toBe(1);
    expect(service.lastResourceEvent()).toBeNull();

    connection.handlers.get('OrderChanged')!({ id: 'order-1' });
    expect(service.notifications().length).toBe(1);
    expect(service.lastResourceEvent()).toEqual({
      eventName: 'OrderChanged',
      payload: { id: 'order-1' },
    });
  });

  it('resubscribes subscribed resources on the same connection after reconnecting', async () => {
    await service.connect();
    await service.subscribeResource('order-1');
    const connection = built[0];
    connection.invocations.length = 0;

    await connection.triggerReconnected();

    expect(connection.invocations).toEqual([{ method: 'Subscribe', args: ['order-1'] }]);
  });

  it('can connect again after disconnecting', async () => {
    await service.connect();
    await service.disconnect();

    expect(service.isConnected()).toBe(false);
    expect(built.every((connection) => connection.stopCount === 1)).toBe(true);

    await service.connect();

    expect(service.isConnected()).toBe(true);
    expect(built.length).toBe(2);
  });

  it('does not reuse the connection or keep the notifications of the previous principal after a principal switch', async () => {
    await service.connect();
    service.notifications.set([
      { id: 'n1', title: 'A 的通知', type: 'info', isRead: false, creationTime: '2026-01-01' },
    ]);
    await service.subscribeResource('order-1');

    await service.reset();

    // SignalR 的 principal 在握手时定死：不断开就换人登录，下一个用户会复用
    // 上一个人的活连接，以对方的身份继续收消息。
    expect(built.every((connection) => connection.stopCount === 1)).toBe(true);
    expect(service.notifications()).toEqual([]);
    expect(service.lastResourceEvent()).toBeNull();
    expect(service.isConnected()).toBe(false);

    await service.connect();

    // 新主体拿到的是新建的连接，不是上一个人的。
    expect(built.length).toBe(2);
    expect(built[1].stopCount).toBe(0);
  });

  it('does not hand an in-progress connection to the next principal after a switch', async () => {
    const connecting = service.connect();
    await service.reset();
    await connecting;

    // await 回来的连接握的是上一个身份；写进字段就成了没人再管、却仍在收推送的孤儿。
    expect(service.isConnected()).toBe(false);
    expect(built.every((connection) => connection.stopCount >= 1)).toBe(true);
  });

  it('does not write stale hub pushes arriving during reset into the list of the new principal', async () => {
    await service.connect();
    const push = built[0].handlers.get(SignalRService.notificationReceived)!;

    await service.reset();

    // stop() 是异步的，在它完成之前仍可能收到上一个主体的推送。
    push({ id: 'n1', title: 'A 的推送', type: 'info', isRead: false, creationTime: '2026-01-01' });

    expect(service.notifications()).toEqual([]);
  });

  it('does not resubscribe a subscribe call completing after reset on the connection of the next principal', async () => {
    await service.connect();

    const pending = service.subscribeResource('order-1');
    await service.reset();
    await pending;

    await service.connect();
    const business = built[1];
    business.dropAndRecover();
    await Promise.resolve();

    // 断言真实后果而不是内部集合：后端订阅授权默认关闭，回填的 key 一旦留下，
    // 重连时会被真的重新订阅到下一个用户名下。
    expect(business.invocations).toEqual([]);
  });

  it('connects for a new principal even while the attempt of the old one is unfinished', async () => {
    const stale = service.connect();
    await service.reset();

    const fresh = service.connect();
    await Promise.all([stale, fresh]);

    // 直接复用上一个主体的 Promise，会让本主体拿到"正常返回但什么都没连上"，
    // 在组件重挂载前一直没有实时通知。
    expect(service.isConnected()).toBe(true);
  });

  it('does not connect when a stale connect request resumes after reset', async () => {
    deferStart = true;

    // 旧请求在 await 处让出执行权，恢复时已经不是当前主体。
    const stale = service.connect();
    try {
      await service.reset();
      await Promise.resolve();
      await Promise.resolve();

      // 一条都不该建：连接一旦写进字段，身份比对就一律为真，
      // start() 期间收到的推送会直接落进下一个主体的界面。
      expect(built.length).toBe(0);
    } finally {
      // 回归时这里会有连接卡在未完成的 start 上，不释放就等成超时。
      built.forEach((connection) => connection.completeStart());
    }

    await stale;
    expect(service.isConnected()).toBe(false);
  });

  it('ignores pushes and events from the old connection when the principal switches before start completes', async () => {
    deferStart = true;

    // 这一轮 connect 发起时仍是当前主体，因此连接会被建出来并写进字段；
    // 切换发生在 start() 完成之前——身份判据正是为这个窗口存在的。
    const pending = service.connect();
    try {
      await Promise.resolve();
      await Promise.resolve();

      const connection = built[0];
      expect(connection, '连接应当已经建出并写入字段').toBeDefined();

      service.registerResourceEvent('OrderChanged');
      await service.reset();

      connection.handlers.get(SignalRService.notificationReceived)?.({
        id: 'a-1',
        title: 'A 的推送',
        type: 'info',
        isRead: false,
        creationTime: '2026-01-01',
      });
      connection.handlers.get('OrderChanged')?.({ id: 'order-1' });

      expect(service.notifications()).toEqual([]);
      expect(service.lastResourceEvent()).toBeNull();
    } finally {
      built.forEach((connection) => connection.completeStart());
    }

    await pending;
    expect(service.isConnected()).toBe(false);
  });

  it('does not subscribe resources of the next principal when switching while reconnect resubscription is stuck', async () => {
    await service.connect();
    await service.subscribeResource('a-order');

    const staleBusiness = built[0];
    staleBusiness.invocations.length = 0;
    staleBusiness.gateInvoke = true;

    // 重连回调进入循环并卡在 A 的第一次 Subscribe 上。
    const reconnected = staleBusiness.triggerReconnected();
    try {
      await Promise.resolve();
      expect(staleBusiness.invocations.length, '重连回调应当已经发出第一次 Subscribe').toBe(1);

      // 就在这一轮未完成时切换主体，并让新主体订阅自己的资源。
      await service.reset();
      await service.connect();
      await service.subscribeResource('b-order');
    } finally {
      // 先关掉拦截再释放：缺陷被重新引入时，循环会对 b-order 再发一次 invoke，
      // 只释放已排队的那次会让它继续挂住，用例最终以 5 秒超时收场——
      // 杀得死缺陷，但失败得又慢又看不出原因。
      staleBusiness.gateInvoke = false;
      staleBusiness.releaseInvoke();
    }

    await reconnected;

    // 跨 await 迭代活集合时，旧回调的迭代器会读到新主体刚加入的 key，
    // 并在上一个人的连接上把它订阅一遍。
    expect(staleBusiness.invocations.map((call) => call.args[0])).not.toContain('b-order');
  });

  // stop 抛错也要清空引用，否则下一次连接会把泄漏的连接留在后面。
  it('clears the reference even when stop throws', async () => {
    await service.connect();
    built.forEach((connection) => {
      vi.spyOn(connection, 'stop').mockRejectedValue(new Error('stop failed'));
    });

    await service.disconnect();
    await service.connect();

    // 引用已清空，新一轮正常建立一条。
    expect(built.length).toBe(2);
    expect(service.isConnected()).toBe(true);
  });
});
