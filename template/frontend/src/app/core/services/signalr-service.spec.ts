import { TestBed } from '@angular/core/testing';
import * as signalR from '@microsoft/signalr';

import { SignalRService } from './signalr-service';

/**
 * 连接生命周期：两个 Hub 要么都连上，要么一条都不留。
 *
 * 这里只替换 HubConnectionBuilder，不去 mock 网络：要锁住的是本服务对"部分失败"
 * 与"重复调用"的处理，而不是 SignalR 客户端自身的行为。
 */
describe('SignalRService 连接生命周期', () => {
  let service: SignalRService;
  let built: FakeConnection[];
  let failing: Set<string>;
  let deferStart = false;

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
    readonly invocations: { method: string; args: unknown[] }[] = [];

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

  function connectionsFor(fragment: string): FakeConnection[] {
    return built.filter((connection) => connection.url.includes(fragment));
  }

  afterEach(() => {
    // 兜底：正常路径由各用例的 finally 释放。afterEach 只在测试体提前抛出时生效——
    // 断言失败后若还有 await 卡在未释放的 gate 上，失败会退化成 Jasmine 超时。
    built.forEach((connection) => {
      connection.completeStart();
      connection.releaseInvoke();
    });
  });

  beforeEach(() => {
    built = [];
    failing = new Set<string>();
    deferStart = false;

    spyOn(console, 'error');

    // 只替换 build，让真正的 builder 负责链式调用；URL 从 withUrl 的调用记录里取。
    // 不去 spy 类导出本身：那要求 fake 与构造签名兼容，类型上通不过，
    // 而且会把"构造 builder"这件事也一并接管，测试就开始验证 SignalR 客户端而不是本服务。
    const withUrl = spyOn(signalR.HubConnectionBuilder.prototype, 'withUrl').and.callThrough();
    spyOn(signalR.HubConnectionBuilder.prototype, 'build').and.callFake(() => {
      const url = withUrl.calls.mostRecent().args[0];
      const connection = new FakeConnection(url);
      built.push(connection);

      return connection as unknown as signalR.HubConnection;
    });

    TestBed.configureTestingModule({});
    service = TestBed.inject(SignalRService);
  });

  it('两个 Hub 都连上时进入已连接状态', async () => {
    await service.connect();

    expect(built.length).toBe(2);
    expect(service.isConnected()).toBeTrue();
  });

  it('一个 Hub 失败时停掉本轮已连上的另一个，不留活连接', async () => {
    failing.add('/hubs/realtime');

    await service.connect();

    expect(service.isConnected()).toBeFalse();

    // 通知 Hub 已经 start 成功，必须被回滚掉——留着它就会继续往同一个 signal 里推。
    const notification = connectionsFor('/hubs/notifications')[0];
    expect(notification.startCount).toBe(1);
    expect(notification.stopCount).toBe(1);
  });

  it('并发调用复用同一次连接过程，不会各建一套', async () => {
    await Promise.all([service.connect(), service.connect(), service.connect()]);

    expect(built.length).toBe(2);
  });

  it('已经连上之后再次调用直接返回，不重建也不泄漏', async () => {
    await service.connect();
    await service.connect();

    // 只去重"进行中"的调用是不够的：串行第二次会新建一对并覆盖字段引用，
    // 旧的两条连同 handler 继续往同一个 signal 里推。通知组件重挂载就会走到这里。
    expect(built.length).toBe(2);
    expect(built.every((connection) => connection.stopCount === 0)).toBeTrue();
    expect(service.isConnected()).toBeTrue();
  });

  it('手上的连接已经彻底断开时，再次调用会重建', async () => {
    await service.connect();
    built.forEach((connection) => {
      connection.state = signalR.HubConnectionState.Disconnected;
    });

    await service.connect();

    // 自动重连耗尽后一味早退，会把应用永久留在断线状态。
    expect(built.length).toBe(4);
    expect(service.isConnected()).toBeTrue();
  });

  it('失败之后可以重试，且不与上一轮的连接叠加', async () => {
    failing.add('/hubs/realtime');
    await service.connect();
    expect(service.isConnected()).toBeFalse();

    failing.clear();
    await service.connect();

    expect(service.isConnected()).toBeTrue();

    // 上一轮的两条都已停掉，本轮的两条各自只 start 一次。
    expect(built.filter((connection) => connection.stopCount === 0).length).toBe(2);
    expect(built.every((connection) => connection.startCount === 1)).toBeTrue();
  });

  it('业务 Hub 单独掉线时不再报告已连接', async () => {
    await service.connect();
    expect(service.isConnected()).toBeTrue();

    // 只跟踪通知 Hub 的话，这里会一直是 true——界面显示"实时已连接"，实际一半没了。
    connectionsFor('/hubs/realtime')[0].drop();
    expect(service.isConnected()).toBeFalse();

    connectionsFor('/hubs/realtime')[0].dropAndRecover();
    expect(service.isConnected()).toBeTrue();
  });

  it('两个 Hub 交错恢复时，要等最后一个回来才算已连接', async () => {
    await service.connect();

    const notification = connectionsFor('/hubs/notifications')[0];
    const business = connectionsFor('/hubs/realtime')[0];

    notification.drop();
    business.drop();
    expect(service.isConnected()).toBeFalse();

    // 通知 Hub 先恢复：此时业务 Hub 还没回来，不能因为它上报成功就整体置真。
    notification.dropAndRecover();
    expect(service.isConnected()).toBeFalse();

    business.dropAndRecover();
    expect(service.isConnected()).toBeTrue();
  });

  it('断开后可以重新连接', async () => {
    await service.connect();
    await service.disconnect();

    expect(service.isConnected()).toBeFalse();
    expect(built.every((connection) => connection.stopCount === 1)).toBeTrue();

    await service.connect();

    expect(service.isConnected()).toBeTrue();
    expect(built.length).toBe(4);
  });

  it('主体切换后不复用上一个人的连接，也不残留他的通知', async () => {
    await service.connect();
    service.notifications.set([
      { id: 'n1', title: 'A 的通知', type: 'info', isRead: false, creationTime: '2026-01-01' },
    ]);
    await service.subscribeResource('order-1');

    await service.reset();

    // SignalR 的 principal 在握手时定死：不断开就换人登录，下一个用户会复用
    // 上一个人的活连接，以对方的身份继续收消息。
    expect(built.every((connection) => connection.stopCount === 1)).toBeTrue();
    expect(service.notifications()).toEqual([]);
    expect(service.lastResourceEvent()).toBeNull();
    expect(service.isConnected()).toBeFalse();

    await service.connect();

    // 新主体拿到的是新建的两条，不是上一个人的。
    expect(built.length).toBe(4);
    expect(built.slice(2).every((connection) => connection.stopCount === 0)).toBeTrue();
  });

  it('连接进行中发生主体切换时，那对连接不会留给下一个人', async () => {
    const connecting = service.connect();
    await service.reset();
    await connecting;

    // await 回来的连接握的是上一个身份；写进字段就成了没人再管、却仍在收推送的孤儿。
    expect(service.isConnected()).toBeFalse();
    expect(built.every((connection) => connection.stopCount >= 1)).toBeTrue();
  });

  it('reset 窗口内到达的旧 Hub 推送不写进新主体的列表', async () => {
    await service.connect();
    const notificationHub = connectionsFor('/hubs/notifications')[0];
    const push = notificationHub.handlers.get('NotificationReceived')!;

    await service.reset();

    // stop() 是异步的，在它完成之前仍可能收到上一个主体的推送。
    push({ id: 'n1', title: 'A 的推送', type: 'info', isRead: false, creationTime: '2026-01-01' });

    expect(service.notifications()).toEqual([]);
  });

  it('reset 之后完成的订阅调用不会在下一个主体的连接上重新订阅', async () => {
    await service.connect();

    const pending = service.subscribeResource('order-1');
    await service.reset();
    await pending;

    await service.connect();
    const business = connectionsFor('/hubs/realtime')[1];
    business.dropAndRecover();
    await Promise.resolve();

    // 断言真实后果而不是内部集合：后端订阅授权默认关闭，回填的 key 一旦留下，
    // 重连时会被真的重新订阅到下一个用户名下。
    expect(business.invocations).toEqual([]);
  });

  it('旧主体的连接过程未收尾时，新主体的 connect 仍会为自己建立连接', async () => {
    const stale = service.connect();
    await service.reset();

    const fresh = service.connect();
    await Promise.all([stale, fresh]);

    // 直接复用上一个主体的 Promise，会让本主体拿到"正常返回但什么都没连上"，
    // 在组件重挂载前一直没有实时通知。
    expect(service.isConnected()).toBeTrue();
  });

  it('reset 之后恢复执行的旧连接请求不再建连', async () => {
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
    expect(service.isConnected()).toBeFalse();
  });

  it('start 尚未完成时发生主体切换，旧连接的推送与事件都不写状态', async () => {
    deferStart = true;

    // 这一轮 connect 发起时仍是当前主体，因此连接会被建出来并写进字段；
    // 切换发生在 start() 完成之前——身份判据正是为这个窗口存在的。
    const pending = service.connect();
    try {
      await Promise.resolve();
      await Promise.resolve();

      const notificationHub = connectionsFor('/hubs/notifications')[0];
      const businessHub = connectionsFor('/hubs/realtime')[0];
      expect(notificationHub).withContext('连接应当已经建出并写入字段').toBeDefined();

      service.registerResourceEvent('OrderChanged');
      await service.reset();

      notificationHub.handlers.get('NotificationReceived')?.({
        id: 'a-1',
        title: 'A 的推送',
        type: 'info',
        isRead: false,
        creationTime: '2026-01-01',
      });
      businessHub.handlers.get('OrderChanged')?.({ id: 'order-1' });

      expect(service.notifications()).toEqual([]);
      expect(service.lastResourceEvent()).toBeNull();
    } finally {
      built.forEach((connection) => connection.completeStart());
    }

    await pending;
    expect(service.isConnected()).toBeFalse();
  });

  it('重连重订阅卡在某一轮时切换主体，不会继续订阅下一个人的资源', async () => {
    await service.connect();
    await service.subscribeResource('a-order');

    const staleBusiness = connectionsFor('/hubs/realtime')[0];
    staleBusiness.invocations.length = 0;
    staleBusiness.gateInvoke = true;

    // 重连回调进入循环并卡在 A 的第一次 Subscribe 上。
    const reconnected = staleBusiness.triggerReconnected();
    await Promise.resolve();
    expect(staleBusiness.invocations.length)
      .withContext('重连回调应当已经发出第一次 Subscribe')
      .toBe(1);

    // 就在这一轮未完成时切换主体，并让新主体订阅自己的资源。
    await service.reset();
    await service.connect();
    await service.subscribeResource('b-order');

    staleBusiness.releaseInvoke();
    await reconnected;

    // 跨 await 迭代活集合时，旧回调的迭代器会读到新主体刚加入的 key，
    // 并在上一个人的连接上把它订阅一遍。
    expect(staleBusiness.invocations.map((call) => call.args[0])).not.toContain('b-order');
  });

  it('stop 抛错也要清空引用，否则下一次连接会把泄漏的连接留在后面', async () => {
    await service.connect();
    built.forEach((connection) => {
      spyOn(connection, 'stop').and.rejectWith(new Error('stop failed'));
    });

    await service.disconnect();
    await service.connect();

    // 引用已清空，新一轮正常建立两条。
    expect(built.length).toBe(4);
    expect(service.isConnected()).toBeTrue();
  });
});
