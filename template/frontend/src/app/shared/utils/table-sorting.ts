import { RowData, Table } from '@tanstack/angular-table';

import type { AppTableFeatures } from './table-features';

/** 切换某列排序：升序 → 降序 → 升序。 */
export function toggleTableSort<TData extends RowData>(
  table: Table<AppTableFeatures, TData>,
  columnId: string,
): void {
  const column = table.getColumn(columnId);
  column?.toggleSorting(column.getIsSorted() === 'asc');
}

/** 排序指示图标名（lucide）。 */
export function tableSortIcon<TData extends RowData>(
  table: Table<AppTableFeatures, TData>,
  columnId: string,
): string {
  const direction = table.getColumn(columnId)?.getIsSorted();
  return direction === 'asc'
    ? 'lucideSortAsc'
    : direction === 'desc'
      ? 'lucideSortDesc'
      : 'lucideArrowUpDown';
}

/** 表头的 `aria-sort` 取值，与图标同源，免得视觉与读屏说出不同方向。 */
export function tableSortAria<TData extends RowData>(
  table: Table<AppTableFeatures, TData>,
  columnId: string,
): 'ascending' | 'descending' | 'none' {
  const direction = table.getColumn(columnId)?.getIsSorted();
  return direction === 'asc' ? 'ascending' : direction === 'desc' ? 'descending' : 'none';
}
