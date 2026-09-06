import { Table } from '@tanstack/angular-table';

/** 切换某列排序：升序 → 降序 → 升序。 */
export function toggleTableSort<TData>(table: Table<TData>, columnId: string): void {
  const column = table.getColumn(columnId);
  column?.toggleSorting(column.getIsSorted() === 'asc');
}

/** 排序指示图标名（lucide）。 */
export function tableSortIcon<TData>(table: Table<TData>, columnId: string): string {
  const direction = table.getColumn(columnId)?.getIsSorted();
  return direction === 'asc'
    ? 'lucideSortAsc'
    : direction === 'desc'
      ? 'lucideSortDesc'
      : 'lucideArrowUpDown';
}

/**
 * 表头的 `aria-sort` 取值。
 *
 * 必须与图标同源：两处各自判断时，视觉与读屏会说出不同的排序方向。
 */
export function tableSortAria<TData>(
  table: Table<TData>,
  columnId: string,
): 'ascending' | 'descending' | 'none' {
  const direction = table.getColumn(columnId)?.getIsSorted();
  return direction === 'asc' ? 'ascending' : direction === 'desc' ? 'descending' : 'none';
}
