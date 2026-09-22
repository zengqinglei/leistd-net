import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronRight, lucideDatabase, lucideSearchX, lucideUserPen } from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ColumnDef, PaginationState } from '@tanstack/angular-table';

import { SettingContextService } from '../../../../../../core/settings/setting-context-service';
import {
  TablePaginator,
  TablePaginatorLabels,
} from '../../../../../../shared/components/table-paginator/table-paginator';
import { tableColumnVisibility } from '../../../../../../shared/models/table-column-meta';
import {
  injectAppTable,
  type AppTableFeatures,
} from '../../../../../../shared/models/table-features';
import { AppDate } from '../../../../../../shared/pipes/app-date-pipe';
import { createExpandableRows } from '../../../../../../shared/utils/expandable-rows';
import { resolveTableUpdater } from '../../../../../../shared/utils/table-query-state';
import { tableViewportSignal } from '../../../../../../shared/utils/table-viewport';
import { OperationRecordOutputDto } from '../../../../models/operation-record.dto';
//#if (!IncludeLocalization)

/**
 * 不启用多语言时的英文句子表。
 *
 * **与 `public/i18n/en.json` 的 `operationRecords.actions` 是两个事实源，必须同步改。**
 * 这是模板条件裁剪的固有代价：启用多语言的场景走词条，不启用的场景没有词条文件
 * （`template.json` 在 `!IncludeLocalization` 时整个排除 `public/i18n/**`），
 * 句子只能内联。组件里 `actorDisplay`、`impersonationHint`、`outcomeLabel` 已是同一形态。
 *
 * 不内联的话，三分之一的场景（identity／resource／standalone）会退回显示裸动作码——
 * 而句子化正是这张表改造的全部价值。
 */
const ACTION_SENTENCES: Record<string, (target: string) => string> = {
  'user.created': (t) => `Created user ${t}`,
  'user.updated': (t) => `Updated user ${t}`,
  'user.deleted': (t) => `Deleted user ${t}`,
  'user.unlocked': (t) => `Unlocked user ${t}`,
  'user.two-factor-reset': (t) => `Reset two-factor authentication for user ${t}`,
  'user.roles-replaced': (t) => `Changed roles for user ${t}`,
  'role.created': (t) => `Created role ${t}`,
  'role.deleted': (t) => `Deleted role ${t}`,
  'permission-grants.replaced': (t) => `Changed permissions for ${t}`,
  'tenant.created': (t) => `Created tenant ${t}`,
  'tenant.updated': (t) => `Updated tenant ${t}`,
  'tenant.activation-changed': (t) => `Changed activation of tenant ${t}`,
  'tenant.deleted': (t) => `Deleted tenant ${t}`,
  'tenant.connection-registered': (t) => `Registered a database connection for tenant ${t}`,
  'tenant.connection-changed': (t) => `Changed the database connection of tenant ${t}`,
  'tenant.connection-removed': (t) => `Removed the database connection of tenant ${t}`,
  'setting.changed': (t) => `Changed setting ${t}`,
  'operation-records.exported': () => 'Exported operation records',
  'auth.login.succeeded': () => 'Signed in',
  'auth.login.failed': (t) => `Failed to sign in (${t})`,
  'auth.locked-out': (t) => `Account ${t} was locked after repeated failed sign-ins`,
  'auth.password.changed': () => 'Changed their own password',
  'auth.avatar.changed': () => 'Changed their avatar',
  'auth.email.verified': () => 'Verified their email address',
  'auth.session.revoked': (t) => `Signed out a device (${t})`,
  'auth.sessions.others-revoked': () => 'Signed out all other devices',
  'auth.two-factor.enabled': () => 'Turned on two-factor authentication',
  'auth.two-factor.disabled': () => 'Turned off two-factor authentication',
  'auth.two-factor.recovery-codes-regenerated': () => 'Regenerated recovery codes',
  'auth.two-factor.recovery-code-used': (t) => `Account ${t} signed in with a recovery code`,
  //#if (ExternalLogin)
  'auth.external-login.linked': (t) => `Linked external account ${t}`,
  'auth.external-login.unlinked': (t) => `Unlinked external account ${t}`,
  //#endif
  'auth.registered': (t) => `Registered account ${t}`,
  'impersonation.started': (t) => `Started acting as ${t}`,
  'impersonation.ended': () => 'Ended impersonation',
  'tenant.impersonation-started': (t) => `Signed in as the administrator of tenant ${t}`,
  'tenant.impersonation-ended': (t) => `Ended impersonation of tenant ${t}`,
  'auth.token.issued': () => 'Issued an access token',
};

/** 无目标时的变体：只有创建类端点在授权阶段被拒时会走到这里（目标记为 `-`）。 */
const ACTION_SENTENCES_NO_TARGET: Record<string, string> = {
  'user.created': 'Created a user',
  'role.created': 'Created a role',
};

/** 失败原因的英文文案，与 `en.json` 的 `operationRecords.failures` 同步。 */
const FAILURE_REASONS: Record<string, string> = {
  Auth_InvalidCredentials: 'Incorrect username or password',
  Auth_UserTemporarilyLockedOut: 'Too many failed sign-in attempts',
};
//#endif

@Component({
  selector: 'app-operation-record-table',
  // 不启用多语言时 TranslocoModule 被守卫剥掉，剩下的 7 项刚好缩到 98 字符（< printWidth 100），
  // prettier 就要求折成一行；带上它又超行、要求展开。同一份源码满足不了两种生成物，
  // 所以固定书写形态——与 default-sidebar 的同类数组一致。
  // prettier-ignore
  imports: [
    AppDate,
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmSpinner,
    TablePaginator,
    ...HlmTableImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [
    provideIcons({
      lucideChevronRight,
      lucideDatabase,
      lucideSearchX,
      lucideUserPen,
    }),
  ],
  templateUrl: './operation-record-table.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperationRecordTable {
  // 时间统一按设置里的展示时区渲染：服务端存 UTC，每处各自用浏览器时区
  // 会让同一时刻在不同页面显示成不同时间。
  protected readonly displayTimeZone = inject(SettingContextService).timeZone;
  protected readonly displayLocale = inject(SettingContextService).displayLocale;
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  readonly records = input<OperationRecordOutputDto[]>([]);
  readonly totalCount = input(0);
  readonly pagination = input<PaginationState>({ pageIndex: 0, pageSize: 20 });
  readonly loading = input(false);
  readonly filtered = input(false);

  readonly paginationChange = output<PaginationState>();

  private readonly tableViewport = tableViewportSignal();

  /**
   * 四列：时间 ｜ 操作人 ｜ 操作内容 ｜ 结果。
   *
   * **操作内容是一句话，且句子里不含操作人**——操作人独立成列。二者若都带人名，
   * 同一个名字会在一行里印两遍。Django admin 的 object_history 正是这个形态
   * （Date/time、User、Action，而 Action 列写的是 "Changed name and email."）。
   *
   * **保留"结果"列而不是把成败写进句子**：NIST AU-3 与 OWASP 都把 success/fail
   * 列为审计记录的必需内容，写进句子就没法独立筛选与告警。
   *
   * 目标标识、授权依据、链路标识不占列——它们只在追查时有人看，进行展开区。
   *
   * **没有操作列**：本页只读，一个行操作也没有。
   */
  protected readonly columns: ColumnDef<AppTableFeatures, OperationRecordOutputDto>[] = [
    {
      accessorKey: 'creationTime',
      id: 'creationTime',
      enableSorting: false,
      enableHiding: false,
      meta: { priority: 'primary', locked: true },
    },
    {
      accessorKey: 'actorName',
      id: 'actor',
      enableSorting: false,
      meta: { priority: 'secondary' },
    },
    {
      accessorKey: 'action',
      id: 'action',
      enableSorting: false,
      enableHiding: false,
      meta: { priority: 'primary', locked: true },
    },
    {
      accessorKey: 'outcome',
      id: 'outcome',
      enableSorting: false,
      enableHiding: false,
      meta: { priority: 'primary', locked: true },
    },
  ];

  private readonly columnVisibility = computed(() =>
    tableColumnVisibility(this.columns, this.tableViewport()),
  );

  private readonly expandableRows = createExpandableRows();

  /**
   * 行展开**始终**可用——这一点与用户列表不同，是有意的。
   *
   * 用户列表的展开只是移动端裁剪的补偿，桌面端无列可补就不给箭头。这里不一样：
   * `correlationId` 和 `actorId` 从不占列（列宽有限，而它们只在追查时才有人看），
   * 所以桌面端同样有内容要展开。跟着 `hasCollapsedColumns` 走的话，
   * 桌面端就永远看不到链路标识——而那恰恰是把这条记录接到日志上的唯一钥匙。
   */
  isRowExpanded(id: string): boolean {
    return this.expandableRows.isExpanded(id);
  }

  toggleRow(id: string): void {
    this.expandableRows.toggle(id);
  }

  isColumnHidden(id: string): boolean {
    return this.table.getColumn(id)?.getIsVisible() === false;
  }

  protected readonly table = injectAppTable(() => ({
    data: this.records(),
    columns: this.columns,
    manualPagination: true,
    rowCount: this.totalCount(),
    onPaginationChange: (updater) =>
      this.paginationChange.emit(resolveTableUpdater(updater, this.pagination())),
    state: {
      columnVisibility: this.columnVisibility(),
      pagination: this.pagination(),
    },
  }));

  readonly currentPage = computed(() => this.pagination().pageIndex + 1);
  readonly totalPages = computed(() => Math.max(1, this.table.getPageCount()));

  isSucceeded(record: OperationRecordOutputDto): boolean {
    return record.outcome === 'Succeeded';
  }

  /**
   * 操作人名可能缺失：宿主没下发 name claim 时只留得下标识。
   *
   * `actorIsTarget` 为真时回落到目标名——登录这类自证动作发生在认证之前，
   * 请求主体当时确实是匿名的（见 `AuthAppService` 的说明），"什么人"由目标承载。
   * **这个判定来自服务端**：此处不按动作码前缀猜，否则前端就复制了一份服务端的动作登记；
   * 更要紧的是 `auth.login.failed` 的目标是调用方提交的用户名，未经验证。
   */
  actorDisplay(record: OperationRecordOutputDto): string {
    const fromTarget = record.actorIsTarget ? record.targetName : undefined;
    //#if (IncludeLocalization)
    return (
      record.actorName ??
      record.actorId ??
      fromTarget ??
      this.transloco.translate('operationRecords.table.unknownActor')
    );
    //#else
    return record.actorName ?? record.actorId ?? fromTarget ?? 'Anonymous';
    //#endif
  }

  /**
   * 模拟登录提示语。
   *
   * 与操作人名**同格显示**，不放进展开区：`actorName` 是被模拟的租户用户，
   * 真正按下按钮的是这里的人。只显示前者会把责任指向一个什么都没做的人。
   */
  impersonationHint(record: OperationRecordOutputDto): string | null {
    if (!record.impersonatorName) {
      return null;
    }
    //#if (IncludeLocalization)
    return this.transloco.translate('operationRecords.table.impersonatedBy', {
      name: record.impersonatorName,
    });
    //#else
    return `acting on behalf, by ${record.impersonatorName}`;
    //#endif
  }

  /**
   * 把一条记录渲染成「操作内容」那一句话。
   *
   * **整句进语言包，绝不在代码里拼片段。** Discourse 的中文译文把占位符顺序整个翻转
   * （en 是"动词+宾语+时间"，zh 是"时间+动词+宾语"），任何 join 片段的写法在那里必然出错。
   *
   * 目标三级降级：有名字用名字 → 只有标识用截断标识 → 无目标（`-`）走 `actionsNoTarget` 变体。
   * 动作码未登记时原样显示裸码——见 {@link translateOrNull} 为什么不能直接用返回值判断。
   */
  actionSentence(record: OperationRecordOutputDto): string {
    //#if (IncludeLocalization)
    const hasTarget = record.targetId !== '-';
    if (!hasTarget) {
      const noTarget = this.translateOrNull(`operationRecords.actionsNoTarget.${record.action}`);
      if (noTarget) {
        return noTarget;
      }
    }

    const sentence = this.translateOrNull(`operationRecords.actions.${record.action}`, {
      target: this.targetDisplay(record),
    });
    // 未登记的动作码（下游业务自定义、尚未补词条）原样显示裸码：
    // 造一个假句子比显示机器码更糟——读者会以为自己看懂了。
    return sentence ?? record.action;
    //#else
    if (record.targetId === '-') {
      const noTarget = ACTION_SENTENCES_NO_TARGET[record.action];
      if (noTarget) {
        return noTarget;
      }
    }

    const template = ACTION_SENTENCES[record.action];
    return template ? template(this.targetDisplay(record)) : record.action;
    //#endif
  }

  /**
   * 目标的显示形态。
   *
   * 授权阶段被拒的记录没有名字，这是**正确**的：那条路径上调用方正因无权访问该目标而被拒，
   * 回填名字等于把他无权查看的内容写进他能读到的记录。此时退到标识，并截断——
   * 一个 36 位 GUID 塞进句子会把整行撑开。
   */
  targetDisplay(record: OperationRecordOutputDto): string {
    if (record.targetName) {
      return record.targetName;
    }
    return record.targetId.length > 12 ? `${record.targetId.slice(0, 8)}…` : record.targetId;
  }

  /**
   * 失败原因：按错误码查词条，查不到就显示原始码。
   *
   * 后端错误码形如 `Auth:InvalidCredentials`，而冒号在词条键里没有先例，
   * 统一换成下划线再查。`failureData` 是后端序列化的 JSON 参数对象，解析失败就当作无参数——
   * 一条审计记录不该因为参数解析不了而整行渲染不出来。
   */
  failureReason(record: OperationRecordOutputDto): string | null {
    if (!record.failureCode) {
      return null;
    }
    //#if (IncludeLocalization)
    const key = `operationRecords.failures.${record.failureCode.replace(/:/g, '_')}`;
    return (
      this.translateOrNull(key, this.parseFailureData(record.failureData)) ?? record.failureCode
    );
    //#else
    return FAILURE_REASONS[record.failureCode.replace(/:/g, '_')] ?? record.failureCode;
    //#endif
  }

  private parseFailureData(raw: string | undefined): Record<string, unknown> {
    if (!raw) {
      return {};
    }
    try {
      const parsed: unknown = JSON.parse(raw);
      return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : {};
    } catch {
      return {};
    }
  }
  //#if (IncludeLocalization)

  /**
   * 查词条，缺失时返回 `null` 而不是键本身。
   *
   * **transloco 的 `DefaultMissingHandler` 缺键时返回 key 原样**（已核官方源码），
   * 所以不能直接用返回值——否则界面会显示成 `operationRecords.actions.foo.bar` 这种丑键。
   * 用"返回值是否等于传入键"来识别缺失。
   */
  private translateOrNull(key: string, params?: Record<string, unknown>): string | null {
    const value = this.transloco.translate(key, params);
    return value === key ? null : value;
  }
  //#endif

  outcomeLabel(record: OperationRecordOutputDto): string {
    //#if (IncludeLocalization)
    return this.transloco.translate(
      this.isSucceeded(record)
        ? 'operationRecords.outcome.succeeded'
        : 'operationRecords.outcome.failed',
    );
    //#else
    return this.isSucceeded(record) ? 'Succeeded' : 'Rejected';
    //#endif
  }

  detailLabel(
    field:
      | 'actor'
      | 'target'
      | 'time'
      | 'basis'
      | 'correlationId'
      | 'actorTenantId'
      | 'failure'
      | 'failureDetail',
  ): string {
    //#if (IncludeLocalization)
    const keys = {
      actor: 'operationRecords.table.colActor',
      target: 'operationRecords.table.colTarget',
      time: 'operationRecords.table.colTime',
      basis: 'operationRecords.table.colBasis',
      correlationId: 'operationRecords.table.colCorrelationId',
      actorTenantId: 'operationRecords.table.colActorTenantId',
      failure: 'operationRecords.table.colFailure',
      failureDetail: 'operationRecords.table.colFailureDetail',
    } as const;
    return this.transloco.translate(keys[field]);
    //#else
    const labels = {
      actor: 'Operator',
      target: 'Target',
      time: 'Time',
      basis: 'Authorized by',
      correlationId: 'Trace ID',
      actorTenantId: 'Operator tenant',
      failure: 'Reason',
      failureDetail: 'Technical detail',
    } as const;
    return labels[field];
    //#endif
  }

  detailsLabel(): string {
    //#if (IncludeLocalization)
    return this.transloco.translate('common.details');
    //#else
    return 'View details';
    //#endif
  }

  /** 每页条数变化：回到第一页并广播新的分页状态。 */
  changePageSize(pageSize: number): void {
    this.paginationChange.emit({ pageIndex: 0, pageSize });
  }

  paginatorLabels(): TablePaginatorLabels {
    //#if (IncludeLocalization)
    return {
      currentPageReport: this.transloco.translate('operationRecords.table.currentPageReport', {
        total: this.totalCount(),
      }),
      rowsPerPage: this.transloco.translate('common.rowsPerPage'),
      page: this.transloco.translate('common.pageOf', {
        page: this.currentPage(),
        total: this.totalPages(),
      }),
      first: this.transloco.translate('common.pagination.first'),
      previous: this.transloco.translate('common.pagination.previous'),
      next: this.transloco.translate('common.pagination.next'),
      last: this.transloco.translate('common.pagination.last'),
    };
    //#else
    return {
      currentPageReport: `${this.totalCount()} in total`,
      rowsPerPage: 'Items per page',
      page: `Page ${this.currentPage()} of ${this.totalPages()}`,
      first: 'First page',
      previous: 'Previous page',
      next: 'Next page',
      last: 'Last page',
    };
    //#endif
  }
}
