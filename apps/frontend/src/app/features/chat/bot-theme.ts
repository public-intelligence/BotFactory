/** Platform dark defaults — match current chat chrome when bot has no theme. */
export const DEFAULT_THEME_BACKGROUND = '#0A0A0A';
export const DEFAULT_THEME_PRIMARY = '#FFFFFF';
export const DEFAULT_THEME_ACCENT = '#FFFFFF';

export interface BotThemeInput {
  themeBackground?: string | null;
  themePrimary?: string | null;
  themeAccent?: string | null;
}

export interface BotThemeColors {
  background: string;
  primary: string;
  accent: string;
}

/** CSS custom properties applied on the chat root (and Apariencia preview). */
export type BotThemeCssVars = Record<string, string>;

const HEX_RE = /^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$/;

export function normalizeHex(value: string | null | undefined, fallback: string): string {
  if (!value || !HEX_RE.test(value.trim())) return fallback;
  const hex = value.trim().slice(1);
  if (hex.length === 3) {
    return (
      '#' +
      [hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]].join('').toUpperCase()
    );
  }
  return '#' + hex.toUpperCase();
}

export function resolveThemeColors(bot: BotThemeInput | null | undefined): BotThemeColors {
  return {
    background: normalizeHex(bot?.themeBackground, DEFAULT_THEME_BACKGROUND),
    primary: normalizeHex(bot?.themePrimary, DEFAULT_THEME_PRIMARY),
    accent: normalizeHex(bot?.themeAccent, DEFAULT_THEME_ACCENT),
  };
}

/** True when the bot has at least one explicit theme color set. */
export function hasCustomTheme(bot: BotThemeInput | null | undefined): boolean {
  return !!(bot?.themeBackground || bot?.themePrimary || bot?.themeAccent);
}

export function buildThemeCssVars(colors: BotThemeColors): BotThemeCssVars {
  const fg = contrastForeground(colors.background);
  const ctaFg = contrastForeground(colors.accent);
  const darkBg = relativeLuminance(colors.background) < 0.45;

  const surface = darkBg
    ? mixHex(colors.background, '#FFFFFF', 0.12)
    : mixHex(colors.background, '#FFFFFF', 0.55);

  const botBubble = darkBg
    ? mixHex(colors.background, '#FFFFFF', 0.08)
    : mixHex(colors.background, '#FFFFFF', 0.85);

  const muted = darkBg
    ? mixHex(fg, colors.background, 0.45)
    : mixHex(fg, colors.background, 0.4);

  return {
    '--bf-bg': colors.background,
    '--bf-fg': fg,
    '--bf-primary': colors.primary,
    '--bf-accent': colors.accent,
    '--bf-surface': surface,
    '--bf-cta-fg': ctaFg,
    '--bf-bot-bubble': botBubble,
    '--bf-muted': muted,
  };
}

export function themeStyleFromBot(bot: BotThemeInput | null | undefined): BotThemeCssVars {
  return buildThemeCssVars(resolveThemeColors(bot));
}

function parseRgb(hex: string): [number, number, number] {
  const h = normalizeHex(hex, '#000000').slice(1);
  return [
    parseInt(h.slice(0, 2), 16),
    parseInt(h.slice(2, 4), 16),
    parseInt(h.slice(4, 6), 16),
  ];
}

function toHex(r: number, g: number, b: number): string {
  const clamp = (n: number) => Math.max(0, Math.min(255, Math.round(n)));
  return (
    '#' +
    [clamp(r), clamp(g), clamp(b)]
      .map((n) => n.toString(16).padStart(2, '0'))
      .join('')
      .toUpperCase()
  );
}

/** Relative luminance (sRGB), 0 = black, 1 = white. */
export function relativeLuminance(hex: string): number {
  const [r, g, b] = parseRgb(hex).map((c) => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrastForeground(background: string): string {
  return relativeLuminance(background) > 0.45 ? '#0A0A0A' : '#FFFFFF';
}

/** Mix `from` toward `toward` by `amount` (0–1). */
export function mixHex(from: string, toward: string, amount: number): string {
  const a = parseRgb(from);
  const b = parseRgb(toward);
  const t = Math.max(0, Math.min(1, amount));
  return toHex(
    a[0] + (b[0] - a[0]) * t,
    a[1] + (b[1] - a[1]) * t,
    a[2] + (b[2] - a[2]) * t,
  );
}
