//#if (IncludeRoles)
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
import { BehaviorSubject } from 'rxjs';
//#endif

import { PermissionGrantDialog, PermissionGrantProvider } from './permission-grant-dialog';

/**
 * 权限弹窗的加载竞态回归。
 *
 * 弹窗是角色与用户共用的，主体可以在两次请求之间切换。若加载不取消也不校验归属，
 * 晚到的响应会落到新主体上，保存时就会把上一个主体的授予写给当前主体——两边版本
 * 都是 0 时乐观并发也拦不住，因此这三条必须由前端自己守住。
 */
@Component({
  imports: [PermissionGrantDialog],
  template: `
    <app-permission-grant-dialog
      [(open)]="open"
      [providerName]="providerName()"
      [providerKey]="providerKey()"
    />
  `,
})
class HostComponent {
  readonly open = signal(true);
  readonly providerName = signal<PermissionGrantProvider>('Role');
  readonly providerKey = signal<string | null>('role-a');
}

const DEFINITIONS_URL = '/api/v1/permissions/definitions';

function definitions() {
  return [
    {
      name: 'App',
      displayName: 'App',
      permissions: [
        { name: 'App.Users', displayName: 'Users', parentName: undefined, children: [] },
      ],
    },
  ];
}

function grants(providerKey: string, direct: 'Granted' | 'Prohibited' | null) {
  return {
    providerName: 'Role',
    providerKey,
    revision: 7,
    grants: [{ name: 'App.Users', direct, inherited: null, effective: direct === 'Granted' }],
  };
}

describe('PermissionGrantDialog', () => {
  let fixture: ComponentFixture<HostComponent>;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    //#if (IncludeLocalization)
    // 最小 Transloco 桩：本组用例只关心加载竞态，不关心具体文案。
    const translations = new BehaviorSubject<Record<string, string>>({});
    const transloco = {
      translate: (key: string) => key,
      selectTranslation: () => translations.asObservable(),
    };
    //#endif

    TestBed.configureTestingModule({
      imports: [HostComponent],
      // prettier-ignore
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        { provide: TranslocoService, useValue: transloco },
        //#endif
      ],
    });
    fixture = TestBed.createComponent(HostComponent);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  function dialog(): PermissionGrantDialog {
    return fixture.debugElement.children[0].componentInstance as PermissionGrantDialog;
  }

  it('cancels the in-flight load when the subject changes', async () => {
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    const first = httpTesting.expectOne('/api/v1/permissions/grants/roles/role-a');

    fixture.componentInstance.providerKey.set('role-b');
    await fixture.whenStable();

    // 切换主体后上一次的授予请求被取消，新主体重新走完整加载。
    expect(first.cancelled).toBeTrue();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    httpTesting
      .expectOne('/api/v1/permissions/grants/roles/role-b')
      .flush(grants('role-b', 'Granted'));
    await fixture.whenStable();

    expect(dialog().stateOf('App.Users')).toBe('Granted');
    expect(dialog().revision()).toBe(7);
  });

  it('ignores a response whose subject does not match the request', async () => {
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());

    // 服务端/Mock 返回了另一个主体的数据：宁可不渲染，也不能当成本主体的授予。
    httpTesting
      .expectOne('/api/v1/permissions/grants/roles/role-a')
      .flush(grants('role-b', 'Prohibited'));
    await fixture.whenStable();

    expect(dialog().stateOf('App.Users')).toBe('Inherit');
    expect(dialog().revision()).toBe(0);
  });

  it('cancels the in-flight load when the dialog is destroyed', async () => {
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    const pending = httpTesting.expectOne('/api/v1/permissions/grants/roles/role-a');

    fixture.destroy();

    // 销毁即退订：请求被取消，晚到的响应没有任何落地路径。
    expect(pending.cancelled).toBeTrue();
    expect(() => pending.flush(grants('role-a', 'Granted'))).toThrowError(
      /Cannot flush a cancelled request/,
    );
  });
});
//#endif
