import { describe, expect, it } from 'vitest';
import {
  MAX_RULES,
  MAX_SPOKEN_LENGTH,
  MAX_WRITTEN_LENGTH,
  checkRules,
  parseRules,
  rulesProblem,
} from './pronunciation-rules';

describe('pronunciation-rules', () => {
  it('parseRules accepts every arrow spelling', () => {
    expect(parseRules('ABC → a, be, ce\nXYZ -> equis\nQRS => cu\nTUV = te')).toEqual([
      { written: 'ABC', spoken: 'a, be, ce' },
      { written: 'XYZ', spoken: 'equis' },
      { written: 'QRS', spoken: 'cu' },
      { written: 'TUV', spoken: 'te' },
    ]);
  });

  it('parseRules trims, collapses spaces and skips blank lines and what is not a rule', () => {
    const raw = '  Punto   ABC  →   punto   a,  be,  ce \n\n   \nsin flecha\n → vacío\nvacío → \nXYZ → equis';
    expect(parseRules(raw)).toEqual([
      { written: 'Punto ABC', spoken: 'punto a, be, ce' },
      { written: 'XYZ', spoken: 'equis' },
    ]);
  });

  it('parseRules keeps the first of a repeated written form, case aside', () => {
    expect(parseRules('ABC → primera\nabc → segunda')).toEqual([{ written: 'ABC', spoken: 'primera' }]);
  });

  it('a list of good rules has no problem', () => {
    const check = checkRules('ABC → a, be, ce\nPunto ABC → punto a, be, ce');
    expect(check.rules).toHaveLength(2);
    expect(rulesProblem(check)).toBeNull();
    expect(rulesProblem(checkRules(''))).toBeNull();
  });

  it('a line without an arrow is reported with the line', () => {
    expect(rulesProblem(checkRules('esto no es una regla'))).toContain('esto no es una regla');
  });

  it('an empty side is reported', () => {
    expect(rulesProblem(checkRules('→ solo lo dicho'))).toContain('vacío');
    expect(rulesProblem(checkRules('solo lo escrito →'))).toContain('vacío');
  });

  it('a repeated written form is reported by name', () => {
    expect(rulesProblem(checkRules('ABC → a\nabc → b'))?.toLowerCase()).toContain('abc');
  });

  it('the length limits are reported, and exactly the limits are fine', () => {
    expect(rulesProblem(checkRules(`${'x'.repeat(MAX_WRITTEN_LENGTH + 1)} → a`))).toContain('escrito');
    expect(rulesProblem(checkRules(`ABC → ${'y'.repeat(MAX_SPOKEN_LENGTH + 1)}`))).toContain('dicho');
    expect(rulesProblem(checkRules(`${'x'.repeat(MAX_WRITTEN_LENGTH)} → ${'y'.repeat(MAX_SPOKEN_LENGTH)}`))).toBeNull();
  });

  it('more rules than the limit is reported with the count', () => {
    const raw = Array.from({ length: MAX_RULES + 1 }, (_, i) => `sigla${i} → dicho${i}`).join('\n');
    const problem = rulesProblem(checkRules(raw));
    expect(problem).toContain(String(MAX_RULES + 1));
  });
});
