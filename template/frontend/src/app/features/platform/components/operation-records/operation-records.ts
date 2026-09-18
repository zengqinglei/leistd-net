import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Params, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBan,
  lucideCircleCheck,
  lucideDownload,
  lucideRefreshCw,
  lucideSearch,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
// 只取这两个：HlmDatePickerImports 是个数组常量，import 它等于把日期/多选/月年等 10 个组件
// 全列进本组件的 imports，本页只用范围选择器与它的输入框形态（官方称 Input Picker）。
// 输入框自带 HlmInputGroup 宿主指令、框内的清空与日历按钮，以及各自的图标——
// 因此本页不必再登记 lucideCalendar / lucideX。
//
// **这么写是为了 imports 面干净，不是为了减小包体。** libs/ui/date-picker 的 index.ts
// 无条件 `export *`，桶文件会把同胞组件一并带进共享 chunk：实测部署产物里仍能搜到
// hlm-date-picker-trigger，而源码侧除本文件注释外已无人引用它。别把这行当成摇树优化。
import { HlmDateRangeInput, HlmDateRangePicker } from '@spartan-ng/helm/date-picker';
import {
  HlmInputGroup,
  HlmInputGroupInput,
  HlmInputGroupAddon,
} from '@spartan-ng/helm/input-group';
// 导出与刷新按钮上的 hlmTooltip 指令要在作用域内，否则它只是个无效属性、不会有提示。
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { PaginationState } from '@tanstack/angular-table';
import { combineLatest, EMPTY, Subject } from 'rxjs';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  finalize,
  startWith,
  switchMap,
  tap,
} from 'rxjs/operators';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { SettingContextService } from '../../../../core/settings/setting-context-service';
import { LayoutService } from '../../../../layout/services/layout-service';
import {
  FacetedFilter,
  type FacetedFilterOption,
} from '../../../../shared/components/faceted-filter/faceted-filter';
import { PERMISSIONS } from '../../../../shared/models/permission';
import { formatAppDate, parseAppCalendarDate } from '../../../../shared/pipes/app-date-pipe';
import { saveBlob } from '../../../../shared/utils/download-file';
import { paginationFromQuery, tableStateToQuery } from '../../../../shared/utils/table-query-state';
import {
  isoToZonedDate,
  zonedEndOfDayIso,
  zonedStartOfDayIso,
} from '../../../../shared/utils/zoned-time';
import {
  ExportOperationRecordsInputDto,
  GetOperationRecordsInputDto,
  OperationRecordFilterOptionsDto,
  OperationRecordOutputDto,
} from '../../models/operation-record.dto';
import { OperationRecordService } from '../../services/operation-record-service';
import { OperationRecordTable } from './widgets/operation-record-table/operation-record-table';

/**
 * 操作记录页。
 *
 * 与用户 / 角色管理页的差别只有两处，都是刻意的：
 *   - **没有新建 / 编辑 / 删除**：审计表只读，能被改写的记录不能当证据；
 *   - **没有排序入口**：后端契约是 offset/limit/keyword 加时间区间，不含排序，固定按时间倒序。
 *     给一个能改排序的控件，只会让人翻出一页"最早的几条"然后以为漏了数据。
 */
@Component({
  selector: 'app-operation-records',
  imports: [
    NgIcon,
    HlmButton,
    HlmInputGroup,
    HlmInputGroupInput,
    HlmInputGroupAddon,
    HlmDateRangePicker,
    HlmDateRangeInput,
    FacetedFilter,
    ...HlmTooltipImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
    OperationRecordTable,
  ],
  providers: [
    // lucideCircleCheck / lucideBan 供结果筛选的选项图标使用（与用户管理页同一组图标）。
    // 漏登记不会报错，只是图标不渲染——这类缺失只能靠肉眼发现。
    provideIcons({
      lucideBan,
      lucideCircleCheck,
      lucideDownload,
      lucideRefreshCw,
      lucideSearch,
    }),
  ],
  templateUrl: './operation-records.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperationRecords {
  private readonly service = inject(OperationRecordService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly layoutService = inject(LayoutService);
  private readonly authorizationService = inject(AuthorizationService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  private readonly searchSubject = new Subject<string>();
  private readonly refreshRequests = new Subject<void>();
  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  records = signal<OperationRecordOutputDto[]>([]);
  totalRecords = signal(0);
  loading = signal(false);

  /** 导出进行中：按钮置灰，避免连点发起多次下载。 */
  readonly exporting = signal(false);

  /**
   * 是否显示导出按钮。
   *
   * 按**导出权限**而不是查看权限裁剪：整批带离系统的影响面与在线翻页不是一个量级。
   * 前端裁剪只影响体验，服务端对 `/export` 仍独立校验同一权限。
   */
  readonly canExport = computed(() =>
    this.authorizationService.has(PERMISSIONS.operationRecords.export),
  );

  // 列表状态全部来源于 URL 查询参数（刷新 / 前进后退 / 分享皆可复原）。
  readonly pagination = computed(() => paginationFromQuery(this.queryParams()));

  // 搜索框即时值：随 URL 回填，输入时乐观更新，防抖后写回 URL。
  readonly searchQuery = signal(this.route.snapshot.queryParamMap.get('keyword') ?? '');

  // 时间统一按展示时区解释：列表里的时间就是按它渲染的，筛选与显示必须同一口径。
  private readonly displayTimeZone = inject(SettingContextService).timeZone;
  private readonly displayLocale = inject(SettingContextService).displayLocale;

  /**
   * 选中的时间范围，从 URL 反向回填。
   *
   * 只有起止都在才算一段区间：选择器要的是 `[Date, Date]`，缺一端就当作未筛选。
   */
  readonly selectedRange = computed<[Date, Date] | undefined>(() => {
    const params = this.queryParams();
    const start = params.get('startTime');
    const end = params.get('endTime');
    if (!start || !end) {
      return undefined;
    }

    const zone = this.displayTimeZone();
    const from = isoToZonedDate(start, zone);
    const to = isoToZonedDate(end, zone);
    return from && to ? [from, to] : undefined;
  });

  /** 服务端下发的筛选项：类别与动作码都不在前端硬编码。 */
  readonly filterOptions = signal<OperationRecordFilterOptionsDto>({ categories: [], actions: [] });

  /** 当前选中的类别（空串表示不筛）。 */
  readonly selectedCategory = computed(() => this.queryParams().get('category') ?? '');

  /** 当前选中的动作码（空串表示不筛）。 */
  readonly selectedAction = computed(() => this.queryParams().get('action') ?? '');

  /** 当前选中的结果（空串表示不筛）。 */
  readonly selectedOutcome = computed(() => this.queryParams().get('outcome') ?? '');

  /**
   * 动作下拉的候选：选了类别就只列该类别下的动作。
   *
   * 不做联动的话，用户选了"认证"类别，动作下拉里却仍列着全部 20 个码——
   * 选中一个跨类别的动作会让两个条件相交为空，界面显示"没有匹配"，
   * 而人看不出是自己把两个条件选矛盾了。
   */
  readonly actionOptions = computed(() => {
    const category = this.selectedCategory();
    const all = this.filterOptions().actions;
    return category ? all.filter((option) => option.category === category) : all;
  });

  /**
   * 是否处于筛选态：用于区分「暂无数据」与「无匹配结果」的空状态。
   *
   * **时间区间必须算进来**：否则选了一段没有记录的时间时，界面会说"暂无操作记录"，
   * 让人以为系统从未记录过任何操作，而事实只是这段时间内没有。
   * 类别／动作／结果同理——任何一个生效都属于筛选态。
   */
  readonly hasActiveFilters = computed(
    () =>
      this.searchQuery().trim().length > 0 ||
      this.selectedRange() !== undefined ||
      this.selectedCategory() !== '' ||
      this.selectedAction() !== '' ||
      this.selectedOutcome() !== '',
  );

  //#if (IncludeLocalization)
  // 追踪「翻译就绪」：资源加载完成与语言切换时重算，含首帧避免裸键。
  private readonly translationReady = translationReady(this.transloco);

  readonly searchPlaceholder = () =>
    this.transloco.translate('operationRecords.filter.searchPlaceholder');
  readonly refreshLabel = () => this.transloco.translate('common.refresh');
  readonly dateRangeLabel = () => this.transloco.translate('operationRecords.filter.dateRange');
  readonly categoryLabel = () => this.transloco.translate('operationRecords.filter.category');
  readonly actionLabel = () => this.transloco.translate('operationRecords.filter.action');
  readonly outcomeLabel = () => this.transloco.translate('operationRecords.filter.outcome');
  readonly exportLabel = () => this.transloco.translate('operationRecords.filter.export');
  // 清空 / 空结果两条复用公共词条：其它列表页的筛选器用的就是它们，
  // 另起一份会让同一句话在不同页面里出现两种译法。
  readonly clearFilterLabel = () => this.transloco.translate('common.clearFilter');
  readonly filterEmptyLabel = () => this.transloco.translate('common.noResults');

  /** 类别的展示名；没有词条就退回原始类别标识。 */
  categoryText(category: string): string {
    const key = `operationRecords.categories.${category}`;
    const value = this.transloco.translate(key);
    return value === key ? category : value;
  }

  /** 动作的展示名：复用列表里的句子模板键，没有就退回裸码。 */
  actionText(code: string): string {
    const key = `operationRecords.actions.${code}`;
    const value = this.transloco.translate(key, { target: '' });
    return value === key ? code : value.trim();
  }

  outcomeText(outcome: string): string {
    return this.transloco.translate(
      outcome === 'Succeeded'
        ? 'operationRecords.outcome.succeeded'
        : 'operationRecords.outcome.failed',
    );
  }
  //#else
  readonly searchPlaceholder = () => 'Search action / target / operator...';
  readonly refreshLabel = () => 'Refresh';
  readonly dateRangeLabel = () => 'Date range';
  readonly categoryLabel = () => 'Category';
  readonly actionLabel = () => 'Action';
  readonly outcomeLabel = () => 'Outcome';
  readonly exportLabel = () => 'Export';
  readonly clearFilterLabel = () => 'Clear filter';
  readonly filterEmptyLabel = () => 'No results';

  categoryText(category: string): string {
    return category;
  }

  actionText(code: string): string {
    return code;
  }

  outcomeText(outcome: string): string {
    return outcome === 'Succeeded' ? 'Succeeded' : 'Rejected';
  }
  //#endif

  /** 结果的候选值，与后端枚举一致。 */
  private readonly outcomeValues = ['Succeeded', 'Failed'] as const;

  /**
   * 三个筛选器的候选项。
   *
   * 与其它列表页一样交给 `app-faceted-filter`：它自带搜索框、清空与选中态 Badge。
   * **动作与类别会随业务增长**，用固定下拉迟早翻不完，而搜索是这个组件本来就有的能力。
   *
   * 本地化分支里读一次 `translationReady()` 建立依赖：资源就绪与切换语言时重算，
   * 否则切了语言、选项文案还停在上一种——`users.ts` 为同一理由做了同样的事。
   */
  //#if (IncludeLocalization)
  readonly categoryFilterOptions = computed<FacetedFilterOption[]>(() => {
    this.translationReady();
    return this.filterOptions().categories.map((category) => ({
      value: category,
      label: this.categoryText(category),
    }));
  });

  readonly actionFilterOptions = computed<FacetedFilterOption[]>(() => {
    this.translationReady();
    return this.actionOptions().map((option) => ({
      value: option.code,
      label: this.actionText(option.code),
    }));
  });

  readonly outcomeFilterOptions = computed<FacetedFilterOption[]>(() => {
    this.translationReady();
    return this.outcomeValues.map((outcome) => ({
      value: outcome,
      label: this.outcomeText(outcome),
      icon: outcome === 'Succeeded' ? 'lucideCircleCheck' : 'lucideBan',
    }));
  });
  //#else
  readonly categoryFilterOptions = computed<FacetedFilterOption[]>(() =>
    this.filterOptions().categories.map((category) => ({
      value: category,
      label: this.categoryText(category),
    })),
  );

  readonly actionFilterOptions = computed<FacetedFilterOption[]>(() =>
    this.actionOptions().map((option) => ({
      value: option.code,
      label: this.actionText(option.code),
    })),
  );

  readonly outcomeFilterOptions = computed<FacetedFilterOption[]>(() =>
    this.outcomeValues.map((outcome) => ({
      value: outcome,
      label: this.outcomeText(outcome),
      icon: outcome === 'Succeeded' ? 'lucideCircleCheck' : 'lucideBan',
    })),
  );
  //#endif

  /**
   * 三个下拉的取名函数。
   *
   * **必须写成箭头函数属性，不能写成方法**：`itemToString` 是 signal input，
   * 传方法引用会在每轮变更检测里得到新的函数对象，让下拉以为输入变了而反复重算。
   * `settings.ts` 为同一原因做了按设置名缓存闭包，并把理由写在那里；
   * 本页的取名不带参数化状态，因此用固定的属性引用即可。
   *
   * 空串是"不筛"的哨兵值，触发器此时应显示 placeholder 而非空白。
   */
  constructor() {
    this.searchSubject
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe((keyword) => this.updateQuery({ keyword: keyword.trim() || null, page: 1 }, true));

    // 搜索框即时值随 URL 回填（前进后退 / 分享链接场景）。
    effect(() => this.searchQuery.set(this.queryParams().get('keyword') ?? ''));

    // 筛选项由服务端下发，加载一次即可：动作定义是启动期事实，不随请求变化。
    // 失败时不打扰用户——筛选项拿不到只会让下拉是空的，而列表本身照常可用；
    // 弹一个错误提示反而会让人以为整页坏了。
    this.service
      .getFilterOptions()
      .pipe(
        catchError(() => EMPTY),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((options) => this.filterOptions.set(options));

    // URL 变化或显式刷新时重新拉取列表。
    combineLatest([this.route.queryParamMap, this.refreshRequests.pipe(startWith(undefined))])
      .pipe(
        tap(() => this.loading.set(true)),
        switchMap(([params]) =>
          this.service.getOperationRecords(this.queryFromParams(params)).pipe(
            catchError((error: unknown) => {
              this.showRequestError(error);
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((data) => {
        this.records.set(data.items);
        this.totalRecords.set(data.totalCount);
      });
    //#if (IncludeLocalization)

    // 读 translationReady 建立依赖：资源就绪 / 语言切换时标题随之重设。
    effect(() => {
      this.translationReady();
      this.layoutService.title.set(this.transloco.translate('operationRecords.page.title'));
    });
    //#else

    this.layoutService.title.set('Operation records');
    //#endif
  }

  onSearchQueryChange(value: string) {
    this.searchQuery.set(value);
    this.searchSubject.next(value);
  }

  /**
   * 类别变化：同时清掉动作选择。
   *
   * 换了类别却留着上一个类别的动作，两个条件会相交为空——界面显示"没有匹配"，
   * 而人看不出是自己把条件选矛盾了。
   *
   * 三个回调都先比对当前值再写 URL。**这不是为了防某个组件的重复事件**——
   * 写回 URL 会触发 `queryParamMap` 变化进而重新拉取列表，值没变时跳过可以
   * 省掉一次无谓的请求，也避免在浏览器历史里堆出一串内容相同的条目。
   */
  onCategoryChange(value: string | boolean | null): void {
    const next = value == null ? '' : String(value);
    if (next === this.selectedCategory()) {
      return;
    }
    // 换类别时清掉动作：动作候选随类别裁剪，留着上一个类别的动作会让两个条件
    // 相交为空，界面显示"没有匹配"，而人看不出是自己把条件选矛盾了。
    this.updateQuery({ category: next || null, action: null, page: 1 });
  }

  onActionChange(value: string | boolean | null): void {
    const next = value == null ? '' : String(value);
    if (next === this.selectedAction()) {
      return;
    }
    this.updateQuery({ action: next || null, page: 1 });
  }

  onOutcomeChange(value: string | boolean | null): void {
    const next = value == null ? '' : String(value);
    if (next === this.selectedOutcome()) {
      return;
    }
    this.updateQuery({ outcome: next || null, page: 1 });
  }

  onPaginationChange(pagination: PaginationState) {
    // 本页无排序：传空排序状态，让分页参数与其他列表页保持同一套 URL 约定。
    this.updateQuery(tableStateToQuery(pagination, []));
  }

  /**
   * 输入框里显示的区间文案，同时交给 `formatDates`（展示态）与 `formatInputDates`（编辑态）。
   *
   * 日期写法与列表的时间列同源（`formatAppDate` 的 `date` 槽位）、随界面语言变化：
   * 中文 `2026-09-17 ~ 2026-09-18`，英文 `Sep 17, 2026 ~ Sep 18, 2026`。
   * 写成返回函数的 `computed`：函数被当作回调传进子组件（普通方法会丢掉 `this`），
   * 切换语言时给出一个新函数，输入框才会按新写法重渲染。
   *
   * **两态共用同一个函数是刻意的**：`formatInputDates` 若与 `formatDates` 不同，
   * 用户一聚焦输入框，框里的文本就会当场变成另一种写法，看上去像自己的输入被改掉了。
   *
   * **只读本地字段，不再套 `Intl` 的 `timeZone`。**
   * `selectedRange()` 的两个 `Date` 来自 `isoToZonedDate`，它返回的是**浏览器本地** `Date`，
   * 其年月日**已经**等于该时刻在展示时区里的日历日；`zonedStartOfDayIso` / `zonedEndOfDayIso`
   * 也是按本地字段读回去的。此处若再按 `timeZone` 格式化，就是把偏移折第二遍——
   * 浏览器时区领先于展示时区时会显示成前一天：选 17 号、框里写 16 号、
   * 筛出来的仍是 17 号的数据，且不报任何错。
   *
   * 缺一端时只渲染已选的那一端：区间选择过程中会短暂处于"只有起点"的状态，
   * 而空区间根本不会走到这里（`formattedDate` 在两端皆空时返回 undefined，
   * 由输入框的 placeholder 接管）。
   */
  readonly formatRange = computed(() => {
    const locale = this.displayLocale();
    // 不传时区：两个 Date 的本地字段已经是展示时区的日历日（见上），再按时区渲染会折两遍偏移。
    const day = (date: Date | null) => (date ? formatAppDate(date, 'date', undefined, locale) : '');
    return (dates: [Date | null, Date | null]): string => {
      const [from, to] = dates;
      if (!from && !to) {
        return '';
      }

      return `${day(from)} ~ ${day(to)}`.trim();
    };
  });

  /**
   * 解析用户手输的区间，交给 `hlm-date-range-input` 的 `parseDate`。
   *
   * 认 {@link formatRange} 写出的当前语言写法，也认 `YYYY-MM-DD`（见 `parseAppCalendarDate`）。构造出的是
   * **浏览器本地** `Date`——与 `isoToZonedDate` 的约定一致，随后 `onRangeChange`
   * 会用 `zonedStartOfDayIso` / `zonedEndOfDayIso` 按展示时区换算成 UTC。
   *
   * 解析不了就返回 `null`：组件会清掉区间但**保留文本**，让人能接着改，
   * 而不是把输入吞掉。
   */
  readonly parseRangeInput = computed(() => {
    const locale = this.displayLocale();
    return (value: string): [Date, Date] | null => {
      const parts = value
        .split('~')
        .map((part) => part.trim())
        .filter(Boolean);
      if (parts.length !== 2) {
        return null;
      }

      const from = parseAppCalendarDate(parts[0], locale);
      const to = parseAppCalendarDate(parts[1], locale);
      return from && to ? [from, to] : null;
    };
  });

  /**
   * 时间范围变化：按展示时区取整天，再换算成 UTC 写回 URL。
   *
   * 起点取当天 00:00:00.000、终点取当天 23:59:59.999——后端是闭区间，
   * 终点若停在 00:00:00，选中的最后一天会整天查不到记录。
   */
  onRangeChange(range: [Date, Date] | null): void {
    if (!range?.[0] || !range[1]) {
      this.updateQuery({ startTime: null, endTime: null, page: 1 });
      return;
    }

    const zone = this.displayTimeZone();
    this.updateQuery({
      startTime: zonedStartOfDayIso(range[0], zone),
      endTime: zonedEndOfDayIso(range[1], zone),
      page: 1,
    });
  }

  reloadList() {
    this.refreshRequests.next();
  }

  /**
   * 导出当前筛选条件下的记录。
   *
   * 筛选参数来自 {@link filtersFromParams}，与列表查询**同一份构造**——
   * 另拼一份的话，界面上筛的和导出的迟早不是同一批，而文件本身看不出这个差别。
   *
   * **不是全量导出**：后端取筛选结果的前 N 条（10000 封顶）。
   */
  onExport(): void {
    if (this.exporting()) {
      return;
    }

    this.exporting.set(true);
    this.service
      .exportOperationRecords(this.filtersFromParams(this.queryParams()))
      .pipe(
        finalize(() => this.exporting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        // 文件名在客户端生成：`responseType: 'blob'` 拿不到 `Content-Disposition`
        // （那要额外读响应头），而服务端已经给了同样形状的名字，这里保持一致即可。
        next: (blob) =>
          saveBlob(
            blob,
            `operation-records-${new Date().toISOString().slice(0, 19).replace(/[-:T]/g, '')}.csv`,
          ),
        error: (error: unknown) => this.showExportError(error),
      });
  }

  /**
   * 纯筛选条件（不含分页）。
   *
   * **列表查询与导出共用这一份**：两处各拼一次的话迟早漂移，症状是"导出的内容
   * 与屏幕上筛出来的不一致"，而文件本身看不出这个差别。
   */
  private filtersFromParams(params: ParamMap): Omit<ExportOperationRecordsInputDto, 'limit'> {
    return {
      keyword: params.get('keyword') || undefined,
      // URL 里存的已是 UTC ISO 串，直接透传；换算只在写入 URL 时做一次。
      startTime: params.get('startTime') || undefined,
      endTime: params.get('endTime') || undefined,
      // 服务端接的是数组（List<string>），界面目前一次只筛一个，因此包成单元素数组下传——
      // 契约按数组定，将来换成多选下拉时这里不用改。
      categories: params.get('category') ? [params.get('category')!] : undefined,
      actions: params.get('action') ? [params.get('action')!] : undefined,
      outcome: params.get('outcome') || undefined,
    };
  }

  private queryFromParams(params: ParamMap): GetOperationRecordsInputDto {
    const pagination = paginationFromQuery(params);
    return {
      offset: pagination.pageIndex * pagination.pageSize,
      limit: pagination.pageSize,
      ...this.filtersFromParams(params),
    };
  }

  private updateQuery(queryParams: Params, replaceUrl = false): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  private showExportError(error: unknown): void {
    //#if (IncludeLocalization)
    toast.error(this.transloco.translate('operationRecords.filter.exportFailed'), {
      description: applicationErrorMessage(error),
    });
    //#else
    toast.error('Export failed', { description: applicationErrorMessage(error) });
    //#endif
  }

  private showRequestError(error: unknown): void {
    //#if (IncludeLocalization)
    toast.error(this.transloco.translate('common.requestError'), {
      description: applicationErrorMessage(error),
    });
    //#else
    toast.error('Request failed', { description: applicationErrorMessage(error) });
    //#endif
  }
}
