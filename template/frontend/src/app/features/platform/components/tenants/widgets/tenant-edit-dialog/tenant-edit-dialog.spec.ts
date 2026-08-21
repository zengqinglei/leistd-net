//#if (MultiTenancy)
import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
//#if (IncludeLocalization)
import { provideTransloco, TRANSLOCO_LOADER } from '@jsverse/transloco';
//#endif

import { TenantEditDialog } from './tenant-edit-dialog';
import {
  CreateTenantInputDto,
  TenantOutputDto,
  UpdateTenantInputDto,
} from '../../../../../../shared/dtos/tenant.dto';

/**
 * 新建与编辑共用一个对话框，两种模式的字段集与提交载荷都不一样：
 * 新建要连租户初始管理员一起建出来，编辑只改名称与显示名。
 *
 * 这条差异全靠 `isEdit()` 一个信号分流——校验规则的 `when`、模板里的 `@if`、
 * onSubmit 里的分支各写一遍。漏掉任何一处，编译期与 lint 都看不出来：
 * 要么新建时把管理员账号漏成空、要么编辑时把空邮箱和空密码发给后端。
 */
@Component({
  imports: [TenantEditDialog],
  template: `
    <app-tenant-edit-dialog [(open)]="open" [tenant]="tenant()" (save)="saved.push($event)" />
  `,
})
class HostComponent {
  readonly open = signal(true);
  readonly tenant = signal<TenantOutputDto | null>(null);
  readonly saved: (CreateTenantInputDto | UpdateTenantInputDto)[] = [];
}

describe('TenantEditDialog', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  const existing: TenantOutputDto = {
    id: '019ff8ed-221b-7673-9ba8-6b6dd5a638ab',
    name: 'acme',
    displayName: 'Acme Inc.',
    isActive: true,
    creationTime: '2026-08-14T00:00:00Z',
  };

  /** 对话框实例：表单值只能从它的 tenantForm 写入，才能覆盖真实的校验规则。 */
  function dialog(): TenantEditDialog {
    return fixture.debugElement.query(By.directive(TenantEditDialog))
      .componentInstance as TenantEditDialog;
  }

  /** 切到编辑模式：租户变化会重新触发打开时的表单回填。 */
  async function switchToEdit(): Promise<void> {
    host.tenant.set(existing);
    await fixture.whenStable();
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      // prettier-ignore
      providers: [
        provideZonelessChangeDetection(),
        //#if (IncludeLocalization)
        // 用真实 transloco 配空词条：校验消息与标题都走 translate()，缺词条时回落成键名，
        // 本组用例只关心字段是否存在、表单是否放行、提交出去的载荷长什么样。
        provideTransloco({
          config: { availableLangs: ['en'], defaultLang: 'en', fallbackLang: 'en' },
        }),
        { provide: TRANSLOCO_LOADER, useValue: { getTranslation: () => Promise.resolve({}) } },
        //#endif
      ],
    });

    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    await fixture.whenStable();
  });

  afterEach(() => fixture.destroy());

  it('新建模式缺少管理员邮箱或初始密码时表单非法，不提交', async () => {
    dialog().tenantForm.name().value.set('acme');
    await fixture.whenStable();

    // 只填名称就能提交的话，后端会收到一个没有管理员的租户——谁都进不去。
    expect(dialog().tenantForm().invalid()).toBeTrue();
    dialog().onSubmit();
    expect(host.saved).toEqual([]);

    // 密码强度规则同样只在新建模式生效，弱密码不能放行。
    dialog().tenantForm.adminEmail().value.set('admin@example.test');
    dialog().tenantForm.adminPassword().value.set('weak');
    await fixture.whenStable();

    expect(dialog().tenantForm().invalid()).toBeTrue();
    dialog().onSubmit();
    expect(host.saved).toEqual([]);
  });

  it('编辑模式不渲染管理员账号字段，且只凭名称即可放行', async () => {
    expect(document.getElementById('tenant-admin-email')).not.toBeNull();
    expect(document.getElementById('tenant-admin-password')).not.toBeNull();

    await switchToEdit();

    // 字段不渲染却仍参与校验，保存按钮会被两个看不见的空字段永久禁用。
    expect(document.getElementById('tenant-admin-email')).toBeNull();
    expect(document.getElementById('tenant-admin-password')).toBeNull();
    expect(dialog().isEdit()).toBeTrue();
    expect(dialog().tenantForm().invalid()).toBeFalse();
    expect(dialog().tenantForm.name().value()).toBe('acme');
    expect(dialog().tenantForm.displayName().value()).toBe('Acme Inc.');
  });

  it('新建提交带上管理员账号，名称去除首尾空白、空显示名转 undefined', async () => {
    dialog().tenantForm.name().value.set('  acme  ');
    dialog().tenantForm.displayName().value.set('   ');
    dialog().tenantForm.adminEmail().value.set('admin@example.test');
    // 尾部空格：密码里的空白是密码的一部分，被 trim 掉用户就再也登不进去。
    dialog().tenantForm.adminPassword().value.set('Passw0rd! ');
    await fixture.whenStable();

    dialog().onSubmit();

    const dto = host.saved[0] as CreateTenantInputDto;
    expect(dto.name).toBe('acme');
    // 显示名可选：空字符串会被存成一个空的显示名，比不填更难改回来。
    expect(dto.displayName).toBeUndefined();
    expect(dto.adminEmail).toBe('admin@example.test');
    expect(dto.adminPassword).toBe('Passw0rd! ');
    expect(dto.databaseMode).toBe('SharedDatabase');
    expect(dto.runtimeSecretReference).toBeUndefined();
    expect(dto.migrationSecretReference).toBeUndefined();
  });

  it('独立数据库模式要求运行时和迁移 Secret 引用', async () => {
    dialog().tenantForm.name().value.set('acme');
    dialog().tenantForm.adminEmail().value.set('admin@example.test');
    dialog().tenantForm.adminPassword().value.set('Passw0rd!');
    dialog().tenantForm.databaseMode().value.set('DedicatedDatabase');
    await fixture.whenStable();

    expect(document.getElementById('tenant-runtime-secret-reference')).not.toBeNull();
    expect(document.getElementById('tenant-migration-secret-reference')).not.toBeNull();
    expect(dialog().tenantForm().invalid()).toBeTrue();

    dialog().tenantForm.runtimeSecretReference().value.set('vault://runtime/acme');
    dialog().tenantForm.migrationSecretReference().value.set('vault://migration/acme');
    await fixture.whenStable();
    dialog().onSubmit();

    expect(host.saved[0]).toEqual(
      jasmine.objectContaining({
        databaseMode: 'DedicatedDatabase',
        runtimeSecretReference: 'vault://runtime/acme',
        migrationSecretReference: 'vault://migration/acme',
      }),
    );
  });

  it('编辑提交只带名称与显示名，不夹带管理员字段', async () => {
    await switchToEdit();

    dialog().tenantForm.name().value.set('acme-renamed');
    await fixture.whenStable();

    dialog().onSubmit();

    // 编辑载荷里出现 adminEmail/adminPassword，等于用一组空凭据覆盖租户管理员。
    expect(Object.keys(host.saved[0]).sort()).toEqual(['displayName', 'name']);
    expect(host.saved[0]).toEqual({ name: 'acme-renamed', displayName: 'Acme Inc.' });
  });
});
//#endif
