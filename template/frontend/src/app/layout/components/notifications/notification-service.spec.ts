import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { NotificationService } from './notification-service';
import { NotificationOutputDto, SignalRService } from '../../../core/services/signalr-service';

/**
 * 通知的连接闭环：init() 是 SignalR 连接的实际调用方，铃铛组件每次初始化都会走到这里。
 */
describe('NotificationService', () => {
  let service: NotificationService;
  let signalR: SignalRService;
  let httpMock: HttpTestingController;

  function notification(id: string, creationTime: string): NotificationOutputDto {
    return { id, title: id, type: 'info', isRead: false, creationTime };
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(NotificationService);
    signalR = TestBed.inject(SignalRService);
    httpMock = TestBed.inject(HttpTestingController);

    spyOn(console, 'error');
    spyOn(signalR, 'connect').and.resolveTo();
  });

  afterEach(() => httpMock.verify());

  it('重复初始化不会重复建立连接', async () => {
    const first = service.init();
    httpMock.expectOne((req) => req.url === '/api/v1/notifications').flush([]);
    await first;

    const second = service.init();
    httpMock.expectOne((req) => req.url === '/api/v1/notifications').flush([]);
    await second;

    // 幂等由 SignalRService.connect() 自身保证；这里锁住的是"调用方没有绕过它"，
    // 例如自己缓存一个 initialized 标志、或改成直接建连接。
    expect(signalR.connect).toHaveBeenCalledTimes(2);
    expect(signalR.connect).toHaveBeenCalledWith();
  });

  it('历史通知与已推送的通知合并后按时间倒序，且不重复', async () => {
    // 先有一条实时推送进来，随后才拉到历史列表。
    signalR.notifications.set([notification('pushed', '2026-01-02T00:00:00Z')]);

    const init = service.init();
    httpMock
      .expectOne((req) => req.url === '/api/v1/notifications')
      .flush([
        notification('old', '2026-01-01T00:00:00Z'),
        notification('pushed', '2026-01-02T00:00:00Z'),
      ]);
    await init;

    // 推送那条已经在历史里，不能再插一遍——否则铃铛会显示两条一模一样的。
    expect(service.notifications().map((item) => item.id)).toEqual(['pushed', 'old']);
  });

  it('加载失败时保留已推送的通知，并复位 loading', async () => {
    signalR.notifications.set([notification('pushed', '2026-01-02T00:00:00Z')]);

    const init = service.init();
    httpMock
      .expectOne((req) => req.url === '/api/v1/notifications')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await init;

    expect(service.loading()).toBeFalse();
    expect(service.notifications().map((item) => item.id)).toEqual(['pushed']);
  });
});
