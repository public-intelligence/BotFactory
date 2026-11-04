import { describe, expect, it } from 'vitest';
import { formatAssistantMessage, parseInline, splitLines, type MessageBlock } from './message-format';

const kinds = (blocks: MessageBlock[]) =>
  blocks.map((b) => (b.kind === 'list' ? `list:${b.ordered ? 'ol' : 'ul'}:${b.items.length}` : b.kind));

const textOf = (segments: { value: string }[]) => segments.map((s) => s.value).join('');

describe('message-format', () => {
  it('builds a lead sentence, a bullet list and a closing paragraph from real line breaks', () => {
    const blocks = formatAssistantMessage(
      'Los requisitos son:\n- Cita impresa.\n- Acta de nacimiento original con código QR.\n\nEl costo vigente lo publica la Dirección General de Pasaportes.',
    );
    expect(kinds(blocks)).toEqual(['paragraph', 'list:ul:2', 'paragraph']);
    const lead = blocks[0]!;
    expect(lead.kind === 'paragraph' && lead.lead).toBe(true);
    const list = blocks[1]!;
    if (list.kind !== 'list') throw new Error('expected a list');
    expect(textOf(list.items[1]!.segments)).toBe('Acta de nacimiento original con código QR.');
  });

  it('keeps the numbers the model wrote in a numbered list', () => {
    const blocks = formatAssistantMessage('Pasos:\n1. Examen médico.\n2. Curso de manejo.\n3. Examen teórico.');
    const list = blocks[1]!;
    if (list.kind !== 'list') throw new Error('expected a list');
    expect(list.ordered).toBe(true);
    expect(list.items.map((i) => i.marker)).toEqual(['1.', '2.', '3.']);
  });

  it('rebuilds a bullet list from an answer the backend stored flattened', () => {
    // Real stored answer (conversation 831, turn 3), before line breaks were kept.
    const blocks = formatAssistantMessage(
      'En general, necesitará: - Cédula de Identidad y Electoral de los padres o pasaporte si son extranjeros. - Certificado de la clínica u hospital donde ocurrió el nacimiento. - Acta de matrimonio actualizada, si aplica.  La solicitud de expedición de acta tiene un costo de seiscientos pesos.',
    );
    expect(kinds(blocks)).toEqual(['paragraph', 'list:ul:3', 'paragraph']);
  });

  it('rebuilds a numbered list from a flattened answer', () => {
    const blocks = formatAssistantMessage(
      'Los pasos generales son: 1. Realizarse un examen médico. 2. Tomar el curso de manejo. 3. Aprobar el examen teórico.',
    );
    expect(kinds(blocks)).toEqual(['paragraph', 'list:ol:3']);
  });

  it('splits "•" bullets and the double-space separators TainaApp also handles', () => {
    expect(kinds(formatAssistantMessage('Requisitos: • Cita impresa • Acta de nacimiento'))).toEqual([
      'paragraph',
      'list:ul:2',
    ]);
    expect(kinds(formatAssistantMessage('Requisitos:  •  Cita impresa.  •  Acta.'))).toEqual(['paragraph', 'list:ul:2']);
  });

  it('does not break hours, versions or single dashes into lists', () => {
    expect(kinds(formatAssistantMessage('Atienden de 8:00 a. m. - 5:00 p. m. de lunes a viernes.'))).toEqual(['paragraph']);
    expect(kinds(formatAssistantMessage('Desde la versión 2. El trámite cambió de lugar.'))).toEqual(['paragraph']);
    expect(kinds(formatAssistantMessage('Vaya a la oficina. - Lleve su cédula.'))).toEqual(['paragraph']);
  });

  it('reads a heading', () => {
    expect(kinds(formatAssistantMessage('## Requisitos\nTraiga su cédula.'))).toEqual(['heading', 'paragraph']);
  });

  it('marks bold and drops the markers', () => {
    const segs = parseInline('Esto es **importante** hoy.');
    expect(segs.map((s) => [s.value, !!s.bold])).toEqual([
      ['Esto es ', false],
      ['importante', true],
      [' hoy.', false],
    ]);
  });

  it('shows an unclosed bold (still streaming) as bold, without asterisks', () => {
    const segs = parseInline('Los **requisitos son');
    expect(segs.map((s) => [s.value, !!s.bold])).toEqual([
      ['Los ', false],
      ['requisitos son', true],
    ]);
  });

  it('keeps the *462 service code and makes it dialable, and drops stray asterisks', () => {
    const segs = parseInline('Llame al *462 o *visite* www.gob.do');
    const phone = segs.find((s) => s.type === 'phone');
    expect(phone?.value).toBe('*462');
    expect(phone && 'href' in phone ? phone.href : '').toBe('tel:*462');
    expect(textOf(segs)).toBe('Llame al *462 o visite www.gob.do');
  });

  it('turns markdown links into links with their label', () => {
    const segs = parseInline('Vea la [ficha del trámite](https://www.gob.do/servicios/x).');
    const link = segs.find((s) => s.type === 'url');
    expect(link?.value).toBe('ficha del trámite');
    expect(link && 'href' in link ? link.href : '').toBe('https://www.gob.do/servicios/x');
  });

  it('does not turn street numbers or postal codes into phone links', () => {
    const segs = parseInline(
      'El Consulado está en la 1038 Brickell Avenue, Suite 410, Miami, Florida 33131.',
    );
    expect(segs.some((s) => s.type === 'phone')).toBe(false);
  });

  it('keeps the contact line as one paragraph with each contact tappable', () => {
    const blocks = formatAssistantMessage(
      'Por favor, vea los datos de contacto: 809-532-4233 · comunicaciones@pasaportes.gob.do · https://www.gob.do',
    );
    expect(kinds(blocks)).toEqual(['paragraph']);
    const segs = blocks[0]!.kind === 'paragraph' ? blocks[0]!.segments : [];
    expect(segs.filter((s) => s.type !== 'text').map((s) => s.type)).toEqual(['phone', 'email', 'url']);
  });

  it('ignores empty input and blank lines', () => {
    expect(formatAssistantMessage('')).toEqual([]);
    expect(splitLines('Hola.\n\n\nAdiós.')).toEqual(['Hola.', 'Adiós.']);
  });
});
