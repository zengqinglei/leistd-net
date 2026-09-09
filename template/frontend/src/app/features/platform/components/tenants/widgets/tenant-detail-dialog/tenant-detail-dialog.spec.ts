import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { TenantDetailDialog } from './tenant-detail-dialog';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { TenantConnectionOutputDto } from '../../../../../../shared/dtos/tenant-connection.dto';
import { TenantOutputDto } from '../../../../../../shared/dtos/tenant.dto';
import { TenantConnectionService } from '../../../../services/tenant-connection-service';

/**
 * 详情弹窗有两处只在"操作顺序不寻常"时才暴露的问题，这组用例钉住它们：
 *
 * 1. 连接查询没有随弹窗关闭/换租户取消时，慢响应会把另一个租户的数据库配置
 *    贴到当前租户的身份信息旁边——两个租户的信息拼在一屏，而且看不出来是错的。
 * 2. 编辑动作用 `model` 表达时，连续两次编辑同一个租户，第二次不会发出事件
 *    （signal 判等），详情关掉了、编辑框却不打开。
 *
 * 断言一律落在**渲染结果**上，而不是内部 signal：内部字段可以改名，
 * "用户看到了谁的密钥引用名"不会。
 */
describe('TenantDetailDialog', () => {
  let fixture: ComponentFixture<TenantDetailDialog>;
  let component: TenantDetailDialog;
  let pending: Map<string, Subject<TenantConnectionOutputDto>>;
  let requested: string[];

  function tenant(id: string, name: string): TenantOutputDto {
    return {
      id,
      name,
      displayName: name.toUpperCase(),
      isActive: true,
      creationTime: '2026-01-15T12:00:00Z',
    };
  }

  function connectionOf(tenantId: string, secret: string): TenantConnectionOutputDto {
    return {
      tenantId,
      databaseMode: 'dedicatedDatabase',
      runtimeSecretReference: secret,
      migrationSecretReference: `${secret}-migration`,
      version: 1,
    };
  }

  async function setUp(): Promise<void> {
    pending = new Map();
    requested = [];

    TestBed.configureTestingModule({
      imports: [TenantDetailDialog],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
        {
          provide: TenantConnectionService,
          useValue: {
            // 每个租户一个 Subject：用例自己决定谁先返回，才能造出乱序
            getConnection: (id: string): Observable<TenantConnectionOutputDto> => {
              requested.push(id);
              const subject = new Subject<TenantConnectionOutputDto>();
              pending.set(id, subject);
              return subject;
            },
          },
        },
      ],
    });

    fixture = TestBed.createComponent(TenantDetailDialog);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('canManage', true);
    await fixture.whenStable();
  }

  async function open(target: TenantOutputDto): Promise<void> {
    fixture.componentRef.setInput('tenant', target);
    fixture.componentRef.setInput('open', true);
    await fixture.whenStable();
  }

  async function close(): Promise<void> {
    fixture.componentRef.setInput('open', false);
    await fixture.whenStable();
  }

  // 弹窗内容经 *hlmDialogPortal 渲染到 CDK 浮层里，不在组件宿主元素内——
  // 断言得看 document，否则永远拿到空串（那会让所有断言都"通过"成假的）。
  const dialogHost = () => document.querySelector('[role="dialog"]') as HTMLElement | null;
  const text = () => dialogHost()?.textContent ?? '';

  async function resolve(tenantId: string, secret: string): Promise<void> {
    const subject = pending.get(tenantId);
    subject?.next(connectionOf(tenantId, secret));
    subject?.complete();
    await fixture.whenStable();
  }

  it('换到另一个租户后，前一个租户的慢响应不再影响界面', async () => {
    await setUp();
    const acme = tenant('t-a', 'acme');
    const globex = tenant('t-b', 'globex');

    await open(acme);
    await close();
    await open(globex);

    expect(requested).toEqual(['t-a', 't-b']);

    // globex 先回来，acme 的响应姗姗来迟。
    // 发完要 complete：真实 HttpClient 就是发一个值随即完成，只 next 不 complete
    // 会让界面一直停在加载态，断言测到的就不是"数据渲染成什么"了。
    await resolve('t-b', 'secret-globex');
    await resolve('t-a', 'secret-acme');

    expect(text()).toContain('secret-globex');
    // 取消掉的那次请求不该再写回界面，否则 globex 的身份信息会配上 acme 的库配置
    expect(text()).not.toContain('secret-acme');
  });

  it('关闭弹窗即取消在途的连接查询，不把加载态留在原地', async () => {
    await setUp();
    await open(tenant('t-a', 'acme'));

    const subject = pending.get('t-a')!;
    expect(subject.observed).toBeTrue();

    await close();

    expect(subject.observed).toBeFalse();
  });

  // 连续两次编辑同一个租户：第二次也必须发出事件。
  // 用 model 表达这个动作时，第二次 set 拿到同一个对象引用，signal 判等后静默不发。
  it('同一个租户连续两次点编辑都会发出事件', async () => {
    await setUp();
    const acme = tenant('t-a', 'acme');
    const seen: TenantOutputDto[] = [];
    component.edit.subscribe((value) => seen.push(value));

    await open(acme);
    component.onEdit();
    await fixture.whenStable();

    await open(acme);
    component.onEdit();
    await fixture.whenStable();

    expect(seen.length).toBe(2);
    expect(seen[1]).toBe(acme);
  });

  // 连接配置接口要求 App.Tenants.Update：没有这个权限就别发那个必然 403 的请求，
  // 也别渲染一个点不动的编辑按钮。
  it('没有更新权限时不请求连接配置，也不渲染编辑按钮', async () => {
    await setUp();
    fixture.componentRef.setInput('canManage', false);
    await open(tenant('t-a', 'acme'));

    expect(requested).toEqual([]);
    // 配置段与编辑按钮都不该出现（弹窗自带的关闭按钮不算）
    expect(text()).not.toContain(component.label('sectionPlacement'));
    const labels = [...(dialogHost()?.querySelectorAll('button') ?? [])].map((button) =>
      (button.textContent ?? '').trim(),
    );
    expect(labels).not.toContain(component.label('edit'));
    expect(labels).toContain(component.label('close'));
  });
});
