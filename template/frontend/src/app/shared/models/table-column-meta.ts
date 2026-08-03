import type { ColumnDef, RowData, VisibilityState } from '@tanstack/table-core';

export type TableColumnPriority = 'primary' | 'secondary' | 'tertiary';
export type TableViewport = 'mobile' | 'tablet' | 'desktop';

declare module '@tanstack/table-core' {
  // Type parameter names must match TanStack's declaration for interface merging.
  // eslint-disable-next-line unused-imports/no-unused-vars
  interface ColumnMeta<TData extends RowData, TValue> {
    locked?: boolean;
    priority: TableColumnPriority;
    headClass?: string;
    cellClass?: string;
  }
}

export function tableColumnVisibility<TData extends RowData>(
  columns: readonly ColumnDef<TData>[],
  viewport: TableViewport,
): VisibilityState {
  return Object.fromEntries(
    columns.flatMap((column) => {
      const id = 'id' in column ? column.id : undefined;
      if (!id) {
        return [];
      }

      const meta = column.meta;
      const visible =
        meta?.locked === true ||
        meta?.priority === undefined ||
        meta.priority === 'primary' ||
        (meta.priority === 'secondary' && viewport !== 'mobile') ||
        (meta.priority === 'tertiary' && viewport === 'desktop');

      return [[id, visible]];
    }),
  );
}
