import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResultDto } from '../../../shared/dtos/paged-result.dto';
import * as Dto from '../dtos/distributed-event.dto';

/** 空值不进查询串；数据库选择不改变当前登录租户。 */
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

  getStatus(database?: string | null): Observable<Dto.DistributedEventStatusOutputDto> {
    return this.http.get<Dto.DistributedEventStatusOutputDto>(`${this.baseUrl}/status`, {
      params: toParams({ database }),
    });
  }

  getDatabases(): Observable<Dto.DistributedEventDatabaseOutputDto[]> {
    return this.http.get<Dto.DistributedEventDatabaseOutputDto[]>(`${this.baseUrl}/databases`);
  }

  getCatalog(database?: string | null): Observable<Dto.DistributedEventCatalogOutputDto[]> {
    return this.http.get<Dto.DistributedEventCatalogOutputDto[]>(`${this.baseUrl}/catalog`, {
      params: toParams({ database }),
    });
  }

  getOutboxList(
    input: Dto.GetDistributedEventOutboxInputDto = {},
  ): Observable<PagedResultDto<Dto.DistributedEventOutboxOutputDto>> {
    return this.http.get<PagedResultDto<Dto.DistributedEventOutboxOutputDto>>(
      `${this.baseUrl}/outbox`,
      { params: toParams(input) },
    );
  }

  getOutbox(
    id: string,
    database?: string | null,
  ): Observable<Dto.DistributedEventOutboxDetailOutputDto> {
    return this.http.get<Dto.DistributedEventOutboxDetailOutputDto>(
      `${this.baseUrl}/outbox/${id}`,
      { params: toParams({ database }) },
    );
  }

  getOutboxPayload(
    id: string,
    database?: string | null,
  ): Observable<Dto.DistributedEventPayloadOutputDto> {
    return this.http.get<Dto.DistributedEventPayloadOutputDto>(
      `${this.baseUrl}/outbox/${id}/payload`,
      { params: toParams({ database }) },
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

  getInbox(
    id: string,
    database?: string | null,
  ): Observable<Dto.DistributedEventInboxDetailOutputDto> {
    return this.http.get<Dto.DistributedEventInboxDetailOutputDto>(`${this.baseUrl}/inbox/${id}`, {
      params: toParams({ database }),
    });
  }

  getInboxPayload(
    id: string,
    database?: string | null,
  ): Observable<Dto.DistributedEventPayloadOutputDto> {
    return this.http.get<Dto.DistributedEventPayloadOutputDto>(
      `${this.baseUrl}/inbox/${id}/payload`,
      { params: toParams({ database }) },
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

  getRejection(
    id: string,
    database?: string | null,
  ): Observable<Dto.DistributedEventRejectionDetailOutputDto> {
    return this.http.get<Dto.DistributedEventRejectionDetailOutputDto>(
      `${this.baseUrl}/rejections/${id}`,
      { params: toParams({ database }) },
    );
  }

  getRejectionPayload(
    id: string,
    database?: string | null,
  ): Observable<Dto.DistributedEventRawMessageOutputDto> {
    return this.http.get<Dto.DistributedEventRawMessageOutputDto>(
      `${this.baseUrl}/rejections/${id}/payload`,
      { params: toParams({ database }) },
    );
  }

  redeliver(
    input: Dto.RecoverDistributedEventOutboxInputDto,
    database?: string | null,
  ): Observable<Dto.DistributedEventRecoveryOutputDto> {
    return this.http.post<Dto.DistributedEventRecoveryOutputDto>(
      `${this.baseUrl}/outbox/redeliver`,
      input,
      { params: toParams({ database }) },
    );
  }

  replay(
    input: Dto.RecoverDistributedEventInboxInputDto,
    database?: string | null,
  ): Observable<Dto.DistributedEventRecoveryOutputDto> {
    return this.http.post<Dto.DistributedEventRecoveryOutputDto>(
      `${this.baseUrl}/inbox/replay`,
      input,
      { params: toParams({ database }) },
    );
  }

  redecode(
    input: Dto.RecoverDistributedEventRejectionInputDto,
    database?: string | null,
  ): Observable<Dto.DistributedEventRecoveryOutputDto> {
    return this.http.post<Dto.DistributedEventRecoveryOutputDto>(
      `${this.baseUrl}/rejections/redecode`,
      input,
      { params: toParams({ database }) },
    );
  }

  discard(
    input: Dto.RecoverDistributedEventRejectionInputDto,
    database?: string | null,
  ): Observable<Dto.DistributedEventRecoveryOutputDto> {
    return this.http.post<Dto.DistributedEventRecoveryOutputDto>(
      `${this.baseUrl}/rejections/discard`,
      input,
      { params: toParams({ database }) },
    );
  }

  sendTestEvent(database?: string | null): Observable<Dto.DistributedEventTestOutputDto> {
    return this.http.post<Dto.DistributedEventTestOutputDto>(
      `${this.baseUrl}/test`,
      {},
      { params: toParams({ database }) },
    );
  }
}
