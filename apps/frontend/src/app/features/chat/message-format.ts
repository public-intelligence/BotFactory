/**
 * Turns an assistant answer into readable blocks for the chat: paragraphs, bullet
 * and numbered lists, headings, with bold and links inside. Pure functions, no HTML:
 * the component renders the blocks through Angular's escaping, never innerHTML.
 *
 * Answers now keep their line breaks ("Requisitos:\n- Cita\n- Acta"). Older stored
 * answers were flattened to one line by the backend, so a fallback rebuilds their
 * structure from what survived: two or more spaces where a break was, "•" bullets,
 * and "- " / "1. " items right after a sentence ends. Same approach as TainaApp.
 */
import { linkifySegments, telHref, type TextSegment } from './contact-extract';

/** Inline text: plain, or a link (url / email / phone), optionally bold. */
export type InlineSegment = TextSegment & { bold?: boolean };

export interface ListItem {
  /** "•" for bullets, "1." / "2)" for numbered items (the number the model wrote). */
  marker: string;
  segments: InlineSegment[];
}

export type MessageBlock =
  | { kind: 'paragraph'; segments: InlineSegment[]; lead: boolean }
  | { kind: 'heading'; segments: InlineSegment[] }
  | { kind: 'list'; ordered: boolean; items: ListItem[] };

const BULLET_LINE = /^[-*•–]\s+(\S.*)$/;
const NUMBERED_LINE = /^(\d{1,2})([.)])\s+(\S.*)$/;
const HEADING_LINE = /^#{1,6}\s+(\S.*)$/;
const MARKER_ONLY = /^(?:[-*•–]|\d{1,2}[.)]|#{1,6})$/;

// Flattened-text fallback. "•" is unambiguous, so any " • item" opens a line.
const INLINE_DOT_BULLET = /[ \t]+(?=•[ \t]+\S)/g;
// "- item" / "1. item" only right after a sentence ends and before a capital, and
// only when the line has at least two of them: "8:00 a. m. - 5:00" or "versión 2. El"
// must stay as they are.
const DASH_AFTER_SENTENCE = /(?<=[.;:!?])[ \t]+(?=[-*–][ \t]+[A-ZÁÉÍÓÚÑ¿¡"“])/g;
const NUMBER_AFTER_SENTENCE = /(?<=[.;:!?])[ \t]+(?=\d{1,2}[.)][ \t]+[A-ZÁÉÍÓÚÑ¿¡"“])/g;
const PARAGRAPH_GAP = /[ \t]{2,}/;

const MD_LINK = /\[([^\]\n]+)\]\(((?:https?:\/\/|tel:|mailto:)[^)\s]+)\)/g;
/** A single asterisk that is not a star code (*462) is markup noise once bold is parsed. */
const STRAY_ASTERISK = /\*(?!\d)/g;

const CACHE_LIMIT = 300;
const cache = new Map<string, MessageBlock[]>();

/** Blocks for an assistant answer; cached, since the template asks on every render. */
export function formatAssistantMessage(text: string): MessageBlock[] {
  if (!text) return [];
  const hit = cache.get(text);
  if (hit) return hit;

  const blocks = buildBlocks(splitLines(text));
  cache.set(text, blocks);
  if (cache.size > CACHE_LIMIT) {
    const oldest = cache.keys().next().value;
    if (oldest !== undefined) cache.delete(oldest);
  }
  return blocks;
}

/** One logical element per line, rebuilding breaks a flattened answer lost. */
export function splitLines(text: string): string[] {
  const normalized = text.replace(/\r\n?/g, '\n');
  const hasBreaks = normalized.includes('\n');

  const lines: string[] = [];
  for (const raw of normalized.split('\n')) {
    let line = raw.replace(INLINE_DOT_BULLET, '\n');
    line = splitWhenRepeated(line, DASH_AFTER_SENTENCE);
    line = splitWhenRepeated(line, NUMBER_AFTER_SENTENCE);
    for (const part of line.split('\n')) {
      // Without real breaks, two or more spaces are where a paragraph or item ended.
      const pieces = hasBreaks ? [part] : part.split(PARAGRAPH_GAP);
      lines.push(...joinLoneMarkers(pieces));
    }
  }
  return lines.map((l) => l.trim()).filter((l) => l.length > 0);
}

function splitWhenRepeated(line: string, pattern: RegExp): string {
  const count = line.match(pattern)?.length ?? 0;
  return count >= 2 ? line.replace(pattern, '\n') : line;
}

/** "•" or "1." left alone by a split belongs to the text after it. */
function joinLoneMarkers(pieces: string[]): string[] {
  const out: string[] = [];
  for (let i = 0; i < pieces.length; i++) {
    const piece = pieces[i]!.trim();
    if (MARKER_ONLY.test(piece) && i + 1 < pieces.length) {
      out.push(`${piece} ${pieces[i + 1]!.trim()}`);
      i++;
    } else {
      out.push(piece);
    }
  }
  return out;
}

function buildBlocks(lines: string[]): MessageBlock[] {
  const blocks: MessageBlock[] = [];

  for (const line of lines) {
    const bullet = BULLET_LINE.exec(line);
    const numbered = bullet ? null : NUMBERED_LINE.exec(line);
    if (bullet || numbered) {
      const ordered = numbered !== null;
      const item: ListItem = bullet
        ? { marker: '•', segments: parseInline(bullet[1]!) }
        : { marker: `${numbered![1]}${numbered![2]}`, segments: parseInline(numbered![3]!) };
      const last = blocks[blocks.length - 1];
      if (last?.kind === 'list' && last.ordered === ordered) {
        last.items.push(item);
      } else {
        blocks.push({ kind: 'list', ordered, items: [item] });
      }
      continue;
    }

    const heading = HEADING_LINE.exec(line);
    if (heading) {
      blocks.push({ kind: 'heading', segments: parseInline(heading[1]!) });
      continue;
    }

    blocks.push({ kind: 'paragraph', segments: parseInline(line), lead: false });
  }

  // A sentence that introduces a list ("Los requisitos son:") reads as its title.
  for (let i = 0; i + 1 < blocks.length; i++) {
    const block = blocks[i]!;
    if (block.kind === 'paragraph' && blocks[i + 1]!.kind === 'list' && endsWithColon(block.segments)) {
      block.lead = true;
    }
  }
  return blocks;
}

function endsWithColon(segments: InlineSegment[]): boolean {
  const last = segments[segments.length - 1];
  return last?.type === 'text' && last.value.trimEnd().endsWith(':');
}

/** Bold (**…**), markdown links, then the existing URL / e-mail / phone detection. */
export function parseInline(line: string): InlineSegment[] {
  const out: InlineSegment[] = [];
  // Split on "**": odd pieces are bold. An unclosed "**" (still streaming) makes the
  // rest bold, which is what it will be once the closing marker arrives.
  const runs = line.split('**');
  runs.forEach((run, index) => {
    if (!run) return;
    const bold = index % 2 === 1;
    for (const piece of splitMarkdownLinks(run)) {
      if (piece.type !== 'text') {
        out.push(bold ? { ...piece, bold } : piece);
        continue;
      }
      const cleaned = piece.value.replace(STRAY_ASTERISK, '');
      if (!cleaned) continue;
      for (const seg of linkifySegments(cleaned)) {
        out.push(bold ? { ...seg, bold } : seg);
      }
    }
  });
  return mergeText(out);
}

function splitMarkdownLinks(text: string): TextSegment[] {
  const out: TextSegment[] = [];
  let last = 0;
  for (const m of text.matchAll(MD_LINK)) {
    const start = m.index ?? 0;
    if (start > last) out.push({ type: 'text', value: text.slice(last, start) });
    const label = m[1]!;
    const href = m[2]!;
    if (href.startsWith('tel:')) {
      out.push({ type: 'phone', value: label, href: telHref(href.slice(4)) });
    } else if (href.startsWith('mailto:')) {
      out.push({ type: 'email', value: label, href });
    } else {
      out.push({ type: 'url', value: label, href });
    }
    last = start + m[0].length;
  }
  if (last < text.length) out.push({ type: 'text', value: text.slice(last) });
  return out;
}

/** Adjacent plain pieces with the same weight read as one. */
function mergeText(segments: InlineSegment[]): InlineSegment[] {
  const out: InlineSegment[] = [];
  for (const seg of segments) {
    const prev = out[out.length - 1];
    if (prev && prev.type === 'text' && seg.type === 'text' && !!prev.bold === !!seg.bold) {
      out[out.length - 1] = { ...prev, value: prev.value + seg.value };
    } else {
      out.push(seg);
    }
  }
  return out;
}
