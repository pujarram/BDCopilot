import { CUSTOM_ELEMENTS_SCHEMA, Component, ElementRef, ViewChild, inject, signal } from '@angular/core';
import { ApiService } from '../../core/services/api.service';
import { TeamsService } from '../../core/services/teams.service';
import { ChatTurn, Citation } from '../../core/models/api-models';

interface DisplayMessage {
  role: 'user' | 'assistant';
  text: string;
  citations?: Citation[];
}

const SUGGESTED_PROMPTS = [
  { key: 'rfp', text: 'Create RFP response for Wealth Management Platform based on previous proposals' },
  { key: 'demo', text: 'Find all demo assets related to Credit Risk and summarize' },
  { key: 'knowledge', text: 'Show all references where Wealth Copilot was discussed' },
  { key: 'meeting', text: 'Prepare meeting notes for customer ABC Bank' }
];

@Component({
  selector: 'app-chat',
  templateUrl: './chat.html',
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class Chat {
  private readonly api = inject(ApiService);
  private readonly teams = inject(TeamsService);

  @ViewChild('logEl') logEl?: ElementRef<HTMLDivElement>;

  protected readonly prompts = SUGGESTED_PROMPTS;
  protected readonly messages = signal<DisplayMessage[]>([]);
  protected readonly draft = signal('');
  protected readonly asking = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected usePrompt(text: string): void {
    this.draft.set(text);
    this.ask();
  }

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLInputElement).value ?? '');
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') this.ask();
  }

  protected ask(): void {
    const text = this.draft().trim();
    if (!text || this.asking()) return;

    const history: ChatTurn[] = this.messages().map(m => ({
      role: m.role === 'user' ? 'user' : 'assistant',
      content: m.text
    }));

    this.messages.update(list => [...list, { role: 'user', text }]);
    this.draft.set('');
    this.asking.set(true);
    this.errorMessage.set(null);
    this.scrollToBottom();

    this.api.chat({ message: text, history, userObjectId: this.teams.user().objectId }).subscribe({
      next: response => {
        this.messages.update(list => [...list, { role: 'assistant', text: response.answer, citations: response.citations }]);
        this.asking.set(false);
        this.scrollToBottom();
      },
      error: err => {
        const status = err?.status;
        const detail =
          err?.error?.detail || err?.error?.title || err?.error?.message || err?.message;
        if (status === 0 || status == null) {
          this.errorMessage.set(
            'Could not reach BD Copilot API (timeout or offline). Confirm BDCopilot.Api is running at http://localhost:5154 and Ollama is up (llama3.1:8b).'
          );
        } else if (status === 503) {
          this.errorMessage.set(
            detail ||
              'AI provider unavailable. Confirm Ollama is running and llama3.1:8b is pulled.'
          );
        } else {
          this.errorMessage.set(`Chat failed (HTTP ${status})${detail ? `: ${detail}` : ''}.`);
        }
        console.error(err);
        this.asking.set(false);
      }
    });
  }

  private scrollToBottom(): void {
    queueMicrotask(() => {
      const el = this.logEl?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    });
  }
}
