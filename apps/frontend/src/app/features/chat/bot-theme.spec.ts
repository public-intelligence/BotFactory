import { describe, expect, it } from 'vitest';
import {
  buildThemeCssVars,
  contrastForeground,
  DEFAULT_THEME_BACKGROUND,
  hasCustomTheme,
  mixHex,
  normalizeHex,
  relativeLuminance,
  resolveThemeColors,
  themeStyleFromBot,
} from './bot-theme';

describe('bot-theme', () => {
  it('normalizeHex expands shorthand and uppercases', () => {
    expect(normalizeHex('#abc', '#000000')).toBe('#AABBCC');
    expect(normalizeHex('#a1b2c3', '#000000')).toBe('#A1B2C3');
    expect(normalizeHex('nope', DEFAULT_THEME_BACKGROUND)).toBe(DEFAULT_THEME_BACKGROUND);
  });

  it('resolveThemeColors falls back to defaults', () => {
    expect(resolveThemeColors(null)).toEqual({
      background: DEFAULT_THEME_BACKGROUND,
      primary: '#FFFFFF',
      accent: '#FFFFFF',
    });
  });

  it('hasCustomTheme detects any set color', () => {
    expect(hasCustomTheme({})).toBe(false);
    expect(hasCustomTheme({ themeAccent: '#112233' })).toBe(true);
  });

  it('relativeLuminance and contrastForeground pick readable fg', () => {
    expect(relativeLuminance('#FFFFFF')).toBeGreaterThan(0.45);
    expect(contrastForeground('#FFFFFF')).toBe('#0A0A0A');
    expect(contrastForeground('#000000')).toBe('#FFFFFF');
  });

  it('mixHex blends toward target', () => {
    expect(mixHex('#000000', '#FFFFFF', 0.5)).toBe('#808080');
  });

  it('themeStyleFromBot exposes css vars', () => {
    const vars = themeStyleFromBot({
      themeBackground: '#112233',
      themePrimary: '#445566',
      themeAccent: '#778899',
    });
    expect(vars['--bf-bg']).toBe('#112233');
    expect(vars['--bf-primary']).toBe('#445566');
    expect(vars['--bf-accent']).toBe('#778899');
    expect(vars['--bf-fg']).toBeTruthy();
    expect(Object.keys(buildThemeCssVars(resolveThemeColors({ themeBackground: '#000' })))).toContain(
      '--bf-surface',
    );
  });
});
