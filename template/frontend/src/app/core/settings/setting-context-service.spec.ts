import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SettingContextService } from './setting-context-service';
import { SettingOutputDto } from './setting.dto';

function setting(overrides: Partial<SettingOutputDto> = {}): SettingOutputDto {
  return {
    name: 'Display.TimeZone',
    displayName: '时区',
    userValue: null,
    tenantValue: null,
    defaultValue: 'Asia/Shanghai',
    allowsTenantScope: true,
    allowsUserScope: true,
    ...overrides,
  };
}

describe('SettingContextService', () => {
  let service: SettingContextService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [SettingContextService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SettingContextService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('resolves values by user → tenant → default', async () => {
    const load = service.load();
    http
      .expectOne('/api/v1/settings')
      .flush([setting({ userValue: 'Asia/Tokyo', tenantValue: 'UTC' })]);
    await load;

    expect(service.timeZone()).toBe('Asia/Tokyo');
  });

  it('falls back to the tenant value, then the code default', async () => {
    const first = service.load();
    http.expectOne('/api/v1/settings').flush([setting({ tenantValue: 'UTC' })]);
    await first;
    expect(service.timeZone()).toBe('UTC');

    const second = service.load();
    http.expectOne('/api/v1/settings').flush([setting()]);
    await second;
    expect(service.timeZone()).toBe('Asia/Shanghai');
  });

  // 失败必须向上传播、且保留上一份有效快照：在这里吞掉并清空，会让「保存成功后
  // 刷新失败」表现成页面变空、时区退回浏览器，看起来像是保存本身出了问题。
  it('keeps the last good snapshot when a reload fails', async () => {
    const first = service.load();
    http.expectOne('/api/v1/settings').flush([setting({ userValue: 'Asia/Tokyo' })]);
    await first;

    const failing = service.load();
    http.expectOne('/api/v1/settings').flush('boom', { status: 500, statusText: 'Server Error' });

    await expectAsync(failing).toBeRejected();
    expect(service.timeZone()).toBe('Asia/Tokyo');
  });

  it('clears the snapshot on sign-out', async () => {
    const load = service.load();
    http.expectOne('/api/v1/settings').flush([setting({ userValue: 'Asia/Tokyo' })]);
    await load;

    service.clear();

    expect(service.timeZone()).toBeUndefined();
  });
});
