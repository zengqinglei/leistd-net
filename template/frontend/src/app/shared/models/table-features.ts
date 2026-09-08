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
 * 列元数据契约。
 *
 * TanStack v9 用 `tableFeatures({ columnMeta })` 类型槽取代 v8 的全局声明合并：
 * 类型跟着 feature 集合走，不再污染库的全局接口。
 */
export interface AppColumnMeta {
  locked?: boolean;
  priority: TableColumnPriority;
  headClass?: string;
  cellClass?: string;
}

/**
 * 四个平台表格共用的 feature 集合。
 *
 * v9 的 feature 需显式声明才有对应能力；core 行模型是自动的，不再传 `getCoreRowModel`。
 * 排序与分页一律服务端（`manualSorting` / `manualPagination`），因此只装 feature、
 * 不装 `sortedRowModel` / `paginatedRowModel` 这些客户端行模型。
 */
const features = tableFeatures({
  columnVisibilityFeature,
  rowPaginationFeature,
  rowSortingFeature,
  columnMeta: metaHelper<AppColumnMeta>(),
});

/**
 * 应用级表格 hook：feature 集合已预绑定，各表格直接 `injectAppTable(...)`，不再逐个传 `features`。
 *
 * 这是 v9 为「一个应用共用一套 feature」提供的入口（官方说明：让应用或设计体系拿到
 * 已绑定的 `injectAppTable`，不必重复同一份 feature 泛型）。逐表传 `features` 也能跑，
 * 但那是把该由类型层收口的重复搬到了每个组件里。
 */
export const { injectAppTable } = createTableHook({ features });

/** 供表格辅助函数标注 `Table` / `ColumnDef` 泛型；官方文档里等价于 `typeof features`。 */
export type AppTableFeatures = typeof features;
