import { describe, expect, it } from 'vitest';
import {
  MAX_TERM_LENGTH,
  MAX_TERMS,
  checkVocabulary,
  parseTerms,
  vocabularyProblem,
} from './transcription-vocabulary';

describe('transcription-vocabulary', () => {
  it('parseTerms accepts lines, commas and semicolons', () => {
    expect(parseTerms('Aurora\nABCD, XYZ; DEFG\r\nPunto Ciudadano')).toEqual([
      'Aurora',
      'ABCD',
      'XYZ',
      'DEFG',
      'Punto Ciudadano',
    ]);
  });

  it('parseTerms trims, collapses spaces and drops empties and repeats (case aside)', () => {
    expect(parseTerms('  Punto   Ciudadano \n\n punto ciudadano\n  \nAvanza\nAVANZA')).toEqual([
      'Punto Ciudadano',
      'Avanza',
    ]);
  });

  it('parseTerms of nothing is empty', () => {
    expect(parseTerms('')).toEqual([]);
    expect(parseTerms('  \n , ; ')).toEqual([]);
  });

  it('the terms ABCD asked for are fine', () => {
    const check = checkVocabulary('Aurora\nABCD\nXYZ\nDEFG\nAvanza\nPunto Ciudadano\nQuisqueya\nZETA');
    expect(check.terms).toHaveLength(8);
    expect(vocabularyProblem(check)).toBeNull();
  });

  it('exactly the limits are fine', () => {
    const raw = Array.from({ length: MAX_TERMS }, (_, i) => `t${String(i).padStart(2, '0')}`.padEnd(MAX_TERM_LENGTH, 'x')).join('\n');
    expect(vocabularyProblem(checkVocabulary(raw))).toBeNull();
  });

  it('too many terms is reported with the count', () => {
    const raw = Array.from({ length: MAX_TERMS + 1 }, (_, i) => `término${i}`).join('\n');
    const problem = vocabularyProblem(checkVocabulary(raw));
    expect(problem).toContain(String(MAX_TERMS + 1));
    expect(problem).toContain(String(MAX_TERMS));
  });

  it('a term over the length limit is reported by name', () => {
    const problem = vocabularyProblem(
      checkVocabulary('Aurora\nInstituto Nacional de Tránsito y Transporte Terrestre\nXYZ'),
    );
    expect(problem).toContain('Instituto Nacional de Tránsito y Transporte Terrestre');
    expect(problem).not.toContain('Aurora');
  });

  it('repeats do not count toward the limit', () => {
    const raw = Array.from({ length: 60 }, (_, i) => (i % 2 === 0 ? 'XYZ' : 'xyz')).join('\n');
    expect(vocabularyProblem(checkVocabulary(raw))).toBeNull();
  });
});
