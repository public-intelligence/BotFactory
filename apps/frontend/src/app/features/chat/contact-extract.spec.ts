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
});
