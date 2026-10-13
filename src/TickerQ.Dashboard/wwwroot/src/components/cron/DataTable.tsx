import { useState, type ReactNode } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import {
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type ColumnFiltersState,
  type SortingState,
} from "@tanstack/react-table";

import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { TableRowsSkeleton } from "@/components/cron/TableRowsSkeleton";
import { cn } from "@/lib/utils";

interface DataTableProps<TData, TValue> {
  columns: ColumnDef<TData, TValue>[];
  data: TData[];
  /** Custom toolbar above the table. When provided, replaces the simple filterColumn input. */
  toolbar?: ReactNode;
  filterColumn?: string;
  filterPlaceholder?: string;
  emptyMessage?: string;
  onRowClick?: (row: TData) => void;

  // ===== Server-driven mode (matches the Hub). Enabled when onPageChange is set. =====
  page?: number;
  totalPages?: number;
  totalCount?: number;
  onPageChange?: (page: number) => void;
  isLoading?: boolean;
  isFetching?: boolean;
  skeletonRows?: number;

  // Row affordances
  getRowId?: (row: TData) => string;
  highlightedIds?: Set<string>;
  deletingIds?: Set<string>;

  // Multi-select for bulk actions. Providing onSelectionChange prepends a
  // checkbox column; selection is controlled by the page and keyed by getRowId.
  selectedIds?: Set<string>;
  onSelectionChange?: (ids: Set<string>) => void;
}

export function DataTable<TData, TValue>({
  columns,
  data,
  toolbar,
  filterColumn,
  filterPlaceholder = "Filter…",
  emptyMessage = "No results.",
  onRowClick,
  page,
  totalPages = 1,
  totalCount,
  onPageChange,
  isLoading = false,
  isFetching = false,
  skeletonRows = 6,
  getRowId = (row) => (row as { id: string }).id,
  highlightedIds,
  deletingIds,
  selectedIds,
  onSelectionChange,
}: DataTableProps<TData, TValue>) {
  const [sorting, setSorting] = useState<SortingState>([]);
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([]);
  const serverMode = typeof onPageChange === "function";

  const selectable = typeof onSelectionChange === "function";
  const selection = selectedIds ?? new Set<string>();
  const pageIds = data.map(getRowId);
  const allSelected = pageIds.length > 0 && pageIds.every((id) => selection.has(id));
  const someSelected = pageIds.some((id) => selection.has(id));

  function toggleAll() {
    const next = new Set(selection);
    if (allSelected) pageIds.forEach((id) => next.delete(id));
    else pageIds.forEach((id) => next.add(id));
    onSelectionChange?.(next);
  }

  function toggleOne(id: string) {
    const next = new Set(selection);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    onSelectionChange?.(next);
  }

  const effectiveColumns = selectable
    ? [
        {
          id: "__select",
          header: () => (
            <Checkbox
              checked={allSelected ? true : someSelected ? "indeterminate" : false}
              onCheckedChange={toggleAll}
              aria-label="Select all rows on this page"
              className="h-3.5 w-3.5"
            />
          ),
          cell: ({ row }) => {
            const id = getRowId(row.original as TData);
            return (
              <div onClick={(e) => e.stopPropagation()}>
                <Checkbox
                  checked={selection.has(id)}
                  onCheckedChange={() => toggleOne(id)}
                  aria-label="Select row"
                  className="h-3.5 w-3.5"
                />
              </div>
            );
          },
        } as ColumnDef<TData, TValue>,
        ...columns,
      ]
    : columns;

  const table = useReactTable({
    data,
    columns: effectiveColumns,
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
    // In server mode the API already returns one page, so we must NOT paginate
    // client-side (that would chop the page to 10 rows).
    ...(serverMode ? {} : { getPaginationRowModel: getPaginationRowModel() }),
    getSortedRowModel: getSortedRowModel(),
    onColumnFiltersChange: setColumnFilters,
    getFilteredRowModel: getFilteredRowModel(),
    state: { sorting, columnFilters },
  });

  const rows = table.getRowModel().rows;

  return (
    <div className="space-y-3">
      {toolbar
        ? toolbar
        : filterColumn && (
            <Input
              placeholder={filterPlaceholder}
              value={
                (table.getColumn(filterColumn)?.getFilterValue() as string) ??
                ""
              }
              onChange={(e) =>
                table.getColumn(filterColumn)?.setFilterValue(e.target.value)
              }
              className="h-8 max-w-xs bg-surface-1"
            />
          )}

      <div className="rounded-xl border border-border bg-card overflow-hidden relative">
        {/* Background refetch strip — only while a previous page is still on
            screen (hidden during the first-load skeleton). */}
        {isFetching && !isLoading && <div className="loading-bar-track" aria-hidden />}

        <Table>
          <TableHeader>
            {table.getHeaderGroups().map((headerGroup) => (
              <TableRow
                key={headerGroup.id}
                className="hover:bg-transparent bg-surface-0/30 border-b border-border"
              >
                {headerGroup.headers.map((header) => (
                  <TableHead
                    key={header.id}
                    className="text-[11px] font-medium text-muted-foreground/60 px-4 py-2.5"
                  >
                    {header.isPlaceholder
                      ? null
                      : flexRender(
                          header.column.columnDef.header,
                          header.getContext()
                        )}
                  </TableHead>
                ))}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {isLoading ? (
              <TableRowsSkeleton rows={skeletonRows} cols={effectiveColumns.length} />
            ) : rows.length ? (
              rows.map((row) => {
                const id = getRowId(row.original);
                const isDeleting = deletingIds?.has(id);
                const isHighlighted = highlightedIds?.has(id);
                return (
                  <TableRow
                    key={row.id}
                    data-row-id={id}
                    data-state={row.getIsSelected() && "selected"}
                    onClick={
                      onRowClick && !isDeleting
                        ? () => onRowClick(row.original)
                        : undefined
                    }
                    className={cn(
                      "border-b border-border/30 last:border-0 transition-colors",
                      onRowClick && !isDeleting ? "cursor-pointer" : "",
                      isDeleting
                        ? "bg-status-error/5 opacity-50 pointer-events-none"
                        : "hover:bg-surface-2/15",
                      isHighlighted && "animate-row-highlight"
                    )}
                  >
                    {row.getVisibleCells().map((cell) => (
                      <TableCell key={cell.id} className="px-4 py-2.5 text-[13px]">
                        {flexRender(
                          cell.column.columnDef.cell,
                          cell.getContext()
                        )}
                      </TableCell>
                    ))}
                  </TableRow>
                );
              })
            ) : (
              <TableRow>
                <TableCell
                  colSpan={effectiveColumns.length}
                  className="h-24 text-center text-xs text-muted-foreground"
                >
                  {emptyMessage}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>

        {serverMode && (
          <div className="flex items-center justify-between px-4 py-2.5 border-t border-border bg-surface-0/30">
            <span className="text-[11px] text-muted-foreground/50 tabular-nums">
              {totalCount ?? rows.length} total
            </span>
            <div className="flex items-center gap-1.5">
              <Button
                variant="outline"
                size="sm"
                className="h-7 w-7 p-0 bg-transparent border-border text-muted-foreground rounded-lg"
                disabled={(page ?? 1) <= 1}
                onClick={() => onPageChange?.(Math.max(1, (page ?? 1) - 1))}
              >
                <ChevronLeft className="h-3 w-3" />
              </Button>
              <span className="text-[11px] tabular-nums text-muted-foreground px-2">
                {page ?? 1} / {totalPages}
              </span>
              <Button
                variant="outline"
                size="sm"
                className="h-7 w-7 p-0 bg-transparent border-border text-muted-foreground rounded-lg"
                disabled={(page ?? 1) >= totalPages}
                onClick={() => onPageChange?.((page ?? 1) + 1)}
              >
                <ChevronRight className="h-3 w-3" />
              </Button>
            </div>
          </div>
        )}
      </div>

      {!serverMode && (
        <div className="flex items-center justify-between">
          <p className="text-xs text-muted-foreground tabular-nums">
            {table.getFilteredRowModel().rows.length} row
            {table.getFilteredRowModel().rows.length === 1 ? "" : "s"}
          </p>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              className="h-8 text-xs rounded-lg"
              onClick={() => table.previousPage()}
              disabled={!table.getCanPreviousPage()}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              className="h-8 text-xs rounded-lg"
              onClick={() => table.nextPage()}
              disabled={!table.getCanNextPage()}
            >
              Next
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
