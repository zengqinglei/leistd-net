import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronRight, lucideDatabase, lucideSearchX, lucideUserPen } from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ColumnDef, PaginationState } from '@tanstack/angular-table';

//#if (IncludeLocalization)
import { textAt } from '../../../../../../core/i18n/translation-text';
//#endif
import { SettingContextService } from '../../../../../../core/settings/setting-context-service';
import { TablePaginator } from '../../../../../../shared/components/table-paginator/table-paginator';
import { tableColumnVisibility } from '../../../../../../shared/models/table-column-meta';
import {
  injectAppTable,
  type AppTableFeatures,
} from '../../../../../../shared/models/table-features';
import { AppDate } from '../../../../../../shared/pipes/app-date-pipe';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
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
 * 句子只能内联。
 *
 * 不内联的话，三分之一的场景（identity／resource／standalone）会退回显示裸动作码——
 * 而句子化正是这张表改造的全部价值。
 */
const ACTION_SENTENCES: Record<string, (target: string) => string> = {
  'user.created': (t) => `Created user ${t}`,
  'user.updated': (t) => `Updated user ${t}`,
  'user.deleted': (t) => `Deleted user ${t}`,
  'user.enabled': (t) => `Enabled user ${t}`,
  'user.disabled': (t) => `Disabled user ${t}`,
  'user.password-reset': (t) => `Reset the password of user ${t}`,
  'user.unlocked': (t) => `Unlocked user ${t}`,
  'user.two-factor-reset': (t) => `Reset two-factor authentication for user ${t}`,
  'user.roles-replaced': (t) => `Changed roles for user ${t}`,
  'role.created': (t) => `Created role ${t}`,
  'role.deleted': (t) => `Deleted role ${t}`,
  //#if (RemoteTokenAuth)
  'resource.admin-granted': (t) => `Granted administrator permissions to ${t}`,
  //#endif
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

/**
 * 失败原因的英文文案，键是失败码。
 *
 * 不启用多语言时后端没有本地化器，`failureMessage` 恒为空，常见的失败码只能在这里备英文句子；
 * 与后端 `Api/Resources/en.json` 同步：有 `{码}:Record` 的取它，否则取码本身。
 * 占位符 `{name}` 与后端同一写法，用记录的 `failureData` 填，键名区分大小写。
 */
const FAILURE_REASONS: Record<string, string> = {
  'Auth:InvalidCredentials':
    'Incorrect username or password (failed attempts in the last {windowMinutes} minutes: {attempts})',
  'Auth:UserTemporarilyLockedOut':
    'Locked out for {minutes} minutes after {maxFailedAttempts} failed sign-in attempts',
  // 框架唯一自产的失败码（RecordDeniedOperationAsync 的默认原因），没有这条会显示裸码
  'Error:Forbidden': 'Not allowed to perform this action.',
  // 再认证失败：改口令、停用两步验证、重发恢复码这三处
  'Security:CurrentPasswordIncorrect': 'The current password is incorrect.',
  'Auth:TwoFactorCodeInvalid': 'The verification code is incorrect.',
};
//#endif

/** 「操作内容」那句话：`key` 非空时模板经 t 取词条并填 `params`，否则直接显示 `text`。 */
interface ActionSentence {
  key: string | null;
  params: Record<string, string>;
  text: string;
}

@Component({
  selector: 'app-operation-record-table',
  // 不启用多语言时 TranslocoDirective 被守卫剥掉，剩下的 7 项刚好缩到 98 字符（< printWidth 100），
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
    TranslocoDirective,
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
  /** 动作句子模板整段取成对象，只用来判断动作码登记了没有：句子本身在模板里经 t 带参数取。 */
  private readonly actionTexts = translateObjectSignal(
    'operationRecords.actions',
    {},
    { scope: 'operationRecords' },
  );
  private readonly actionNoTargetTexts = translateObjectSignal(
    'operationRecords.actionsNoTarget',
    {},
    { scope: 'operationRecords' },
  );
  //#else
  protected readonly t = englishText(ENGLISH);
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

  isColumnHidden(id: string): boolean {
    return this.table.getColumn(id)?.getIsVisible() === false;
  }

  protected readonly table = injectAppTable(() => ({
    data: this.records(),
    columns: this.columns,
    getRowId: (row) => row.id,
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
   * 操作人；都缺失时为 undefined，模板显示"匿名"。名字可能缺失：宿主没下发 name claim 时只留得下标识。
   *
   * `actorIsTarget` 为真时回落到目标名——登录这类自证动作发生在认证之前，
   * 请求主体当时确实是匿名的（见 `AuthAppService` 的说明），"什么人"由目标承载。
   * **这个判定来自服务端**：此处不按动作码前缀猜，否则前端就复制了一份服务端的动作登记；
   * 更要紧的是 `auth.login.failed` 的目标是调用方提交的用户名，未经验证。
   */
  actorOf(record: OperationRecordOutputDto): string | undefined {
    const fromTarget = record.actorIsTarget ? record.targetName : undefined;
    return record.actorName ?? record.actorId ?? fromTarget;
  }

  /**
   * 每条记录的操作句子与失败原因，按记录 Id 取。
   *
   * 句子要在词条缺失时降级为裸码，模板里的 t 表达不了"缺词条"，所以在这里判定取哪条词条；
   * 本地化形态下句子本身由模板经 t 取，词条到达与语言切换时随之更新。
   */
  protected readonly recordTexts = computed(
    () =>
      new Map(
        this.records().map((record) => [
          record.id,
          { sentence: this.actionSentence(record), failure: this.failureReason(record) },
        ]),
      ),
  );

  /**
   * 把一条记录渲染成「操作内容」那一句话：给出词条键与参数，或（未登记时）原样的文字。
   *
   * **整句进语言包，绝不在代码里拼片段。** Discourse 的中文译文把占位符顺序整个翻转
   * （en 是"动词+宾语+时间"，zh 是"时间+动词+宾语"），任何 join 片段的写法在那里必然出错。
   *
   * 目标三级降级：有名字用名字 → 只有标识用截断标识 → 无目标（`-`）走 `actionsNoTarget` 变体。
   * 动作码未登记时原样显示裸码：造一个假句子比显示机器码更糟——读者会以为自己看懂了。
   */
  private actionSentence(record: OperationRecordOutputDto): ActionSentence {
    //#if (IncludeLocalization)
    if (
      record.targetId === '-' &&
      textAt(this.actionNoTargetTexts(), record.action) !== undefined
    ) {
      return { key: `operationRecords.actionsNoTarget.${record.action}`, params: {}, text: '' };
    }

    // 未登记的动作码（下游业务自定义、尚未补词条）原样显示裸码
    return textAt(this.actionTexts(), record.action) === undefined
      ? { key: null, params: {}, text: record.action }
      : {
          key: `operationRecords.actions.${record.action}`,
          params: { target: this.targetDisplay(record) },
          text: '',
        };
    //#else
    if (record.targetId === '-') {
      const noTarget = ACTION_SENTENCES_NO_TARGET[record.action];
      if (noTarget) {
        return { key: null, params: {}, text: noTarget };
      }
    }

    const template = ACTION_SENTENCES[record.action];
    const text = template ? template(this.targetDisplay(record)) : record.action;
    return { key: null, params: {}, text };
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
   * 失败原因：显示后端按当前语言渲染的 `failureMessage`，取不到时回落到原始码。
   *
   * 审计专用的措辞与参数（登录失败次数、锁定时长）也由后端的 `{码}:Record` 词条给出，前端不再备词条。
   */
  private failureReason(record: OperationRecordOutputDto): string | null {
    if (!record.failureCode) {
      return null;
    }
    //#if (IncludeLocalization)
    return record.failureMessage ?? record.failureCode;
    //#else
    const text = FAILURE_REASONS[record.failureCode];
    return (
      record.failureMessage ??
      (text ? this.fillPlaceholders(text, this.parseFailureData(record.failureData)) : null) ??
      record.failureCode
    );
    //#endif
  }
  //#if (!IncludeLocalization)

  /** 与后端 `LocalizationPlaceholders.Fill` 同一规则：按名替换 `{name}`，没有对应参数的原样保留。 */
  private fillPlaceholders(text: string, data: Record<string, unknown>): string {
    return text.replace(/\{(\w+)\}/g, (placeholder, name: string) =>
      Object.hasOwn(data, name) ? String(data[name] ?? '') : placeholder,
    );
  }

  /** `failureData` 是后端序列化的 JSON 参数对象；解析失败按无参数处理，一条坏记录不该整行渲染不出来。 */
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
  //#endif

  /** 每页条数变化：回到第一页并广播新的分页状态。 */
  changePageSize(pageSize: number): void {
    this.paginationChange.emit({ pageIndex: 0, pageSize });
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'operationRecords.table.colAction': 'Action',
  'operationRecords.table.colOutcome': 'Outcome',
  'operationRecords.table.colActor': 'Operator',
  'operationRecords.table.colTime': 'Time',
  'common.details': 'View details',
  'operationRecords.outcome.succeeded': 'Succeeded',
  'operationRecords.outcome.failed': 'Rejected',
  'operationRecords.table.unknownActor': 'Anonymous',
  'operationRecords.table.impersonatedBy': 'acting on behalf, by {{name}}',
  'operationRecords.table.colTarget': 'Target',
  'operationRecords.table.colFailure': 'Reason',
  'operationRecords.table.colFailureDetail': 'Technical detail',
  'operationRecords.table.colBasis': 'Authorized by',
  'operationRecords.table.colActorTenantId': 'Operator tenant',
  'operationRecords.table.colCorrelationId': 'Trace ID',
  'operationRecords.table.emptyFilteredTitle': 'No matching records',
  'operationRecords.table.emptyFilteredHint': 'Adjust the search',
  'operationRecords.table.emptyTitle': 'No operation records yet',
  'operationRecords.table.emptyHint': 'Records appear here as soon as an audited operation runs',
  'operationRecords.table.currentPageReport': '{{total}} in total',
  'common.rowsPerPage': 'Items per page',
  'common.pageOf': 'Page {{page}} of {{total}}',
  'common.pagination.first': 'First page',
  'common.pagination.previous': 'Previous page',
  'common.pagination.next': 'Next page',
  'common.pagination.last': 'Last page',
};
//#endif
