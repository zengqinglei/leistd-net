import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
//#if (!LocalIdentity)
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { of } from 'rxjs';
//#endif

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
      // prettier-ignore
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (!LocalIdentity)
        {
          provide: OidcSecurityService,
          useValue: { getAccessToken: () => of('resource-access-token') },
        },
        //#endif
      ],
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

  it('请求在途时切换认证主体，旧响应不写回新主体的列表', async () => {
    spyOn(signalR, 'disconnect').and.resolveTo();

    const init = service.init();
    const request = httpMock.expectOne((req) => req.url === '/api/v1/notifications');

    // 响应还没回来就登出/换人登录：reset 递增认证代际并清空列表。
    await signalR.reset();

    request.flush([notification('a-1', '2026-01-01T00:00:00Z')]);
    await init;

    // 旧响应无条件写回共享 signal，就是把上一个用户的历史通知落到下一个人界面上。
    expect(service.notifications()).toEqual([]);
  });

  it('请求在途时切换主体，旧 init 不再建立连接', async () => {
    spyOn(signalR, 'disconnect').and.resolveTo();
    (signalR.connect as jasmine.Spy).calls.reset();

    const init = service.init();
    const request = httpMock.expectOne((req) => req.url === '/api/v1/notifications');

    await signalR.reset();
    request.flush([]);
    await init;

    // 服务端 Cookie 此刻可能仍有效：这一轮 init 建成的连接会把 principal 定在
    // 上一个人身上，下一个用户的 connect() 见到活连接就直接复用了它。
    expect(signalR.connect).not.toHaveBeenCalled();
  });

  it('写请求在途时切换主体，旧响应不修改新主体的列表', async () => {
    spyOn(signalR, 'disconnect').and.resolveTo();
    signalR.notifications.set([notification('b-1', '2026-02-01T00:00:00Z')]);

    const cleared = service.clearAll();
    const request = httpMock.expectOne((req) => req.url === '/api/v1/notifications');

    await signalR.reset();
    signalR.notifications.set([notification('b-1', '2026-02-01T00:00:00Z')]);

    request.flush(null);
    await cleared;

    // A 的 clearAll 在途、B 登录并加载完自己的列表，这一句会把 B 的列表清空。
    expect(service.notifications().map((item) => item.id)).toEqual(['b-1']);
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
