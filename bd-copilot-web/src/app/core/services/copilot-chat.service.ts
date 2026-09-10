import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { TeamsService } from './teams.service';
import { ChatTurn, Citation } from '../models/api-models';

export interface CopilotMessage {
  role: 'user' | 'assistant';
  text: string;
  citations?: Citation[];
}

@Injectable({ providedIn: 'root' })
export class CopilotChatService {
  private readonly api = inject(ApiService);
  private readonly teams = inject(TeamsService);

  readonly messages = signal<CopilotMessage[]>([]);
  readonly asking = signal(false);
  readonly errorMessage = signal<string | null>(null);

  citationUrl(c: Citation): string | null {
    return this.api.resolveCitationOpenUrl(c, this.teams.user().objectId);
  }

  send(text: string, includeChannelLive = false): void {
    const trimmed = text.trim();
    if (!trimmed || this.asking()) return;

    const history: ChatTurn[] = this.messages().map(m => ({
      role: m.role,
      content: m.text
    }));

    this.messages.update(list => [...list, { role: 'user', text: trimmed }]);
    this.asking.set(true);
    this.errorMessage.set(null);

    this.api.chat({
      message: trimmed,
      history,
      userObjectId: this.teams.user().objectId,
      includeChannelLiveSearch: includeChannelLive
    }).subscribe({
      next: response => {
        this.messages.update(list => [
          ...list,
          { role: 'assistant', text: response.answer, citations: response.citations }
        ]);
        this.asking.set(false);
      },
      error: err => {
        const status = err?.status;
        const detail =
          err?.error?.detail || err?.error?.title || err?.error?.message || err?.message;
        if (status === 0 || status == null) {
          this.errorMessage.set(
            'Could not reach BD Copilot API. Confirm the API is running and Ollama is up.'
          );
        } else if (status === 503) {
          this.errorMessage.set(detail || 'AI provider unavailable.');
        } else {
          this.errorMessage.set(`Chat failed (HTTP ${status})${detail ? `: ${detail}` : ''}.`);
        }
        this.asking.set(false);
      }
    });
  }

  clear(): void {
    this.messages.set([]);
    this.errorMessage.set(null);
  }
}
