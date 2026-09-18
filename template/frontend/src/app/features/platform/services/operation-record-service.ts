import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResultDto } from '../../../shared/models/paged-result.dto';
import {
  ExportOperationRecordsInputDto,
  GetOperationRecordsInputDto,
  OperationRecordFilterOptionsDto,
  OperationRecordOutputDto,
} from '../models/operation-record.dto';

/**
 * 操作记录查询服务。
 *
 * **只读**：记录写下之后不再修改或删除，因此没有写方法——
 * 一张能被改写的审计表不能作为证据。
 */
@Injectable({ providedIn: 'root' })
export class OperationRecordService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/operation-records';

  getOperationRecords(
    input: GetOperationRecordsInputDto,
  ): Observable<PagedResultDto<OperationRecordOutputDto>> {
    let params = new HttpParams();
    if (input.offset !== undefined) params = params.set('offset', input.offset.toString());
    if (input.limit !== undefined) params = params.set('limit', input.limit.toString());
    if (input.keyword) params = params.set('keyword', input.keyword);
    // 时间区间已是 UTC ISO 串（换算在调用方完成），这里原样透传，不再做第二次转换。
    if (input.startTime) params = params.set('startTime', input.startTime);
    if (input.endTime) params = params.set('endTime', input.endTime);
    // 数组参数逐个 append：服务端是 List<string>，查询串要的是重复键。
    // 用 set 会覆盖、只传最后一个值——而且不报错，表现为"筛了几项却只按一项过滤"。
    // 形态与 user-management-service 的 roles 一致。
    if (input.categories?.length) {
      for (const category of input.categories) {
        params = params.append('categories', category);
      }
    }
    if (input.actions?.length) {
      for (const action of input.actions) {
        params = params.append('actions', action);
      }
    }
    if (input.outcome) params = params.set('outcome', input.outcome);
    return this.http.get<PagedResultDto<OperationRecordOutputDto>>(this.baseUrl, { params });
  }

  /**
   * 取筛选项：有哪些类别、哪些动作码。
   *
   * **不在前端硬编码动作码**：那等于把服务端的登记复制一份过来，两处迟早漂移，
   * 而症状是"某个动作在筛选框里选不到"，没有任何报错。
   * 选项已按当前读者的可见性裁剪，租户读者拿不到宿主侧动作。
   */
  getFilterOptions(): Observable<OperationRecordFilterOptionsDto> {
    return this.http.get<OperationRecordFilterOptionsDto>(`${this.baseUrl}/filter-options`);
  }

  /**
   * 按当前筛选条件导出 CSV。
   *
   * **`responseType: 'blob'` 不能省**：默认按 JSON 解析，而服务端返回的是 CSV 文本，
   * 解析失败会抛在拦截器里，表现为"点了没反应"，没有任何提示。
   *
   * **不是全量导出**：取筛选结果的前 N 条（后端 10000 封顶）。
   */
  exportOperationRecords(input: ExportOperationRecordsInputDto): Observable<Blob> {
    let params = new HttpParams();
    if (input.keyword) params = params.set('keyword', input.keyword);
    if (input.startTime) params = params.set('startTime', input.startTime);
    if (input.endTime) params = params.set('endTime', input.endTime);
    // 数组参数逐个 append，与查询方法同一理由：set 会覆盖成只传最后一个值且不报错。
    if (input.categories?.length) {
      for (const category of input.categories) {
        params = params.append('categories', category);
      }
    }
    if (input.actions?.length) {
      for (const action of input.actions) {
        params = params.append('actions', action);
      }
    }
    if (input.outcome) params = params.set('outcome', input.outcome);
    if (input.limit !== undefined) params = params.set('limit', input.limit.toString());
    return this.http.get(`${this.baseUrl}/export`, { params, responseType: 'blob' });
  }
}
