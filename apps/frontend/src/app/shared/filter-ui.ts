/** Shared filter control styling and date-preset helpers. */

export function formatDateInput(date: Date): string {
  return date.toISOString().slice(0, 10);
}

export function matchesDatePreset(
  dateFrom: string,
  dateTo: string,
  days: number,
): boolean {
  const today = formatDateInput(new Date());
  if (dateTo !== today) return false;

  const from = new Date();
  from.setDate(from.getDate() - days);
  return dateFrom === formatDateInput(from);
}

export function presetDaysButtonClass(selected: boolean): string {
  const base =
    'rounded-lg border px-3 py-2 text-xs font-medium transition focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1';
  return selected
    ? `${base} border-blue-600 bg-blue-600 text-white shadow-sm hover:bg-blue-700`
    : `${base} border-neutral-200 bg-white text-neutral-700 hover:border-neutral-300 hover:bg-neutral-50`;
}
