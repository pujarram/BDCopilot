import { CUSTOM_ELEMENTS_SCHEMA, Component, ElementRef, ViewChild, effect, inject, signal } from '@angular/core';
import { Citation } from '../../core/models/api-models';
import { CopilotChatService } from '../../core/services/copilot-chat.service';
import { CopilotPanelStateService } from '../../core/services/copilot-panel-state.service';
import { SpeechService } from '../../core/services/speech.service';
import { TeamsService } from '../../core/services/teams.service';
import { AiStreamText } from '../../shared/ai-stream-text.component';
import { COPILOT_QUICK_CHIPS } from '../../shared/copilot-quick-chips';

@Component({
  selector: 'app-chat',
  templateUrl: './chat.html',
  imports: [AiStreamText],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class Chat {
  private readonly chat = inject(CopilotChatService);
  private readonly speech = inject(SpeechService);
  private readonly panelState = inject(CopilotPanelStateService);
  private readonly teams = inject(TeamsService);

  @ViewChild('logEl') logEl?: ElementRef<HTMLDivElement>;

  protected readonly prompts = COPILOT_QUICK_CHIPS;
  protected readonly messages = this.chat.messages;
  protected readonly asking = this.chat.asking;
  protected readonly errorMessage = this.chat.errorMessage;
  protected readonly draft = signal('');
  protected readonly voiceError = signal<string | null>(null);
  protected readonly animateIndex = signal(-1);
  protected readonly readAloud = this.panelState.readAloud;
  protected readonly voiceSupported = this.speech.supported;
  protected readonly listening = this.speech.listening;
  protected readonly speaking = this.speech.speaking;

  private trackedMessageCount = 0;
  private lastAssistantText = '';

  constructor() {
    effect(() => {
      const msgs = this.messages();
      if (msgs.length > this.trackedMessageCount) {
        const last = msgs[msgs.length - 1];
        if (last?.role === 'assistant') {
          this.animateIndex.set(msgs.length - 1);
        }
      }
      this.trackedMessageCount = msgs.length;
      queueMicrotask(() => this.scrollToBottom());
    });

    effect(() => {
      const asking = this.chat.asking();
      const msgs = this.messages();
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
    });
  }

  protected usePrompt(prompt: string): void {
    this.draft.set('');
    this.speech.stopSpeaking();
    this.chat.send(prompt, this.teams.user().runningInTeams);
  }

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLInputElement).value ?? '');
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') this.ask();
  }

  protected citationUrl(c: Citation): string | null {
    return this.chat.citationUrl(c);
  }

  protected shouldAnimate(index: number): boolean {
    return this.animateIndex() === index;
  }

  protected ask(): void {
    const text = this.draft().trim();
    if (!text || this.asking()) return;
    this.draft.set('');
    this.voiceError.set(null);
    this.speech.stopSpeaking();
    this.chat.send(text, this.teams.user().runningInTeams);
  }

  protected toggleReadAloud(): void {
    const next = !this.panelState.readAloud();
    this.panelState.setReadAloud(next);
    if (!next) this.speech.stopSpeaking();
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
        this.ask();
      },
      msg => this.voiceError.set(msg ?? 'Voice input failed.')
    );
  }

  private scrollToBottom(): void {
    const el = this.logEl?.nativeElement;
    if (el) el.scrollTop = el.scrollHeight;
  }
}
