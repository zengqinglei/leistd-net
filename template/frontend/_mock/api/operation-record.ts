import { PagedResultDto } from '../../src/app/shared/models/paged-result.dto';
import { MockRequest } from '../core/models';
import { OPERATION_RECORDS } from '../data/operation-record';

function getQueryValue(value: unknown) {
  const normalized = Array.isArray(value) ? value[0] : value;
  return normalized === undefined || normalized === null || normalized === ''
    ? undefined
    : String(normalized);
}

/**
 * 与后端 `EfCoreOperationRecordStore.GetPagedListAsync` 同一口径：
 * 固定按时间倒序，并追加 Id 作为稳定次序——否则同一毫秒内的多条记录在翻页时会重复或漏掉。
 *
 * **没有排序参数**：后端契约里就没有，前端也不该造一个。
 */
export function getOperationRecords(params: any): PagedResultDto<any> {
  let records = [...OPERATION_RECORDS];
  const offset = +(getQueryValue(params.offset) ?? 0);
  const limit = +(getQueryValue(params.limit) ?? 10);
  const keyword = getQueryValue(params.keyword)?.toLowerCase();

  if (keyword) {
    records = records.filter(
      (record) =>
        record.action.toLowerCase().includes(keyword) ||
        record.targetId.toLowerCase().includes(keyword) ||
        record.actorName?.toLowerCase().includes(keyword) ||
        record.actorId?.toLowerCase().includes(keyword),
    );
  }

  // 与后端同为**闭区间**。两端都是 UTC ISO 串，而 `creationTime` 也是同格式的 UTC ISO，
  // 因此可以直接按字典序比较——ISO 8601 的字典序与时间序一致，这是它被选作传输格式的原因之一。
  const startTime = getQueryValue(params.startTime);
  const endTime = getQueryValue(params.endTime);
  if (startTime) {
    records = records.filter((record) => record.creationTime >= startTime);
  }
  if (endTime) {
    records = records.filter((record) => record.creationTime <= endTime);
  }

  records.sort((a, b) => {
    const compared = b.creationTime.localeCompare(a.creationTime);
    return compared !== 0 ? compared : b.id.localeCompare(a.id);
  });

  return {
    totalCount: records.length,
    items: records.slice(offset, offset + limit),
  };
}

// 只读：记录写下之后不再修改或删除，因此没有写端点。
export const OPERATION_RECORD_API = {
  'GET /api/v1/operation-records': (req: MockRequest) => getOperationRecords(req.queryParams),
};
