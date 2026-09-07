import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { provideTransloco, TRANSLOCO_LOADER } from '@jsverse/transloco';
//#endif

import { Settings } from './settings';
import { AuthService } from '../../../../core/services/auth-service';
import { SettingOutputDto } from '../../../../core/settings/setting.dto';

function row(overrides: Partial<SettingOutputDto> = {}): SettingOutputDto {
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

/**
 * 保存链路的异步状态。
 *
 * 这一段是上一轮真的出过问题的地方：写入在途时输入没禁用、草稿在 PUT 一完成就删除，
 * 于是「保存后继续输入会被吞掉」和「PUT 成功但刷新失败时界面显示旧值」两个坑同时存在，
 * 而它们都不会让任何既有用例变红。这里按行为断言，而不是断言实现细节。
 */
describe('Settings page', () => {
  let fixture: ComponentFixture<Settings>;
  let http: HttpTestingController;

  const input = () =>
    fixture.nativeElement.querySelector(
      '[data-testid="setting-input-Display.TimeZone"]',
    ) as HTMLInputElement;
  const saveButton = () =>
    fixture.nativeElement.querySelector(
      '[data-testid="setting-save-Display.TimeZone"]',
    ) as HTMLButtonElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [Settings],
      //#if (IncludeLocalization)
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // 页面经会话上下文刷新设置，那条链会碰到 AuthService；真实实现会拉起认证栈
        // （无本地身份的形态下是整个 OIDC 客户端），本组用例只关心页面的保存链路。
        { provide: AuthService, useValue: { clearAuthData: () => undefined } },
        provideTransloco({
          config: { availableLangs: ['en'], defaultLang: 'en', fallbackLang: 'en' },
        }),
        { provide: TRANSLOCO_LOADER, useValue: { getTranslation: () => Promise.resolve({}) } },
      ],
      //#else
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // 页面经会话上下文刷新设置，那条链会碰到 AuthService；真实实现会拉起认证栈
        // （无本地身份的形态下是整个 OIDC 客户端），本组用例只关心页面的保存链路。
        { provide: AuthService, useValue: { clearAuthData: () => undefined } },
      ],
      //#endif
    });
    fixture = TestBed.createComponent(Settings);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    // 构造时的首次载入
    http.expectOne('/api/v1/settings').flush([row()]);
    await fixture.whenStable();
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  function typeAndSave(value: string): void {
    const field = input();
    field.value = value;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    saveButton().click();
    fixture.detectChanges();
  }

  /** 完成写入后的 PUT，并把控制权交回微任务队列，让随后的 GET 真正发出。 */
  async function flushSave(): Promise<void> {
    http.expectOne('/api/v1/settings/current-user').flush(null);
    await fixture.whenStable();
  }

  it('disables the write controls while a save is in flight', async () => {
    typeAndSave('UTC');

    // PUT 还没返回：输入与按钮都必须禁用，否则这期间的输入会被随后的草稿清理吞掉
    expect(input().disabled).toBeTrue();
    expect(saveButton().disabled).toBeTrue();

    await flushSave();
    http.expectOne('/api/v1/settings').flush([row({ userValue: 'UTC' })]);
    await fixture.whenStable();
  });

  // 草稿输入带空格、服务端存的是裁剪后的值：只有草稿真的被清掉，输入框才会显示
  // 服务端那份。两边都用同一个字符串的话，草稿留着也看不出来，这条用例会静默变绿。
  it('clears the draft only after the refreshed snapshot arrives', async () => {
    typeAndSave('  UTC  ');

    const put = http.expectOne('/api/v1/settings/current-user');
    expect(put.request.body).toEqual({ name: 'Display.TimeZone', value: 'UTC' });
    put.flush(null);
    await fixture.whenStable();

    http.expectOne('/api/v1/settings').flush([row({ userValue: 'UTC' })]);
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();

    // 草稿没清掉的话这里会是带空格的原始输入
    expect(input().value).toBe('UTC');
    expect(input().disabled).toBeFalse();
  });

  // PUT 成功、刷新失败：库里已经是新值，界面不能退回旧值。
  it('keeps the typed value when the refresh after a successful save fails', async () => {
    typeAndSave('UTC');
    await flushSave();
    http.expectOne('/api/v1/settings').flush('boom', { status: 500, statusText: 'Server Error' });
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();

    expect(input().value).toBe('UTC');
    // 写入链路已结束，控件重新可用
    expect(input().disabled).toBeFalse();
  });
});
