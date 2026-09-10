import { CUSTOM_ELEMENTS_SCHEMA, Component, ElementRef, ViewChild, inject, signal } from '@angular/core';
import { ApiService } from '../../core/services/api.service';
import { TeamsService } from '../../core/services/teams.service';
import { ChatTurn, Citation } from '../../core/models/api-models';
import { AiStreamText } from '../../shared/ai-stream-text.component';
import { COPILOT_QUICK_CHIPS } from '../../shared/copilot-quick-chips';

interface DisplayMessage {
  role: 'user' | 'assistant';
  text: string;
  citations?: Citation[];
  /** Animate typewriter on first reveal (assistant only). */
  animate?: boolean;
}

@Component({
  selector: 'app-chat',
  templateUrl: './chat.html',
  imports: [AiStreamText],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class Chat {
  private readonly api = inject(ApiService);
  private readonly teams = inject(TeamsService);

  @ViewChild('logEl') logEl?: ElementRef<HTMLDivElement>;

  protected readonly prompts = COPILOT_QUICK_CHIPS;
  protected readonly messages = signal<DisplayMessage[]>([]);
  protected readonly draft = signal('');
  protected readonly asking = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected usePrompt(prompt: string): void {
    this.draft.set(prompt);
    this.ask();
  }

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLInputElement).value ?? '');
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') this.ask();
  }

  protected citationUrl(c: Citation): string | null {
    return this.api.resolveCitationOpenUrl(c, this.teams.user().objectId);
  }

  protected ask(): void {
    const text = this.draft().trim();
    if (!text || this.asking()) return;

    const history: ChatTurn[] = this.messages().map(m => ({
      role: m.role === 'user' ? 'user' : 'assistant',
      content: m.text
    }));

    this.messages.update(list => [
      ...list.map(m => (m.role === 'assistant' ? { ...m, animate: false } : m)),
      { role: 'user', text }
    ]);
    this.draft.set('');
    this.asking.set(true);
    this.errorMessage.set(null);
    this.scrollToBottom();

    this.api.chat({ message: text, history, userObjectId: this.teams.user().objectId }).subscribe({
      next: response => {
        this.messages.update(list => [
          ...list,
          { role: 'assistant', text: response.answer, citations: response.citations, animate: true }
        ]);
        this.asking.set(false);
        this.scrollToBottom();
      },
      error: err => {
        console.error(err);
        this.errorMessage.set('Chat failed — is the API running?');
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
