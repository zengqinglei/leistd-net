import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
import { BehaviorSubject } from 'rxjs';
//#endif

import { PermissionGrantDialog } from './permission-grant-dialog';

/**
 * 权限弹窗的加载竞态回归。
 *
 * 弹窗是角色与用户共用的，主体可以在两次请求之间切换。若加载不取消也不校验归属，
 * 晚到的响应会落到新主体上，保存时就会把上一个主体的授予写给当前主体——两边版本
 * 都是 0 时乐观并发也拦不住，因此这三条必须由前端自己守住。
 */
@Component({
  imports: [PermissionGrantDialog],
  template: ` <app-permission-grant-dialog [(open)]="open" [roleId]="roleId()" /> `,
})
class HostComponent {
  readonly open = signal(true);
  readonly roleId = signal<string | null>('role-a');
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

/** 手风琴组标题的展开按钮。 */
function groupTrigger(): HTMLElement {
  const trigger = document.querySelector<HTMLElement>('hlm-accordion-trigger button');
  expect(trigger).withContext('group trigger should be rendered').not.toBeNull();
  return trigger!;
}

function grants(providerKey: string, granted: boolean) {
  return {
    providerName: 'Role',
    providerKey,
    version: 7,
    grants: [{ name: 'App.Users', granted }],
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

    fixture.componentInstance.roleId.set('role-b');
    await fixture.whenStable();

    // 切换主体后上一次的授予请求被取消，新主体重新走完整加载。
    expect(first.cancelled).toBeTrue();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    httpTesting.expectOne('/api/v1/permissions/grants/roles/role-b').flush(grants('role-b', true));
    await fixture.whenStable();

    expect(dialog().isGranted('App.Users')).toBeTrue();
    expect(dialog().version()).toBe(7);
  });

  it('ignores a response whose subject does not match the request', async () => {
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());

    // 服务端/Mock 返回了另一个主体的数据：宁可不渲染，也不能当成本主体的授予。
    httpTesting.expectOne('/api/v1/permissions/grants/roles/role-a').flush(grants('role-b', false));
    await fixture.whenStable();

    expect(dialog().isGranted('App.Users')).toBeFalse();
    expect(dialog().version()).toBe(0);
  });

  it('drops the previous subject state when loading the next one fails', async () => {
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    httpTesting.expectOne('/api/v1/permissions/grants/roles/role-a').flush(grants('role-a', true));
    await fixture.whenStable();
    expect(dialog().isGranted('App.Users')).toBeTrue();

    fixture.componentInstance.roleId.set('role-b');
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    httpTesting
      .expectOne('/api/v1/permissions/grants/roles/role-b')
      .flush({ detail: 'boom' }, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    // 加载失败后界面不得留着 A 的状态与版本，否则保存会把 A 的授予写给 B；
    // 两边版本恰好相同时乐观并发也拦不住。
    expect(dialog().isGranted('App.Users')).toBeFalse();
    expect(dialog().version()).toBe(0);
    expect(dialog().canSave()).toBeFalse();
  });

  it('blocks saving until the current subject has loaded successfully', async () => {
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());

    // 响应归属不符会被丢弃，此时保存必须不可用，也不得发出任何写请求。
    httpTesting.expectOne('/api/v1/permissions/grants/roles/role-a').flush(grants('role-b', true));
    await fixture.whenStable();

    expect(dialog().canSave()).toBeFalse();
    dialog().onSave();
    httpTesting.expectNone('/api/v1/permissions/grants/roles/role-a');
  });

  it('reloads the subject after a concurrency conflict', async () => {
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    httpTesting.expectOne('/api/v1/permissions/grants/roles/role-a').flush(grants('role-a', true));
    await fixture.whenStable();

    dialog().onSave();
    httpTesting
      .expectOne({ method: 'PUT', url: '/api/v1/permissions/grants/roles/role-a' })
      .flush({ detail: 'conflict' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();

    // 409 后必须真的重新拉取，否则用户只能拿着旧版本反复重试、反复 409。
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    httpTesting
      .expectOne({ method: 'GET', url: '/api/v1/permissions/grants/roles/role-a' })
      .flush(grants('role-a', false));
    await fixture.whenStable();

    expect(dialog().isGranted('App.Users')).toBeFalse();
  });

  it('keeps the spinner up until the next subject has loaded', async () => {
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    httpTesting.expectOne('/api/v1/permissions/grants/roles/role-a');

    fixture.componentInstance.roleId.set('role-b');
    await fixture.whenStable();

    // switchMap 退订上一轮时它的 finalize 照样会跑。若 loading 在 switchMap 之前置位，
    // 这一下就把它打回 false —— B 还在飞，界面已经显示成加载完成。
    expect(dialog().loading()).toBeTrue();

    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    httpTesting.expectOne('/api/v1/permissions/grants/roles/role-b').flush(grants('role-b', true));
    await fixture.whenStable();

    expect(dialog().loading()).toBeFalse();
  });

  it('writes the model when a rendered checkbox is clicked', async () => {
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    httpTesting.expectOne('/api/v1/permissions/grants/roles/role-a').flush(grants('role-a', true));
    await fixture.whenStable();

    // 走真实 DOM 点击而不是调组件方法：复选框的输出名写错时组件方法照样能过，
    // 界面却只改了控件自身的内部状态，重新加载就"复原"——只有点击才暴露得出来。
    const checkbox = document.getElementById('App.Users');
    expect(checkbox).withContext('permission checkbox should be rendered').not.toBeNull();

    checkbox?.click();
    await fixture.whenStable();

    expect(dialog().isGranted('App.Users')).toBeFalse();
  });

  it('keeps the group expanded after its last grant is cleared', async () => {
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    httpTesting.expectOne('/api/v1/permissions/grants/roles/role-a').flush(grants('role-a', true));
    await fixture.whenStable();
    expect(dialog().isExpanded('App')).toBeTrue();

    // 展开状态一旦由"本组已有授予"算出来，取消最后一个勾就会顺手把整组折叠掉，
    // 用户只是想改一个勾，界面却塌了。
    document.getElementById('App.Users')?.click();
    await fixture.whenStable();

    expect(dialog().isGranted('App.Users')).toBeFalse();
    expect(dialog().isExpanded('App')).toBeTrue();
  });

  it('reopens a manually collapsed group when the search matches it', async () => {
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    httpTesting.expectOne('/api/v1/permissions/grants/roles/role-a').flush(grants('role-a', true));
    await fixture.whenStable();
    expect(groupTrigger().getAttribute('aria-expanded')).toBe('true');

    groupTrigger().click();
    await fixture.whenStable();
    expect(groupTrigger().getAttribute('aria-expanded')).toBe('false');

    // 不把手动折叠写回状态的话，isExpanded() 本来就是 true、输入值没有变化，
    // 手风琴不会重新打开它已经关掉的组，搜索命中项就一直藏着。
    dialog().onKeywordChange('Users');
    await fixture.whenStable();

    expect(groupTrigger().getAttribute('aria-expanded')).toBe('true');
  });

  it('cancels the in-flight load when the dialog is destroyed', async () => {
    await fixture.whenStable();
    httpTesting.expectOne(DEFINITIONS_URL).flush(definitions());
    const pending = httpTesting.expectOne('/api/v1/permissions/grants/roles/role-a');

    fixture.destroy();

    // 销毁即退订：请求被取消，晚到的响应没有任何落地路径。
    expect(pending.cancelled).toBeTrue();
    expect(() => pending.flush(grants('role-a', true))).toThrowError(
      /Cannot flush a cancelled request/,
    );
  });
});
