import {
  columnVisibilityFeature,
  createTableHook,
  metaHelper,
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
 */
const features = tableFeatures({
  columnVisibilityFeature,
  rowPaginationFeature,
  rowSortingFeature,
  columnMeta: metaHelper<AppColumnMeta>(),
});

/**
 * 预绑定应用 feature 集合的表格 hook。
 */
export const { injectAppTable } = createTableHook({ features });

/** 供表格与列定义使用的应用 feature 类型。 */
export type AppTableFeatures = typeof features;
