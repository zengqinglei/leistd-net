import {
  columnVisibilityFeature,
  createTableHook,
  metaHelper,
  rowExpandingFeature,
  rowPaginationFeature,
  rowSortingFeature,
  tableFeatures,
} from '@tanstack/angular-table';

export type TableColumnPriority = 'primary' | 'secondary' | 'tertiary';

/**
 * 应用表格的列元数据，通过 feature 类型槽限定可用属性。
 */
export interface AppColumnMeta {
  locked?: boolean;
  priority: TableColumnPriority;
  headClass?: string;
  cellClass?: string;
}

/**
 * 平台表格共用的能力。排序与分页由服务端完成，不加载客户端排序或分页行模型。
 * 行展开只用它的状态（`row.getIsExpanded()` / `row.toggleExpanded()`）补偿被响应式隐藏的列，
 * 没有子行，因此也不加载展开行模型。
 */
const features = tableFeatures({
  columnVisibilityFeature,
  rowExpandingFeature,
  rowPaginationFeature,
  rowSortingFeature,
  columnMeta: metaHelper<AppColumnMeta>(),
});

/**
 * 预绑定应用 feature 集合的表格 hook。
 *
 * 行展开的两项默认值：
 * - 每一行都可展开：展开内容是本行被隐藏的列，不是子行（默认只有带子行的行才能展开）；
 * - 数据换了不收起：翻页、排序、刷新都会换一批数据，默认会清空展开状态。展开按行 id 记，
 *   刷新后同一行仍保持展开；因此各表格须用 `getRowId` 给出实体 id，默认的行下标会让展开跟着位置走。
 */
export const { injectAppTable } = createTableHook({
  features,
  getRowCanExpand: () => true,
  autoResetExpanded: false,
});

/** 供表格与列定义使用的应用 feature 类型。 */
export type AppTableFeatures = typeof features;
