/** Extract URLs / phones / emails and split text into linkify segments for the chat UI. */

export interface ExtractedContacts {
  urls: string[];
  phones: string[];
  emails: string[];
}

export type TextSegment =
  | { type: 'text'; value: string }
  | { type: 'url'; value: string; href: string }
  | { type: 'email'; value: string; href: string }
  | { type: 'phone'; value: string; href: string };

/** Explicit schemes, www., or bare hostnames (filtered by plausible TLD). */
const URL_RE =
  /https?:\/\/[^\s<>"']+|www\.[^\s<>"']+|\b[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+(?:\/[^\s<>"']*)?/gi;
const EMAIL_RE = /\b[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}\b/gi;
const PHONE_RE = /\+?\d[\d\s\-().]{5,22}\d/g;
const SHORT_PHONE_RE = /\(?\b\d{3,6}\b\)?/g;
const TRAILING_URL_JUNK = /[.,;:!?\]\)\}'»"]+$/;

const KNOWN_TLDS = new Set([
  'com',
  'org',
  'net',
  'edu',
  'gov',
  'io',
  'app',
  'info',
  'lat',
  'do',
  'cr',
  'es',
  'mx',
]);

const KNOWN_MULTI_TLDS = new Set([
  'gob.do',
  'go.cr',
  'com.do',
  'com.cr',
  'gov.do',
]);

function trimUrl(raw: string): string {
  return raw.replace(TRAILING_URL_JUNK, '');
}

function hrefFor(url: string): string {
  return /^https?:\/\//i.test(url) ? url : `https://${url}`;
}

/** tel: href with digits only; keeps a leading + for international numbers. */
export function telHref(phone: string): string {
  const trimmed = phone.trim();
  const hasPlus = trimmed.startsWith('+');
  const digits = trimmed.replace(/\D/g, '');
  return hasPlus ? `tel:+${digits}` : `tel:${digits}`;
}

function isPlausibleUrl(candidate: string): boolean {
  if (/^(https?:\/\/|www\.)/i.test(candidate)) return true;

  const host = candidate.split('/')[0] ?? '';
  const parts = host.split('.');
  if (parts.length < 2) return false;
  if (parts.some((p) => p.length === 0)) return false;
  if (parts.every((p) => /^\d+$/.test(p))) return false;

  const tld = parts[parts.length - 1]!.toLowerCase();
  if (KNOWN_TLDS.has(tld)) return true;

  if (parts.length >= 3) {
    const multi = `${parts[parts.length - 2]}.${parts[parts.length - 1]}`.toLowerCase();
    if (KNOWN_MULTI_TLDS.has(multi)) return true;
  }

  return false;
}

function countDigits(s: string): number {
  let n = 0;
  for (const c of s) {
    if (c >= '0' && c <= '9') n++;
  }
  return n;
}

function normalizePhone(raw: string): string | null {
  let s = raw.trim().replace(/[\s\-().]+$/g, '');
  const digits = countDigits(s);
  if (digits < 7 || digits > 15) return null;
  return s;
}

function normalizeShortPhone(raw: string): string | null {
  const wrapped = raw.trim();
  const digitsOnly = wrapped.replace(/\D/g, '');
  if (digitsOnly.length < 3 || digitsOnly.length > 6) return null;
  if (digitsOnly.length === 4) {
    const year = Number(digitsOnly);
    if (year >= 1900 && year <= 2099) return null;
  }
  // Prefer display with original parentheses when present.
  if (/^\(\d{3,6}\)$/.test(wrapped)) return wrapped;
  return digitsOnly;
}

function rangesOverlap(
  covered: Array<{ start: number; end: number }>,
  start: number,
  end: number,
): boolean {
  return covered.some((r) => start < r.end && end > r.start);
}

/** Pull unique URLs, emails and phone numbers from assistant/user message text. */
export function extractContacts(text: string): ExtractedContacts {
  if (!text) return { urls: [], phones: [], emails: [] };

  const urls: string[] = [];
  const urlSeen = new Set<string>();
  const covered: Array<{ start: number; end: number }> = [];

  for (const m of text.matchAll(URL_RE)) {
    const raw = m[0];
    const cleaned = trimUrl(raw);
    if (!cleaned || !isPlausibleUrl(cleaned)) continue;
    // Skip URL-shaped matches that are really emails (local@host).
    if (cleaned.includes('@')) continue;
    const start = m.index ?? 0;
    if (start > 0 && text[start - 1] === '@') continue;
    const end = start + raw.length;
    covered.push({ start, end });
    const key = cleaned.toLowerCase();
    if (!urlSeen.has(key)) {
      urlSeen.add(key);
      urls.push(cleaned);
    }
  }

  const emails: string[] = [];
  const emailSeen = new Set<string>();
  for (const m of text.matchAll(EMAIL_RE)) {
    const start = m.index ?? 0;
    const end = start + m[0].length;
    if (rangesOverlap(covered, start, end)) continue;
    covered.push({ start, end });
    const email = m[0];
    const key = email.toLowerCase();
    if (!emailSeen.has(key)) {
      emailSeen.add(key);
      emails.push(email);
    }
  }

  const phones: string[] = [];
  const phoneSeen = new Set<string>();

  for (const m of text.matchAll(PHONE_RE)) {
    const start = m.index ?? 0;
    const end = start + m[0].length;
    if (rangesOverlap(covered, start, end)) continue;
    const before = start > 0 ? text[start - 1] : '';
    const after = end < text.length ? text[end] : '';
    if (/[a-zA-Z]/.test(before) || /[a-zA-Z]/.test(after)) continue;
    const phone = normalizePhone(m[0]);
    if (!phone) continue;
    covered.push({ start, end });
    const key = phone.replace(/\D/g, '');
    if (!phoneSeen.has(key)) {
      phoneSeen.add(key);
      phones.push(phone);
    }
  }

  for (const m of text.matchAll(SHORT_PHONE_RE)) {
    const start = m.index ?? 0;
    const end = start + m[0].length;
    if (rangesOverlap(covered, start, end)) continue;
    const before = start > 0 ? text[start - 1] : '';
    const after = end < text.length ? text[end] : '';
    if (/[a-zA-Z0-9]/.test(before) || /[a-zA-Z0-9]/.test(after)) continue;
    const phone = normalizeShortPhone(m[0]);
    if (!phone) continue;
    covered.push({ start, end });
    const key = phone.replace(/\D/g, '');
    if (!phoneSeen.has(key)) {
      phoneSeen.add(key);
      phones.push(phone);
    }
  }

  return { urls, phones, emails };
}

type MatchHit = { start: number; end: number; seg: TextSegment };

/** Split content into plain text, URL and email segments for safe template rendering. */
export function linkifySegments(text: string): TextSegment[] {
  if (!text) return [];

  const hits: MatchHit[] = [];

  for (const m of text.matchAll(URL_RE)) {
    const start = m.index ?? 0;
    const raw = m[0];
    const cleaned = trimUrl(raw);
    if (!cleaned || !isPlausibleUrl(cleaned) || cleaned.includes('@')) continue;
    if (start > 0 && text[start - 1] === '@') continue;
    hits.push({
      start,
      end: start + raw.length,
      seg: { type: 'url', value: cleaned, href: hrefFor(cleaned) },
    });
  }

  for (const m of text.matchAll(EMAIL_RE)) {
    const start = m.index ?? 0;
    const email = m[0];
    hits.push({
      start,
      end: start + email.length,
      seg: { type: 'email', value: email, href: `mailto:${email}` },
    });
  }

  const phoneCovered: Array<{ start: number; end: number }> = hits.map((h) => ({
    start: h.start,
    end: h.end,
  }));

  for (const m of text.matchAll(PHONE_RE)) {
    const start = m.index ?? 0;
    const end = start + m[0].length;
    if (rangesOverlap(phoneCovered, start, end)) continue;
    const before = start > 0 ? text[start - 1] : '';
    const after = end < text.length ? text[end] : '';
    if (/[a-zA-Z]/.test(before) || /[a-zA-Z]/.test(after)) continue;
    const phone = normalizePhone(m[0]);
    if (!phone) continue;
    phoneCovered.push({ start, end });
    hits.push({
      start,
      end,
      seg: { type: 'phone', value: phone, href: telHref(phone) },
    });
  }

  for (const m of text.matchAll(SHORT_PHONE_RE)) {
    const start = m.index ?? 0;
    const end = start + m[0].length;
    if (rangesOverlap(phoneCovered, start, end)) continue;
    const before = start > 0 ? text[start - 1] : '';
    const after = end < text.length ? text[end] : '';
    if (/[a-zA-Z0-9]/.test(before) || /[a-zA-Z0-9]/.test(after)) continue;
    const phone = normalizeShortPhone(m[0]);
    if (!phone) continue;
    phoneCovered.push({ start, end });
    hits.push({
      start,
      end,
      seg: { type: 'phone', value: phone, href: telHref(phone) },
    });
  }

  hits.sort((a, b) => a.start - b.start);

  const segments: TextSegment[] = [];
  let last = 0;
  for (const hit of hits) {
    if (hit.start < last) continue; // overlap
    if (hit.start > last) {
      segments.push({ type: 'text', value: text.slice(last, hit.start) });
    }
    const raw = text.slice(hit.start, hit.end);
    if (hit.seg.type === 'url') {
      const trailing = raw.slice(hit.seg.value.length);
      segments.push(hit.seg);
      if (trailing) segments.push({ type: 'text', value: trailing });
    } else if (hit.seg.type === 'phone') {
      // Keep original punctuation (e.g. parentheses) as the visible label when present.
      segments.push({ ...hit.seg, value: raw.trim() });
    } else {
      segments.push(hit.seg);
    }
    last = hit.end;
  }
  if (last < text.length) {
    segments.push({ type: 'text', value: text.slice(last) });
  }
  return segments.length > 0 ? segments : [{ type: 'text', value: text }];
}
