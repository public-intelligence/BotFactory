import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { of } from 'rxjs';
import { ConversationStreamService } from './conversation-stream.service';
import { Api } from '../../api/api';
import { PublicSessionService } from './public-session.service';

describe('ConversationStreamService', () => {
  let service: ConversationStreamService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ConversationStreamService,
        PublicSessionService,
        {
          provide: Api,
          useValue: { invoke: vi.fn() },
        },
        {
          provide: HttpClient,
          useValue: {
            get: vi.fn(() => of('session-guid')),
          },
        },
      ],
    });
    service = TestBed.inject(ConversationStreamService);
  });

  it('starts idle with empty items', () => {
    expect(service.state()).toBe('idle');
    expect(service.items()).toEqual([]);
    expect(service.sendBusy()).toBe(false);
  });

  it('sendTurn throws when not connected', async () => {
    await expect(service.sendTurn('hola')).rejects.toThrow('Not connected');
  });

  it('cancelActiveTurn is a no-op without active turn', async () => {
    await expect(service.cancelActiveTurn()).resolves.toBeUndefined();
  });

  it('disconnect resets stream state', async () => {
    service.state.set('connected');
    service.eventsSubscribed.set(true);
    service.turnInFlight.set(true);
    service.activeTurnId.set('abc');
    service.streamingText.set('partial');

    await service.disconnect();

    expect(service.state()).toBe('idle');
    expect(service.eventsSubscribed()).toBe(false);
    expect(service.turnInFlight()).toBe(false);
    expect(service.activeTurnId()).toBeNull();
    expect(service.streamingText()).toBe('');
    expect(service.items()).toEqual([]);
  });

  it('disconnect keeps what is on screen when asked to (a new conversation replaces a closed one)', async () => {
    const items = (service as unknown as { itemsByKey: { set(v: Map<string, unknown>): void } }).itemsByKey;
    items.set(new Map([['message:1', { kind: 'message', occurredAt: '2026-11-04T10:00:00Z', data: { id: 1 } }]]));
    expect(service.items().length).toBe(1);

    await service.disconnect({ keepItems: true });
    expect(service.items().length).toBe(1);
    expect(service.state()).toBe('idle');

    await service.disconnect();
    expect(service.items()).toEqual([]);
  });
});
