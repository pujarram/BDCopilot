import {
  CUSTOM_ELEMENTS_SCHEMA,
  Component,
  ElementRef,
  ViewChild,
  effect,
  inject,
  signal
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { CopilotChatService } from '../../core/services/copilot-chat.service';
import { CopilotPanelStateService } from '../../core/services/copilot-panel-state.service';
import { SpeechService } from '../../core/services/speech.service';
import { TeamsService } from '../../core/services/teams.service';
import { Citation } from '../../core/models/api-models';
import { AiStreamText } from '../../shared/ai-stream-text.component';
import { COPILOT_QUICK_CHIPS } from '../../shared/copilot-quick-chips';

@Component({
  selector: 'app-copilot-panel',
  imports: [RouterLink, AiStreamText],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  templateUrl: './copilot-panel.html',
  styleUrl: './copilot-panel.scss'
})
export class CopilotPanel {
  private readonly panelState = inject(CopilotPanelStateService);
  private readonly chat = inject(CopilotChatService);
  private readonly speech = inject(SpeechService);
  private readonly teams = inject(TeamsService);

  @ViewChild('logEl') logEl?: ElementRef<HTMLDivElement>;

  protected readonly draft = signal('');
  protected readonly voiceError = signal<string | null>(null);
  protected readonly prompts = COPILOT_QUICK_CHIPS;

  protected readonly panelOpen = this.panelState.open;
  protected readonly readAloud = this.panelState.readAloud;
  protected readonly messages = this.chat.messages;
  protected readonly asking = this.chat.asking;
  protected readonly errorMessage = this.chat.errorMessage;
  protected readonly voiceSupported = this.speech.supported;
  protected readonly listening = this.speech.listening;
  protected readonly speaking = this.speech.speaking;

  private lastAssistantText = '';

  constructor() {
    effect(() => {
      const asking = this.chat.asking();
      const msgs = this.chat.messages();
      const last = msgs.length > 0 ? msgs[msgs.length - 1] : null;
      if (
        !asking
        && last?.role === 'assistant'
        && this.panelState.readAloud()
        && last.text !== this.lastAssistantText
      ) {
        this.lastAssistantText = last.text;
        this.speech.speak(last.text, 500);
      }
      queueMicrotask(() => this.scrollToBottom());
    });
  }

  protected useChip(prompt: string): void {
    this.draft.set('');
    this.speech.stopSpeaking();
    this.chat.send(prompt);
  }

  protected close(): void {
    this.speech.stop();
    this.speech.stopSpeaking();
    this.panelState.close();
  }

  protected toggleReadAloud(): void {
    const next = !this.panelState.readAloud();
    this.panelState.setReadAloud(next);
    if (!next) {
      this.speech.stopSpeaking();
    }
  }

  protected citationUrl(c: Citation): string | null {
    return this.chat.citationUrl(c);
  }

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLInputElement).value ?? '');
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') this.send();
  }

  protected send(): void {
    const text = this.draft().trim();
    if (!text) return;
    this.draft.set('');
    this.voiceError.set(null);
    this.speech.stopSpeaking();
    this.chat.send(text, this.teams.user().runningInTeams);
  }

  protected startVoice(): void {
    this.voiceError.set(null);
    if (this.listening()) {
      this.speech.stop();
      return;
    }
    this.speech.listen(
      text => {
        this.draft.set(text);
        this.send();
      },
      msg => this.voiceError.set(msg ?? 'Voice input failed.')
    );
  }

  protected clearChat(): void {
    this.chat.clear();
    this.lastAssistantText = '';
    this.speech.stopSpeaking();
  }

  private scrollToBottom(): void {
    const el = this.logEl?.nativeElement;
    if (el) el.scrollTop = el.scrollHeight;
  }
}
