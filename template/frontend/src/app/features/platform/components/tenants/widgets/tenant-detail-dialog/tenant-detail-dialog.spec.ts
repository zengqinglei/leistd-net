import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, Subject } from 'rxjs';

import { TenantDetailDialog } from './tenant-detail-dialog';
import { ConfirmService } from '../../../../../../core/feedback/confirm-service';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { TenantConnectionDto } from '../../../../../../shared/dtos/tenant-connection.dto';
import { TenantOutputDto } from '../../../../../../shared/dtos/tenant.dto';
import { TenantConnectionService } from '../../../../services/tenant-connection-service';

/**
 * 详情弹窗的连接段是一张可增删改的表，几处只在"操作顺序不寻常"时才暴露的问题由这组用例钉住：
 *
 * 1. 连接查询没有随弹窗关闭/换租户取消时，慢响应会把另一个租户的连接列表
 *    贴到当前租户的身份信息旁边——两个租户的信息拼在一屏，而且看不出来是错的。
 * 2. 列表为空必须有一句话。去掉标志位之后，"故意不分库"与"漏登记"在数据上完全一样，
 *    空白的一段谁也读不出当前是哪一种。
 * 3. 提交时的 `expectedVersion` 选错档（首次登记该传 null、改已有的该传读到的版本），
 *    后端会拒或让人悄悄盖掉别人的改动，而前端看不出任何异常。
 * 4. 连接串只写：任何时候都不该被预填。
 *
 * 断言一律落在**渲染结果**或**发出的请求参数**上，而不是内部 signal：
 * 内部字段可以改名，"用户看到了谁的连接"和"发出去的是哪个版本"不会。
 */
describe('TenantDetailDialog', () => {
  let fixture: ComponentFixture<TenantDetailDialog>;
  let component: TenantDetailDialog;
  let pending: Map<string, Subject<TenantConnectionDto[]>>;
  let requested: string[];
  let setConnection: jasmine.Spy;
  let removeConnection: jasmine.Spy;
  let confirm: jasmine.SpyObj<ConfirmService>;

  function tenant(id: string, name: string): TenantOutputDto {
    return {
      id,
      name,
      displayName: name.toUpperCase(),
      isActive: true,
      creationTime: '2026-01-15T12:00:00Z',
    };
  }

  function connectionOf(tenantId: string, name: string, version = 1): TenantConnectionDto {
    return { tenantId, name, version };
  }

  async function setUp(): Promise<void> {
    pending = new Map();
    requested = [];
    setConnection = jasmine
      .createSpy('setConnection')
      .and.returnValue(of(connectionOf('t-a', 'crm')));
    removeConnection = jasmine.createSpy('removeConnection').and.returnValue(of(undefined));

    confirm = jasmine.createSpyObj<ConfirmService>('ConfirmService', ['open']);
    confirm.open.and.resolveTo(true);

    TestBed.configureTestingModule({
      imports: [TenantDetailDialog],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
        { provide: ConfirmService, useValue: confirm },
        {
          provide: TenantConnectionService,
          useValue: {
            // 每个租户一个 Subject：用例自己决定谁先返回，才能造出乱序
            getConnections: (id: string): Observable<TenantConnectionDto[]> => {
              requested.push(id);
              const subject = new Subject<TenantConnectionDto[]>();
              pending.set(id, subject);
              return subject;
            },
            setConnection,
            removeConnection,
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
  const connectionStringField = () =>
    document.getElementById('tenant-connection-string') as HTMLInputElement | null;

  async function resolve(tenantId: string, connections: TenantConnectionDto[]): Promise<void> {
    const subject = pending.get(tenantId);
    subject?.next(connections);
    subject?.complete();
    await fixture.whenStable();
  }

  /** 打开某个租户并把它的连接列表交付到位。 */
  async function openWith(
    target: TenantOutputDto,
    connections: TenantConnectionDto[],
  ): Promise<void> {
    await open(target);
    await resolve(target.id, connections);
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
    await resolve('t-b', [connectionOf('t-b', 'globex-crm')]);
    await resolve('t-a', [connectionOf('t-a', 'acme-legacy')]);

    expect(text()).toContain('globex-crm');
    // 取消掉的那次请求不该再写回界面，否则 globex 的身份信息会配上 acme 的连接列表
    expect(text()).not.toContain('acme-legacy');
  });

  // 一条登记都没有就是"不单独分库"这一档。这句话是界面上唯一能看出当前状态的东西：
  // 没有它，"故意不分库"与"漏登记"就长得一模一样。
  it('列表为空时显式说明该租户不单独分库', async () => {
    await setUp();

    await openWith(tenant('t-a', 'acme'), []);

    expect(text()).toContain(component.label('connectionsEmpty'));
  });

  it('已登记的连接按名字与版本列出，不显示连接串', async () => {
    await setUp();

    await openWith(tenant('t-a', 'acme'), [
      connectionOf('t-a', 'default', 3),
      connectionOf('t-a', 'crm', 1),
    ]);

    expect(text()).toContain('default');
    expect(text()).toContain('crm');
    expect(text()).toContain(component.label('version'));
    expect(text()).not.toContain(component.label('connectionsEmpty'));
    expect(text()).not.toMatch(/Host=|Password=|User ID=/i);
  });

  it('添加连接：名字归一化为小写，首次登记预期版本为 null', async () => {
    await setUp();
    await openWith(tenant('t-a', 'acme'), []);

    component.startAdd();
    // 填 Crm 也放行：后端按小写归一化存取，两种写法命中同一条
    component.connectionForm.name().value.set('Crm');
    component.connectionForm.connectionString().value.set('Host=db;Password=spec');
    await fixture.whenStable();

    component.submitEditor();

    expect(setConnection).toHaveBeenCalledWith('t-a', 'crm', {
      expectedVersion: null,
      connectionString: 'Host=db;Password=spec',
    });
  });

  it('改连接串：沿用原名并带上读到的版本，输入框从不预填', async () => {
    await setUp();
    const existing = connectionOf('t-a', 'crm', 5);
    await openWith(tenant('t-a', 'acme'), [existing]);

    component.startEdit(existing);
    await fixture.whenStable();

    // 连接串加密存储、接口从不返回，预填一个占位值只会被原样提交回去覆盖真值
    expect(connectionStringField()?.value).toBe('');
    expect(connectionStringField()?.type).toBe('password');

    component.connectionForm.connectionString().value.set('Host=db;Password=rotated');
    await fixture.whenStable();
    component.submitEditor();

    // 版本传错（比如照搬 null）后端会按"忘了带版本"拒绝，或者让后写者盖掉别人的改动
    expect(setConnection).toHaveBeenCalledWith('t-a', 'crm', {
      expectedVersion: 5,
      connectionString: 'Host=db;Password=rotated',
    });
  });

  // PUT 是整串覆盖，后端没有"不传即保留原值"这一档：留空静默不提交会让人以为已经保存
  it('连接串留空时表单非法，不提交', async () => {
    await setUp();
    const existing = connectionOf('t-a', 'crm', 5);
    await openWith(tenant('t-a', 'acme'), [existing]);

    component.startEdit(existing);
    await fixture.whenStable();

    expect(component.connectionForm().invalid()).toBeTrue();
    component.submitEditor();

    expect(setConnection).not.toHaveBeenCalled();
  });

  it('连接名不合规时表单非法，不提交', async () => {
    await setUp();
    await openWith(tenant('t-a', 'acme'), []);

    component.startAdd();
    component.connectionForm.name().value.set('crm db!');
    component.connectionForm.connectionString().value.set('Host=db;Password=spec');
    await fixture.whenStable();

    expect(component.connectionForm().invalid()).toBeTrue();
    expect(
      component.connectionForm
        .name()
        .errors()
        .map((error) => error.message),
    ).toContain(component.label('connectionNameInvalid'));
    component.submitEditor();

    expect(setConnection).not.toHaveBeenCalled();
  });

  // 同名再"添加"一次会被后端按版本冲突拒掉，在提交前就说清楚该走"改连接串"
  it('连接名已登记时表单非法，不提交', async () => {
    await setUp();
    await openWith(tenant('t-a', 'acme'), [connectionOf('t-a', 'crm', 2)]);

    component.startAdd();
    component.connectionForm.name().value.set('CRM');
    component.connectionForm.connectionString().value.set('Host=db;Password=spec');
    await fixture.whenStable();

    expect(component.connectionForm().invalid()).toBeTrue();
    expect(
      component.connectionForm
        .name()
        .errors()
        .map((error) => error.message),
    ).toContain(component.label('connectionNameTaken'));
    component.submitEditor();

    expect(setConnection).not.toHaveBeenCalled();
  });

  it('删除连接先确认，确认后带上读到的版本', async () => {
    await setUp();
    const existing = connectionOf('t-a', 'crm', 7);
    await openWith(tenant('t-a', 'acme'), [existing]);

    await component.removeConnection(existing);

    expect(confirm.open).toHaveBeenCalled();
    expect(removeConnection).toHaveBeenCalledWith('t-a', 'crm', 7);
  });

  it('取消删除确认时不发请求', async () => {
    await setUp();
    const existing = connectionOf('t-a', 'crm', 7);
    await openWith(tenant('t-a', 'acme'), [existing]);
    confirm.open.and.resolveTo(false);

    await component.removeConnection(existing);

    expect(removeConnection).not.toHaveBeenCalled();
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

  // 连接接口要求 App.Tenants.Update：没有这个权限就别发那些必然 403 的请求，
  // 也别渲染一个点不动的编辑按钮。
  it('没有更新权限时不请求连接列表，也不渲染编辑按钮', async () => {
    await setUp();
    fixture.componentRef.setInput('canManage', false);
    await open(tenant('t-a', 'acme'));

    expect(requested).toEqual([]);
    // 连接段与编辑按钮都不该出现（弹窗自带的关闭按钮不算）
    expect(text()).not.toContain(component.label('sectionConnections'));
    const labels = [...(dialogHost()?.querySelectorAll('button') ?? [])].map((button) =>
      (button.textContent ?? '').trim(),
    );
    expect(labels).not.toContain(component.label('edit'));
    expect(labels).toContain(component.label('close'));
  });
});
