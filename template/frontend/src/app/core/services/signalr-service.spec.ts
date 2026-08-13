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

  class FakeConnection {
    startCount = 0;
    stopCount = 0;
    readonly handlers = new Map<string, (...args: unknown[]) => void>();

    constructor(readonly url: string) {}

    on(name: string, handler: (...args: unknown[]) => void): void {
      this.handlers.set(name, handler);
    }

    // 自动重连回调这里不需要行为，只要能被注册。
    readonly onreconnecting = (): void => undefined;
    readonly onreconnected = (): void => undefined;

    async start(): Promise<void> {
      this.startCount++;
      if ([...failing].some((fragment) => this.url.includes(fragment))) {
        throw new Error(`start failed: ${this.url}`);
      }
    }

    async stop(): Promise<void> {
      this.stopCount++;
    }
  }

  function connectionsFor(fragment: string): FakeConnection[] {
    return built.filter((connection) => connection.url.includes(fragment));
  }

  beforeEach(() => {
    built = [];
    failing = new Set<string>();

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

  it('断开后可以重新连接', async () => {
    await service.connect();
    await service.disconnect();

    expect(service.isConnected()).toBeFalse();
    expect(built.every((connection) => connection.stopCount === 1)).toBeTrue();

    await service.connect();

    expect(service.isConnected()).toBeTrue();
    expect(built.length).toBe(4);
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
