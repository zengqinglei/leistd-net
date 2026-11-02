import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  input,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FormField, form, maxLength, required, validate } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideX } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckbox } from '@spartan-ng/helm/checkbox';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSeparator } from '@spartan-ng/helm/separator';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTextarea } from '@spartan-ng/helm/textarea';
import { firstValueFrom, Observable } from 'rxjs';

import { DialogClose } from '../../../../../../core/components/dialog-close/dialog-close';
import { injectFormSubmission } from '../../../../../../core/validation/form-submission';
import { DialogLoading } from '../../../../../../shared/components/dialog-loading/dialog-loading';
import { DialogFocus } from '../../../../../../shared/directives/dialog-focus';
import { ImeGuard } from '../../../../../../shared/directives/ime-guard';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import {
  CreateWebhookSubscriptionInputDto,
  WebhookEventTypeOutputDto,
  WebhookSubscriptionOutputDto,
} from '../../../../dtos/webhook.dto';
import { WEBHOOK_ALL_EVENTS, WEBHOOK_TEST_EVENT } from '../../../../services/webhook-service';

/** 与后端入参常量一致（`CreateWebhookSubscriptionInputDto`）。 */
const MAX_NAME_LENGTH = 128;
const MAX_DESCRIPTION_LENGTH = 512;
const MAX_URL_LENGTH = 2048;
const MAX_HEADER_COUNT = 20;

interface HeaderRow {
  /** 行标识，只用于 `@for` 跟踪。 */
  key: number;
  name: string;
  value: string;
}

interface SubscriptionFormModel {
  name: string;
  url: string;
  description: string;
  /** 勾选"全部事件"时为 `['*']`。 */
  eventTypes: string[];
  headers: HeaderRow[];
}

/** 地址须是绝对 http(s) URL；内网地址在发送时由组件拦截，这里不判断。 */
function isAbsoluteHttpUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return (url.protocol === 'http:' || url.protocol === 'https:') && !/\s/.test(value);
  } catch {
    return false;
  }
}

/**
 * 新建 / 编辑订阅对话框，三段：基本信息（名称、端点地址、说明）、订阅事件（含"全部事件"）、自定义请求头。
 * 保存经父页面的 `saveAction` 发请求，成功副作用（关闭、揭示密钥、刷新列表）也由父页面处理。
 */
@Component({
  selector: 'app-webhook-subscription-dialog',
  imports: [
    DialogFocus,
    ImeGuard,
    DialogClose,
    DialogLoading,
    FormField,
    NgIcon,
    HlmButton,
    HlmCheckbox,
    HlmInput,
    HlmSeparator,
    HlmSpinner,
    HlmTextarea,
    ...HlmDialogImports,
    ...HlmFieldImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucidePlus, lucideX })],
  templateUrl: './webhook-subscription-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WebhookSubscriptionDialog {
  private readonly formRoot = viewChild.required<ElementRef<HTMLElement>>('subscriptionFormRoot');
  private readonly submitForm = injectFormSubmission();
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
  protected readonly maxHeaderCount = MAX_HEADER_COUNT;

  readonly visible = model(false);
  /** 编辑模式：打开时还在取详情，或已取到订阅。 */
  readonly editing = input(false);
  readonly loading = input(false);
  readonly loadError = input<string | null>(null);
  readonly retry = output<void>();
  readonly subscription = input<WebhookSubscriptionOutputDto | null>(null);
  /** 可订阅的事件类型（服务端登记表）；取失败时可重试。 */
  readonly eventTypes = input<WebhookEventTypeOutputDto[]>([]);
  readonly eventTypesLoading = input(false);
  readonly eventTypesError = input<string | null>(null);
  readonly reloadEventTypes = output<void>();
  readonly saveAction =
    input.required<(dto: CreateWebhookSubscriptionInputDto) => Observable<unknown>>();

  private headerKey = 0;
  protected readonly formModel = signal<SubscriptionFormModel>(this.emptyModel());
  readonly isEditMode = computed(() => this.editing() || !!this.subscription());
  readonly saving = computed(() => this.subscriptionForm().submitting());
  readonly allEvents = computed(() => this.formModel().eventTypes.includes(WEBHOOK_ALL_EVENTS));

  readonly subscriptionForm = form(this.formModel, (path) => {
    required(path.name);
    maxLength(path.name, MAX_NAME_LENGTH);
    required(path.url);
    maxLength(path.url, MAX_URL_LENGTH);
    validate(path.url, (ctx) =>
      ctx.value() && !isAbsoluteHttpUrl(ctx.value()) ? { kind: 'uri' } : null,
    );
    maxLength(path.description, MAX_DESCRIPTION_LENGTH);
    validate(path.eventTypes, (ctx) => (ctx.value().length === 0 ? { kind: 'required' } : null));
    // 名称与值要成对：只填了一半的行不提交
    validate(path.headers, (ctx) =>
      ctx.value().some((row) => !row.name.trim() !== !row.value.trim())
        ? { kind: 'required' }
        : null,
    );
  });

  /** 按分组列出可订阅的事件；测试事件不论订阅了什么都会发，不列出。 */
  readonly eventGroups = computed(() => {
    const groups = new Map<string, WebhookEventTypeOutputDto[]>();
    for (const type of this.eventTypes()) {
      if (type.name === WEBHOOK_TEST_EVENT) {
        continue;
      }
      groups.set(type.group, [...(groups.get(type.group) ?? []), type]);
    }
    return [...groups].map(([group, types]) => ({ group, types }));
  });

  constructor() {
    // 每次打开都重置表单（借 visible 触发：subscription 从 null 到 null 不变化时 effect 不会重跑）
    effect(() => {
      const subscription = this.subscription();
      if (!this.visible()) {
        return;
      }
      this.subscriptionForm().reset();
      this.formModel.set(
        subscription
          ? {
              name: subscription.name,
              url: subscription.url,
              description: subscription.description ?? '',
              eventTypes: [...subscription.eventTypes],
              headers: Object.entries(subscription.headers ?? {}).map(([name, value]) =>
                this.headerRow(name, value),
              ),
            }
          : this.emptyModel(),
      );
    });
  }

  isEventSelected(name: string): boolean {
    return this.allEvents() || this.formModel().eventTypes.includes(name);
  }

  setAllEvents(checked: boolean): void {
    this.updateModel((model) => ({ ...model, eventTypes: checked ? [WEBHOOK_ALL_EVENTS] : [] }));
    this.subscriptionForm.eventTypes().markAsTouched();
  }

  toggleEvent(name: string, checked: boolean): void {
    this.updateModel((model) => ({
      ...model,
      eventTypes: checked
        ? [...model.eventTypes.filter((type) => type !== name), name]
        : model.eventTypes.filter((type) => type !== name),
    }));
    this.subscriptionForm.eventTypes().markAsTouched();
  }

  addHeader(): void {
    this.updateModel((model) => ({ ...model, headers: [...model.headers, this.headerRow()] }));
  }

  removeHeader(key: number): void {
    this.updateModel((model) => ({
      ...model,
      headers: model.headers.filter((row) => row.key !== key),
    }));
  }

  setHeader(key: number, field: 'name' | 'value', value: string): void {
    this.updateModel((model) => ({
      ...model,
      headers: model.headers.map((row) => (row.key === key ? { ...row, [field]: value } : row)),
    }));
  }

  /** 桥接 hlm-dialog 声明式 state 到对外 visible 契约。 */
  onDialogStateChange(state: BrnDialogState): void {
    this.visible.set(state === 'open');
  }

  async save(): Promise<void> {
    if (this.loading() || this.loadError() || this.eventTypesLoading()) return;
    this.formModel.update((model) => ({
      ...model,
      name: model.name.trim(),
      description: model.description.trim(),
      // 两格都空的行视为没填
      headers: model.headers.filter((row) => row.name.trim() || row.value.trim()),
    }));
    await this.submitForm(this.subscriptionForm, this.formRoot().nativeElement, async () => {
      const model = this.formModel();
      await firstValueFrom(
        this.saveAction()({
          name: model.name,
          url: model.url,
          description: model.description || null,
          eventTypes: model.eventTypes,
          headers: Object.fromEntries(
            model.headers.map((row) => [row.name.trim(), row.value.trim()]),
          ),
        }),
        { defaultValue: undefined },
      );
    });
  }

  private updateModel(change: (model: SubscriptionFormModel) => SubscriptionFormModel): void {
    this.subscriptionForm().markAsDirty();
    this.formModel.update(change);
  }

  private headerRow(name = '', value = ''): HeaderRow {
    this.headerKey += 1;
    return { key: this.headerKey, name, value };
  }

  private emptyModel(): SubscriptionFormModel {
    return { name: '', url: '', description: '', eventTypes: [], headers: [] };
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'app.startup.loading': 'Loading...',
  'common.cancel': 'Cancel',
  'common.create': 'Create',
  'common.remove': 'Remove',
  'common.retry': 'Retry',
  'common.save': 'Save',
  'webhooks.dialog.addHeader': 'Add header',
  'webhooks.dialog.createHeader': 'New subscription',
  'webhooks.dialog.description':
    'Choose the events to push. A signing secret is generated on save and shown only once.',
  'webhooks.dialog.editHeader': 'Edit subscription',
  'webhooks.dialog.eventsRequired': 'Select at least one event.',
  'webhooks.dialog.headerIncomplete': 'Each header needs both a name and a value.',
  'webhooks.dialog.headersHint':
    'Sent with every request. webhook-* and Content-Type are set by the system.',
  'webhooks.dialog.loading': 'Loading subscription...',
  'webhooks.dialog.noHeaders': 'No custom headers',
  'webhooks.dialog.sectionEvents': 'Events',
  'webhooks.dialog.sectionHeaders': 'Custom headers',
  'webhooks.event.all': 'All events',
  'webhooks.event.allHint': 'Includes event types added later.',
  'webhooks.field.description': 'Description (optional)',
  'webhooks.field.descriptionPlaceholder': 'Purpose, receiving team or contact',
  'webhooks.field.headerName': 'Header {{index}} name',
  'webhooks.field.headerNamePlaceholder': 'Name',
  'webhooks.field.headerValue': 'Header {{index}} value',
  'webhooks.field.headerValuePlaceholder': 'Value',
  'webhooks.field.name': 'Name',
  'webhooks.field.namePlaceholder': 'e.g. ERP order sync',
  'webhooks.field.url': 'Endpoint URL',
  'webhooks.field.urlHint':
    'An absolute http(s) URL. Private network addresses are blocked when sending.',
};
//#endif
