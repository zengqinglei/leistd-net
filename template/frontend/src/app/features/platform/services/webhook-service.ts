import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResultDto } from '../../../shared/dtos/paged-result.dto';
import {
  CreateWebhookSubscriptionInputDto,
  GetWebhookDeliveriesInputDto,
  GetWebhookSubscriptionsInputDto,
  UpdateWebhookSubscriptionInputDto,
  WebhookDeliveryDetailOutputDto,
  WebhookDeliveryOutputDto,
  WebhookEventTypeOutputDto,
  WebhookStatisticsOutputDto,
  WebhookStatisticsQuery,
  WebhookSubscriptionOutputDto,
  WebhookSubscriptionSecretOutputDto,
} from '../dtos/webhook.dto';

/** 订阅全部事件（含以后新增的）时 `eventTypes` 里的唯一取值。 */
export const WEBHOOK_ALL_EVENTS = '*';
/** 组件内置的测试事件；"发送测试事件"不论订阅了什么都发它，订阅对话框不列出。 */
export const WEBHOOK_TEST_EVENT = 'webhook.test';

/** 未设置、空串与 `undefined` 都不进查询串：服务端把空串当成筛选值。 */
function toParams(values: Record<string, string | number | boolean | undefined | null>) {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== undefined && value !== null && value !== '') {
      params = params.set(key, String(value));
    }
  }
  return params;
}

/** 出站 Webhook 的订阅、投递日志与统计；读需要 `App.Webhooks`，写需要 `App.Webhooks.Manage`。 */
@Injectable({ providedIn: 'root' })
export class WebhookService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/webhooks';

  getEventTypes(): Observable<WebhookEventTypeOutputDto[]> {
    return this.http.get<WebhookEventTypeOutputDto[]>(`${this.baseUrl}/event-types`);
  }

  getSubscriptions(
    input: GetWebhookSubscriptionsInputDto = {},
  ): Observable<PagedResultDto<WebhookSubscriptionOutputDto>> {
    return this.http.get<PagedResultDto<WebhookSubscriptionOutputDto>>(
      `${this.baseUrl}/subscriptions`,
      {
        params: toParams({
          offset: input.offset,
          limit: input.limit,
          keyword: input.keyword,
          isEnabled: input.isEnabled,
          eventType: input.eventType,
        }),
      },
    );
  }

  getSubscription(id: string): Observable<WebhookSubscriptionOutputDto> {
    return this.http.get<WebhookSubscriptionOutputDto>(`${this.baseUrl}/subscriptions/${id}`);
  }

  createSubscription(
    input: CreateWebhookSubscriptionInputDto,
  ): Observable<WebhookSubscriptionSecretOutputDto> {
    return this.http.post<WebhookSubscriptionSecretOutputDto>(
      `${this.baseUrl}/subscriptions`,
      input,
    );
  }

  updateSubscription(
    id: string,
    input: UpdateWebhookSubscriptionInputDto,
  ): Observable<WebhookSubscriptionOutputDto> {
    return this.http.put<WebhookSubscriptionOutputDto>(
      `${this.baseUrl}/subscriptions/${id}`,
      input,
    );
  }

  deleteSubscription(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/subscriptions/${id}`);
  }

  /** `retryFailed` 走查询串：为真时该订阅现存的失败投递各按手动重发处理一次。 */
  enableSubscription(id: string, retryFailed: boolean): Observable<WebhookSubscriptionOutputDto> {
    return this.http.post<WebhookSubscriptionOutputDto>(
      `${this.baseUrl}/subscriptions/${id}/enable`,
      {},
      { params: retryFailed ? toParams({ retryFailed: true }) : undefined },
    );
  }

  disableSubscription(id: string): Observable<WebhookSubscriptionOutputDto> {
    return this.http.post<WebhookSubscriptionOutputDto>(
      `${this.baseUrl}/subscriptions/${id}/disable`,
      {},
    );
  }

  rotateSecret(id: string): Observable<WebhookSubscriptionSecretOutputDto> {
    return this.http.post<WebhookSubscriptionSecretOutputDto>(
      `${this.baseUrl}/subscriptions/${id}/rotate-secret`,
      {},
    );
  }

  sendTestEvent(id: string): Observable<WebhookDeliveryOutputDto> {
    return this.http.post<WebhookDeliveryOutputDto>(`${this.baseUrl}/subscriptions/${id}/test`, {});
  }

  getDeliveries(
    input: GetWebhookDeliveriesInputDto = {},
  ): Observable<PagedResultDto<WebhookDeliveryOutputDto>> {
    return this.http.get<PagedResultDto<WebhookDeliveryOutputDto>>(`${this.baseUrl}/deliveries`, {
      params: toParams({
        offset: input.offset,
        limit: input.limit,
        keyword: input.keyword,
        subscriptionId: input.subscriptionId,
        status: input.status,
        eventType: input.eventType,
      }),
    });
  }

  getDelivery(id: string): Observable<WebhookDeliveryDetailOutputDto> {
    return this.http.get<WebhookDeliveryDetailOutputDto>(`${this.baseUrl}/deliveries/${id}`);
  }

  /** 只对终态投递开放；返回改为待投递（手动）的列表项。 */
  retryDelivery(id: string): Observable<WebhookDeliveryOutputDto> {
    return this.http.post<WebhookDeliveryOutputDto>(`${this.baseUrl}/deliveries/${id}/retry`, {});
  }

  /** 模板统计端点：日历区间、展示时区与可选的订阅筛选，与平台统计同一约定。 */
  getStatistics(query: WebhookStatisticsQuery): Observable<WebhookStatisticsOutputDto> {
    return this.http.get<WebhookStatisticsOutputDto>(`${this.baseUrl}/statistics`, {
      params: toParams({ ...query }),
    });
  }
}
