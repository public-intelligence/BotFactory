/**
 * The bot's transcription vocabulary as the panel's text box holds it: one term per line,
 * the words the speech recogniser should favour (the bot's name, institutions, acronyms).
 * Mirrors the API's `TranscriptionTerms`: the same separators, the same clean-up and the
 * same limits (50 terms of up to 20 characters, the recogniser's for live transcription),
 * so the panel can say what is wrong before saving. The API validates again.
 */
export const MAX_TERMS = 50;
export const MAX_TERM_LENGTH = 20;

/** Splits on lines, commas and semicolons; trims, collapses spaces, drops empties and repeats. */
export function parseTerms(raw: string): string[] {
  const terms: string[] = [];
  const seen = new Set<string>();
  for (const part of raw.split(/[\n\r,;]+/)) {
    const term = part.trim().replace(/\s+/g, ' ');
    if (!term) continue;
    const key = term.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    terms.push(term);
  }
  return terms;
}

export interface VocabularyCheck {
  readonly terms: string[];
  readonly tooLong: string[];
  readonly tooMany: boolean;
}

export function checkVocabulary(raw: string): VocabularyCheck {
  const terms = parseTerms(raw);
  return {
    terms,
    tooLong: terms.filter((t) => t.length > MAX_TERM_LENGTH),
    tooMany: terms.length > MAX_TERMS,
  };
}

/** What stops the list from being saved, in the panel's words; null when nothing does. */
export function vocabularyProblem(check: VocabularyCheck): string | null {
  if (check.tooMany) {
    return `Hay ${check.terms.length} términos; el máximo es ${MAX_TERMS}.`;
  }
  if (check.tooLong.length > 0) {
    const list = check.tooLong.map((t) => `«${t}»`).join(', ');
    return `Estos términos tienen más de ${MAX_TERM_LENGTH} caracteres: ${list}.`;
  }
  return null;
}
