import { describe, expect, it } from 'vitest';
import { extractContacts, linkifySegments } from './contact-extract';

describe('contact-extract', () => {
  it('extractContacts finds urls and phones', () => {
    const text = 'Visita https://example.gob.do/ayuda o llama al +1 (809) 555-1212.';
    const result = extractContacts(text);
    expect(result.urls).toContain('https://example.gob.do/ayuda');
    expect(result.phones.some((p) => p.replace(/\D/g, '').includes('8095551212'))).toBe(true);
  });

  it('extractContacts dedupes and skips phones inside urls', () => {
    const text = 'https://x.example/path/8095551212 and again https://x.example/path/8095551212';
    const result = extractContacts(text);
    expect(result.urls).toHaveLength(1);
    expect(result.phones).toHaveLength(0);
  });

  it('extractContacts returns empty for blank input', () => {
    expect(extractContacts('')).toEqual({ urls: [], phones: [], emails: [] });
  });

  it('linkifySegments splits text and url parts', () => {
    const segments = linkifySegments('Antes https://a.gob.do despues');
    expect(segments).toEqual([
      { type: 'text', value: 'Antes ' },
      { type: 'url', value: 'https://a.gob.do', href: 'https://a.gob.do' },
      { type: 'text', value: ' despues' },
    ]);
  });

  it('linkifySegments prefixes www urls with https', () => {
    const segments = linkifySegments('ver www.example.com.');
    const url = segments.find((s) => s.type === 'url');
    expect(url).toEqual({
      type: 'url',
      value: 'www.example.com',
      href: 'https://www.example.com',
    });
  });
  it('short numbers are phones only when the words before them say so', () => {
    const phones = (t: string) =>
      linkifySegments(t)
        .filter((s) => s.type === 'phone')
        .map((s) => s.value);
    expect(phones('Está en la 1038 Brickell Avenue, Suite 410, Florida 33131.')).toEqual([]);
    expect(phones('En caso de emergencia llame al 911.')).toEqual(['911']);
    expect(phones('Escriba a la línea 1311 de atención.')).toEqual(['1311']);
  });

  it('star service codes keep the star when dialed', () => {
    const seg = linkifySegments('Llame al *462 desde cualquier teléfono.').find((s) => s.type === 'phone');
    expect(seg?.value).toBe('*462');
    expect(seg && 'href' in seg ? seg.href : '').toBe('tel:*462');
    expect(extractContacts('Marque *462.').phones).toEqual(['*462']);
  });

  it('extractContacts leaves addresses out of the phone list', () => {
    expect(extractContacts('1501 Broadway, Suite 410, New York, NY 10036').phones).toEqual([]);
  });
});
