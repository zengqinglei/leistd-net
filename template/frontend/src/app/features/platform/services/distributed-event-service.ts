import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResultDto } from '../../../shared/dtos/paged-result.dto';
import * as Dto from '../dtos/distributed-event.dto';

/** 空值不进查询串；请求范围由当前会话与工作区决定。 */
function toParams(input: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(input)) {
    if (value !== undefined && value !== null && value !== '') {
      params = params.set(key, String(value));
    }
  }
  return params;
}

/** 分布式事件运维；概要只读，载荷与恢复操作要求 Manage。 */
@Injectable({ providedIn: 'root' })
export class DistributedEventService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/distributed-events';

  getStatus(): Observable<Dto.DistributedEventStatusOutputDto> {
    return this.http.get<Dto.DistributedEventStatusOutputDto>(`${this.baseUrl}/status`);
  }

  getCatalog(): Observable<Dto.DistributedEventCatalogOutputDto[]> {
    return this.http.get<Dto.DistributedEventCatalogOutputDto[]>(`${this.baseUrl}/catalog`);
  }

  getOutboxList(
    input: Dto.GetDistributedEventOutboxInputDto = {},
  ): Observable<PagedResultDto<Dto.DistributedEventOutboxOutputDto>> {
    return this.http.get<PagedResultDto<Dto.DistributedEventOutboxOutputDto>>(
      `${this.baseUrl}/outbox`,
      { params: toParams(input) },
    );
  }

  getOutbox(id: string): Observable<Dto.DistributedEventOutboxDetailOutputDto> {
    return this.http.get<Dto.DistributedEventOutboxDetailOutputDto>(`${this.baseUrl}/outbox/${id}`);
  }

  getOutboxPayload(id: string): Observable<Dto.DistributedEventPayloadOutputDto> {
    return this.http.get<Dto.DistributedEventPayloadOutputDto>(
      `${this.baseUrl}/outbox/${id}/payload`,
    );
  }

  getInboxList(
    input: Dto.GetDistributedEventInboxInputDto = {},
  ): Observable<PagedResultDto<Dto.DistributedEventInboxOutputDto>> {
    return this.http.get<PagedResultDto<Dto.DistributedEventInboxOutputDto>>(
      `${this.baseUrl}/inbox`,
      { params: toParams(input) },
    );
  }

  getInbox(id: string): Observable<Dto.DistributedEventInboxDetailOutputDto> {
    return this.http.get<Dto.DistributedEventInboxDetailOutputDto>(`${this.baseUrl}/inbox/${id}`);
  }

  getInboxPayload(id: string): Observable<Dto.DistributedEventPayloadOutputDto> {
    return this.http.get<Dto.DistributedEventPayloadOutputDto>(
      `${this.baseUrl}/inbox/${id}/payload`,
    );
  }

  getRejectionList(
    input: Dto.GetDistributedEventRejectionsInputDto = {},
  ): Observable<PagedResultDto<Dto.DistributedEventRejectionOutputDto>> {
    return this.http.get<PagedResultDto<Dto.DistributedEventRejectionOutputDto>>(
      `${this.baseUrl}/rejections`,
      { params: toParams(input) },
    );
  }

  getRejection(id: string): Observable<Dto.DistributedEventRejectionDetailOutputDto> {
    return this.http.get<Dto.DistributedEventRejectionDetailOutputDto>(
      `${this.baseUrl}/rejections/${id}`,
    );
  }

  getRejectionPayload(id: string): Observable<Dto.DistributedEventRawMessageOutputDto> {
    return this.http.get<Dto.DistributedEventRawMessageOutputDto>(
      `${this.baseUrl}/rejections/${id}/payload`,
    );
  }

  redeliver(
    input: Dto.RecoverDistributedEventOutboxInputDto,
  ): Observable<Dto.DistributedEventRecoveryOutputDto> {
    return this.http.post<Dto.DistributedEventRecoveryOutputDto>(
      `${this.baseUrl}/outbox/redeliver`,
      input,
    );
  }

  replay(
    input: Dto.RecoverDistributedEventInboxInputDto,
  ): Observable<Dto.DistributedEventRecoveryOutputDto> {
    return this.http.post<Dto.DistributedEventRecoveryOutputDto>(
      `${this.baseUrl}/inbox/replay`,
      input,
    );
  }

  redecode(
    input: Dto.RecoverDistributedEventRejectionInputDto,
  ): Observable<Dto.DistributedEventRecoveryOutputDto> {
    return this.http.post<Dto.DistributedEventRecoveryOutputDto>(
      `${this.baseUrl}/rejections/redecode`,
      input,
    );
  }

  discard(
    input: Dto.RecoverDistributedEventRejectionInputDto,
  ): Observable<Dto.DistributedEventRecoveryOutputDto> {
    return this.http.post<Dto.DistributedEventRecoveryOutputDto>(
      `${this.baseUrl}/rejections/discard`,
      input,
    );
  }

  sendTestEvent(): Observable<Dto.DistributedEventTestOutputDto> {
    return this.http.post<Dto.DistributedEventTestOutputDto>(`${this.baseUrl}/test`, {});
  }
}
