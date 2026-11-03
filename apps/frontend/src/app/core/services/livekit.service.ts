import { Injectable, signal } from '@angular/core';
import { DisconnectReason, Room, RoomEvent, Track } from 'livekit-client';
import type {
  LocalAudioTrack,
  RemoteAudioTrack,
  RemoteTrack,
} from 'livekit-client';

export type ConnectionState =
  | 'disconnected'
  | 'connecting'
  | 'connected'
  | 'reconnecting';

export interface SessionEndInfo {
  source: 'close' | 'disconnect';
  reason?: string;
  error?: string;
  errorCode?: string;
  provider?: string;
  stage?: string;
  userMessage?: string;
  operatorMessage?: string;
}

export interface PipelineErrorInfo {
  errorCode?: string;
  provider?: string;
  stage?: string;
  userMessage?: string;
  operatorMessage?: string;
  rawError?: string;
}

export interface SessionPausedInfo {
  reason?: string;
}

function isAgentIdentity(identity: string): boolean {
  return identity.startsWith('agent') || identity.startsWith('bot-factory');
}

function parseProviderFields(
  data: Record<string, unknown>,
): Pick<
  PipelineErrorInfo,
  'errorCode' | 'provider' | 'stage' | 'userMessage' | 'operatorMessage' | 'rawError'
> {
  return {
    errorCode: data['errorCode'] as string | undefined,
    provider: data['provider'] as string | undefined,
    stage: data['stage'] as string | undefined,
    userMessage: data['userMessage'] as string | undefined,
    operatorMessage: data['operatorMessage'] as string | undefined,
    rawError: data['error'] as string | undefined,
  };
}

function sessionEndFromCloseData(
  data: Record<string, unknown>,
): SessionEndInfo {
  const provider = parseProviderFields(data);
  return {
    source: 'close',
    reason: data['reason'] as string | undefined,
    error: provider.rawError,
    ...provider,
  };
}

/**
 * Thin audio bridge to LiveKit. Conversation content (transcripts, messages,
 * telemetry events) is sourced from the DB via the SignalR ConversationHub —
 * see ConversationStreamService — so this service deliberately ignores the
 * data-channel `agent` topic for content; session lifecycle signals
 * (SessionPaused, Close) still come from the data channel for low-latency UI.
 */
@Injectable({ providedIn: 'root' })
export class LiveKitService {
  readonly connectionState = signal<ConnectionState>('disconnected');
  readonly isAgentConnected = signal(false);
  readonly agentEverConnected = signal(false);
  readonly agentState = signal('');
  readonly isMicrophoneEnabled = signal(false);
  readonly agentAudioTrack = signal<RemoteAudioTrack | null>(null);
  readonly localAudioTrack = signal<LocalAudioTrack | null>(null);
  readonly sessionEnd = signal<SessionEndInfo | null>(null);
  readonly sessionPaused = signal<SessionPausedInfo | null>(null);
  readonly pipelineError = signal<PipelineErrorInfo | null>(null);

  private room: Room | null = null;

  clearSessionEnd(): void {
    this.sessionEnd.set(null);
  }

  clearPipelineError(): void {
    this.pipelineError.set(null);
  }

  clearSessionPaused(): void {
    this.sessionPaused.set(null);
  }

  async connect(token: string, url: string): Promise<void> {
    this.connectionState.set('connecting');
    this.agentState.set('');
    this.isAgentConnected.set(false);
    this.agentEverConnected.set(false);
    this.isMicrophoneEnabled.set(false);
    this.agentAudioTrack.set(null);
    this.localAudioTrack.set(null);
    this.sessionEnd.set(null);
    this.sessionPaused.set(null);
    this.pipelineError.set(null);

    this.room = new Room();

    this.room.on(RoomEvent.TrackSubscribed, (track: RemoteTrack) => {
      if (track.kind === Track.Kind.Audio) {
        const el = track.attach();
        el.id = 'agent-audio';
        document.body.appendChild(el);
        this.agentAudioTrack.set(track as RemoteAudioTrack);
        // Agent audio is enough proof the worker is in the room, including when
        // ParticipantConnected never fires because the agent joined first.
        this.markAgentPresent();
      }
    });

    this.room.on(RoomEvent.TrackUnsubscribed, (track: RemoteTrack) => {
      track.detach().forEach((el) => el.remove());
      if (track.kind === Track.Kind.Audio) {
        this.agentAudioTrack.set(null);
      }
    });

    this.room.on(RoomEvent.ParticipantConnected, (p) => {
      if (isAgentIdentity(p.identity)) {
        this.markAgentPresent();
      }
    });

    this.room.on(RoomEvent.ParticipantDisconnected, (p) => {
      if (isAgentIdentity(p.identity)) {
        this.isAgentConnected.set(false);
      }
    });

    this.room.on(RoomEvent.DataReceived, (data: Uint8Array, _participant, _kind, topic) => {
      if (topic !== 'agent') return;
      try {
        const msg = JSON.parse(new TextDecoder().decode(data));

        if (msg.eventType === 'SessionPaused' && msg.data) {
          this.sessionPaused.set({ reason: msg.data['reason'] as string | undefined });
          return;
        }
        if (msg.eventType === 'Error' && msg.data) {
          const fields = parseProviderFields(msg.data as Record<string, unknown>);
          this.pipelineError.set(fields);
          return;
        }
        if (msg.eventType === 'Close' && msg.data) {
          this.sessionEnd.set(
            sessionEndFromCloseData(msg.data as Record<string, unknown>),
          );
          return;
        }
        if (msg.eventType === 'ConversationStarted') {
          this.markAgentPresent();
        }
        if (msg.eventType === 'AgentStateChanged' && msg.data) {
          this.agentState.set(msg.data.newState ?? '');
          // ConversationStarted may have already been emitted before the browser
          // joined a pre-warmed room; any later agent state still means present.
          this.markAgentPresent();
        }
      } catch {
        // ignore malformed data
      }
    });

    this.room.on(RoomEvent.Reconnecting, () => {
      this.connectionState.set('reconnecting');
    });

    this.room.on(RoomEvent.Reconnected, () => {
      this.connectionState.set('connected');
    });

    this.room.on(RoomEvent.Disconnected, (reason?: DisconnectReason) => {
      this.sessionEnd.set({
        source: 'disconnect',
        reason: reason !== undefined ? DisconnectReason[reason] : undefined,
      });
      this.connectionState.set('disconnected');
      this.isAgentConnected.set(false);
      this.isMicrophoneEnabled.set(false);
      this.agentAudioTrack.set(null);
      this.localAudioTrack.set(null);
    });

    this.room.on(RoomEvent.LocalTrackPublished, (publication) => {
      if (!this.room) return;
      this.isMicrophoneEnabled.set(
        this.room.localParticipant.isMicrophoneEnabled,
      );
      if (publication.kind === Track.Kind.Audio && publication.audioTrack) {
        this.localAudioTrack.set(publication.audioTrack as LocalAudioTrack);
      }
    });

    this.room.on(RoomEvent.LocalTrackUnpublished, (publication) => {
      if (!this.room) return;
      this.isMicrophoneEnabled.set(
        this.room.localParticipant.isMicrophoneEnabled,
      );
      if (publication.kind === Track.Kind.Audio) {
        this.localAudioTrack.set(null);
      }
    });

    this.room.on(RoomEvent.TrackMuted, (publication) => {
      if (!this.room) return;
      if (publication.kind === Track.Kind.Audio && publication.isLocal) {
        this.isMicrophoneEnabled.set(
          this.room.localParticipant.isMicrophoneEnabled,
        );
      }
    });

    this.room.on(RoomEvent.TrackUnmuted, (publication) => {
      if (!this.room) return;
      if (publication.kind === Track.Kind.Audio && publication.isLocal) {
        this.isMicrophoneEnabled.set(
          this.room.localParticipant.isMicrophoneEnabled,
        );
      }
    });

    await this.room.connect(url, token);
    // Agent jobs often join the room while CreateChat is still returning a token.
    // Participants already present do not emit ParticipantConnected for late joiners.
    this.syncAgentPresenceFromRoom();
    this.connectionState.set('connected');
  }

  private markAgentPresent(): void {
    this.isAgentConnected.set(true);
    this.agentEverConnected.set(true);
  }

  private syncAgentPresenceFromRoom(): void {
    if (!this.room) return;
    for (const participant of this.room.remoteParticipants.values()) {
      if (isAgentIdentity(participant.identity)) {
        this.markAgentPresent();
        return;
      }
    }
  }

  async enableMicrophone(): Promise<void> {
    if (this.room) {
      await this.room.localParticipant.setMicrophoneEnabled(true);
      this.isMicrophoneEnabled.set(
        this.room.localParticipant.isMicrophoneEnabled,
      );
    }
  }

  async setMicrophoneEnabled(enabled: boolean): Promise<void> {
    if (!this.room) return;
    await this.room.localParticipant.setMicrophoneEnabled(enabled);
    this.isMicrophoneEnabled.set(
      this.room.localParticipant.isMicrophoneEnabled,
    );
  }

  async toggleMicrophone(): Promise<void> {
    await this.setMicrophoneEnabled(!this.isMicrophoneEnabled());
  }

  disconnect(): void {
    if (this.room) {
      this.room.disconnect();
      this.room = null;
    }
    this.connectionState.set('disconnected');
    this.isAgentConnected.set(false);
    this.agentEverConnected.set(false);
    this.isMicrophoneEnabled.set(false);
    this.agentAudioTrack.set(null);
    this.localAudioTrack.set(null);
    document.getElementById('agent-audio')?.remove();
  }
}
