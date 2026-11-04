/**
 * The bot's pronunciation rules as the panel's text box holds them: one rule per line,
 * `escrito → dicho` («ABC → a, be, ce»). They say how the voice should say what the text writes;
 * they never change the chat or the stored text. Mirrors the API's `PronunciationDictionary`: the
 * same separators, clean-up and limits, so the panel can say what is wrong before saving.
 * The API validates again.
 */
export const MAX_RULES = 100;
export const MAX_WRITTEN_LENGTH = 50;
export const MAX_SPOKEN_LENGTH = 100;
export const ARROW = '→';

// «=>» before «=», so the longer one wins where both start at the same place.
const SEPARATORS = [ARROW, '=>', '->', '='];

export interface PronunciationRule {
  readonly written: string;
  readonly spoken: string;
}

export interface RuleCheck {
  readonly rules: PronunciationRule[];
  /** What stops the list from being saved, in the panel's words; empty when nothing does. */
  readonly problems: string[];
}

function collapse(s: string): string {
  return s.trim().replace(/\s+/g, ' ');
}

function shorten(s: string): string {
  return s.length <= 60 ? s : `${s.slice(0, 60)}…`;
}

function split(line: string): PronunciationRule | null {
  let at = -1;
  let length = 0;
  for (const separator of SEPARATORS) {
    const index = line.indexOf(separator);
    if (index < 0) continue;
    if (at < 0 || index < at || (index === at && separator.length > length)) {
      at = index;
      length = separator.length;
    }
  }
  if (at < 0) return null;
  return { written: collapse(line.slice(0, at)), spoken: collapse(line.slice(at + length)) };
}

/** The rules of a text, in order; repeats of a written form (case aside) keep the first. */
export function parseRules(raw: string): PronunciationRule[] {
  const rules: PronunciationRule[] = [];
  const seen = new Set<string>();
  for (const line of raw.split(/[\n\r]+/).map((l) => l.trim())) {
    if (!line) continue;
    const rule = split(line);
    if (!rule || !rule.written || !rule.spoken) continue;
    const key = rule.written.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    rules.push(rule);
  }
  return rules;
}

export function checkRules(raw: string): RuleCheck {
  const problems: string[] = [];
  const seen = new Set<string>();
  let count = 0;
  for (const line of raw.split(/[\n\r]+/).map((l) => l.trim())) {
    if (!line) continue;
    const rule = split(line);
    if (!rule) {
      problems.push(`Falta la flecha «${ARROW}» (escrito ${ARROW} dicho): ${shorten(line)}`);
      continue;
    }
    if (!rule.written || !rule.spoken) {
      problems.push(`Un lado de la regla está vacío: ${shorten(line)}`);
      continue;
    }
    if (rule.written.length > MAX_WRITTEN_LENGTH) {
      problems.push(`Lo escrito pasa de ${MAX_WRITTEN_LENGTH} caracteres: ${shorten(rule.written)}`);
    }
    if (rule.spoken.length > MAX_SPOKEN_LENGTH) {
      problems.push(`Lo dicho pasa de ${MAX_SPOKEN_LENGTH} caracteres: ${shorten(rule.spoken)}`);
    }
    const key = rule.written.toLowerCase();
    if (seen.has(key)) {
      problems.push(`«${rule.written}» está repetido.`);
    }
    seen.add(key);
    count++;
  }
  if (count > MAX_RULES) {
    problems.push(`Hay ${count} reglas; el máximo es ${MAX_RULES}.`);
  }
  return { rules: parseRules(raw), problems };
}

/** The first thing that stops the list from being saved, or null. */
export function rulesProblem(check: RuleCheck): string | null {
  return check.problems[0] ?? null;
}
