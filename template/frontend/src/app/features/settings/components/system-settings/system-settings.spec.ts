//#if (TenantWorkspace)
import { Location } from '@angular/common';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
//#else
import { provideHttpClient } from '@angular/common/http';
//#endif
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
//#if (IncludeLocalization || (TenantWorkspace && (IncludeNotifications || IncludeRealTime)))
// prettier-ignore
import {
  //#if (TenantWorkspace && (IncludeNotifications || IncludeRealTime))
  EMPTY,
  //#endif
  //#if (IncludeLocalization)
  of,
  //#endif
} from 'rxjs';
//#endif

//#if (TenantWorkspace)
import { authGuard } from '../../../../core/guards/auth-guard';
import { workspaceGuard } from '../../../../core/guards/workspace-guard';
//#endif
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
//#if (TenantWorkspace && IncludeLocalization)
import { acceptLanguageInterceptor } from '../../../../core/interceptors/accept-language-interceptor';
//#endif
//#if (TenantWorkspace)
import { workspaceInterceptor } from '../../../../core/interceptors/workspace-interceptor';
//#endif
import { AuthService } from '../../../../core/services/auth-service';
//#if (TenantWorkspace && IncludeLocalization)
import { LanguageService } from '../../../../core/services/language-service';
//#endif
//#if (TenantWorkspace && (IncludeNotifications || IncludeRealTime))
import { SignalRService } from '../../../../core/services/signalr-service';
//#endif
//#if (TenantWorkspace)
import { StartupService } from '../../../../core/services/startup-service';
//#endif
import { SettingOutputDto } from '../../../../core/settings/setting.dto';
//#if (TenantWorkspace)
import { PersonalSettings } from '../personal-settings/personal-settings';
import { SettingSection } from '../setting-section/setting-section';
//#endif
import { SystemSettings } from './system-settings';

function row(name: string, group: string, canManage = true, hostScope = false): SettingOutputDto {
  return {
    name,
    displayName: name,
    group,
    groupDisplayName: group,
    userValue: null,
    tenantValue: null,
    defaultValue: null,
    allowsTenantScope: !hostScope,
    allowsUserScope: false,
    allowsHostScope: hostScope,
    minimum: null,
    maximum: null,
    canManage,
  };
}

@Component({
  selector: 'app-test-settings-panel',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class Panel {}

/** 面板来自可管理的分组：说明按分组标识取，后端新增而前端没登记的分组照常出现、只是没有说明。 */
describe('SystemSettings panels', () => {
  const description = 'Log detail, request logging and telemetry export';
  let harness: RouterTestingHarness;

  async function render(
    settings: SettingOutputDto[] = [
      row('Logging.MinimumLevel', 'Operations'),
      row('Custom.Probe', 'CustomGroup'),
    ],
    url = '/settings',
  ): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          {
            path: 'settings',
            component: SystemSettings,
            children: [{ path: ':group', component: Panel }],
          },
        ]),
        { provide: AuthService, useValue: { clearAuthData: () => undefined } },
        //#if (IncludeLocalization)
        // 设置页的词条在 settings 作用域里；只给本用例要断言的那一条
        ...provideTranslocoTesting(['en'], {
          getTranslation: (lang: string) =>
            of(
              lang === 'settings/en'
                ? {
                    groupDescriptions: { Operations: description },
                    hostScope: { hint: 'One value for the whole system' },
                  }
                : {},
            ),
        }),
        //#endif
      ],
    });
    harness = await RouterTestingHarness.create(url);
    TestBed.inject(HttpTestingController).expectOne('/api/v1/settings').flush(settings);
    await harness.fixture.whenStable();
    return harness.fixture.nativeElement as HTMLElement;
  }

  const panelDescription = (host: HTMLElement) =>
    host.querySelector('h3')?.closest('header')?.querySelector('p')?.textContent?.trim() ?? null;

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('describes a known group and leaves an unknown group without a description', async () => {
    const host = await render();

    // 没带面板时落到第一个面板
    await vi.waitFor(() =>
      expect(host.querySelector('h3')?.textContent?.trim()).toBe('Operations'),
    );
    expect(panelDescription(host)).toBe(description);

    await harness.navigateByUrl('/settings/custom-group');
    expect(host.querySelector('h3')?.textContent?.trim()).toBe('CustomGroup');
    expect(panelDescription(host)).toBeNull();
  });

  // 只有系统设置入口、没有任何分组的管理子权限：面板导航为空，给出说明而不是一片空白
  it('explains that nothing can be managed when no group is manageable', async () => {
    const host = await render([row('Security.RequireTwoFactor', 'Security', false)]);

    await vi.waitFor(() =>
      expect(host.querySelector('[data-testid="settings-empty"]')).not.toBeNull(),
    );
    expect(host.querySelector('h3')).toBeNull();
  });

  // 收藏的面板地址：回到不带面板的地址，只留本页这一份空态，不再激活面板
  it('leaves a bookmarked panel when no group is manageable', async () => {
    const host = await render(
      [row('Security.RequireTwoFactor', 'Security', false)],
      '/settings/operations',
    );

    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/settings'));
    expect(host.querySelectorAll('[data-testid="settings-empty"]')).toHaveLength(1);
  });

  it('leaves out groups the user cannot manage', async () => {
    const host = await render([
      row('Security.RequireTwoFactor', 'Security', false),
      row('Logging.MinimumLevel', 'Operations'),
    ]);

    await vi.waitFor(() =>
      expect(host.querySelector('h3')?.textContent?.trim()).toBe('Operations'),
    );
    expect(host.textContent).not.toContain('Security');
    expect(host.querySelector('[data-testid="settings-empty"]')).toBeNull();
  });

  // 整组都是进程级设置：标题旁给信息图标，提示影响范围与多久在所有服务器生效；其余面板没有
  it('puts a hint beside the title of a panel whose settings are all process-wide', async () => {
    const host = await render([
      row('Logging.MinimumLevel', 'Operations', true, true),
      row('Security.Probe', 'Security'),
    ]);

    await vi.waitFor(() =>
      expect(host.querySelector('h3')?.textContent?.trim()).toBe('Operations'),
    );
    expect(host.querySelector('[data-testid="settings-panel-hint"]')).not.toBeNull();

    await harness.navigateByUrl('/settings/security');
    expect(host.querySelector('[data-testid="settings-panel-hint"]')).toBeNull();
  });
});
//#if (TenantWorkspace)

describe('SystemSettings startup reads', () => {
  const path = '/w/acme/platform/settings';
  const settingsPath = '/api/v1/settings';
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: 'w/:workspace/platform/settings',
            component: SystemSettings,
            canActivate: [authGuard, workspaceGuard],
          },
          {
            path: 'w/:workspace/workspace/settings',
            component: PersonalSettings,
            canActivate: [authGuard, workspaceGuard],
            children: [{ path: 'preferences', component: SettingSection }],
          },
          {
            path: 'w/:workspace/platform',
            component: Panel,
            canActivate: [authGuard, workspaceGuard],
          },
        ]),
        // prettier-ignore
        provideHttpClient(withInterceptors([
          //#if (IncludeLocalization)
          acceptLanguageInterceptor,
          //#endif
          workspaceInterceptor,
        ])),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en', 'zh-CN']),
        //#endif
        //#if (IncludeNotifications || IncludeRealTime)
        {
          provide: SignalRService,
          useValue: { sessionRejected$: EMPTY, reset: vi.fn(), setWorkspace: vi.fn() },
        },
        //#endif
      ],
    });
    //#if (IncludeLocalization)
    localStorage.setItem(LanguageService.STORAGE_KEY, 'en');
    //#endif
    TestBed.inject(Location).replaceState(path);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function start(failed: boolean, language?: string) {
    const done = TestBed.inject(StartupService).load();
    const preferences = language ? { language, timeZone: null, theme: null } : null;
    http.expectOne('/api/v1/auth/me').flush({
      id: 'signed-in',
      username: 'admin',
      preferences,
      preferenceOverrides: preferences,
    });
    const directory = await vi.waitFor(() => http.expectOne('/api/v1/auth/me/workspaces'));
    directory.flush([{ name: 'acme', tenantId: 't1', userId: 'acme-member' }]);
    http
      .match('/api/v1/permissions/current')
      .forEach((pending) =>
        pending.flush({ permissions: ['App.Settings'], isSuperAdmin: false, versionToken: 'v1' }),
      );
    const firstRead = http.expectOne(settingsPath);
    expect(firstRead.request.headers.get('X-Workspace')).toBe('acme');
    //#if (IncludeLocalization)
    expect(firstRead.request.headers.get('Accept-Language')).toBe('en');
    //#endif
    if (failed) {
      firstRead.flush(null, { status: 503, statusText: 'Unavailable' });
    } else {
      firstRead.flush([]);
    }
    await done;

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(path, SystemSettings);
    return harness;
  }

  it('reads system settings only once on a cold deep link', async () => {
    const harness = await start(false);
    http.expectNone(settingsPath);
    await harness.fixture.whenStable();

    await harness.navigateByUrl('/w/acme/platform', Panel);
    http.expectNone((request) => request.url.startsWith('/api/v1/'));
    await harness.navigateByUrl(path, SystemSettings);
    http.expectNone((request) => request.url.startsWith('/api/v1/'));
  });

  it('reads system settings again after failed startup', async () => {
    const harness = await start(true);
    const read = http.expectOne(settingsPath);
    expect(read.request.headers.get('X-Workspace')).toBe('acme');
    read.flush([]);
    await harness.fixture.whenStable();
  });
  it('reads account settings without the workspace header', async () => {
    const harness = await start(false);
    http.expectNone(settingsPath);

    await harness.navigateByUrl('/w/acme/workspace/settings/preferences', PersonalSettings);
    const read = http.expectOne(settingsPath);
    expect(read.request.headers.has('X-Workspace')).toBe(false);
    read.flush([]);
    await harness.fixture.whenStable();
  });
  //#if (IncludeLocalization)

  it('reads settings again when startup used a different request language', async () => {
    const harness = await start(false, 'zh-CN');
    const read = http.expectOne(settingsPath);
    expect(read.request.headers.get('Accept-Language')).toBe('zh-CN');
    read.flush([]);
    await harness.fixture.whenStable();
  });

  it('reads settings once in the new language when it changes on the page', async () => {
    const harness = await start(false);
    http.expectNone(settingsPath);
    await TestBed.inject(LanguageService).applyAccountLang('zh-CN');

    const read = http.expectOne(settingsPath);
    expect(read.request.headers.get('Accept-Language')).toBe('zh-CN');
    read.flush([]);
    await harness.fixture.whenStable();
  });
  //#endif
});
//#endif
