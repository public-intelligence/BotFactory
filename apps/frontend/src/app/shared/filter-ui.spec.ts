import { describe, expect, it } from 'vitest';
import {
  formatDateInput,
  matchesDatePreset,
  presetDaysButtonClass,
} from './filter-ui';

describe('filter-ui', () => {
  it('formatDateInput returns yyyy-mm-dd', () => {
    expect(formatDateInput(new Date('2026-08-06T15:30:00Z'))).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it('matchesDatePreset detects rolling window ending today', () => {
    const to = formatDateInput(new Date());
    const from = new Date();
    from.setDate(from.getDate() - 7);
    expect(matchesDatePreset(formatDateInput(from), to, 7)).toBe(true);
    expect(matchesDatePreset(formatDateInput(from), to, 30)).toBe(false);
  });

  it('presetDaysButtonClass highlights selected preset', () => {
    expect(presetDaysButtonClass(true)).toContain('bg-blue-600');
    expect(presetDaysButtonClass(false)).toContain('bg-white');
  });
});
