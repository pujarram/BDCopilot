import {
  Component,
  OnDestroy,
  effect,
  input,
  signal,
  untracked
} from '@angular/core';

/**
 * Reveals AI text with a blinking caret (same feel as generator stream cards).
 * Pass `animate=false` to show full text immediately (e.g. prior chat turns).
 */
@Component({
  selector: 'app-ai-stream-text',
  template: `<span class="ai-stream-text">{{ visible() }}@if (showCaret()) {
    <span class="gen-caret" aria-hidden="true"></span>
  }</span>`,
  styles: `
    :host { display: inline; }
    .ai-stream-text { white-space: pre-wrap; }
  `
})
export class AiStreamText implements OnDestroy {
  readonly text = input.required<string>();
  /** When false, text appears at once (no typewriter). */
  readonly animate = input(true);
  /** Characters revealed per tick. */
  readonly charsPerTick = input(4);
  readonly tickMs = input(14);

  protected readonly visible = signal('');
  protected readonly showCaret = signal(false);

  private timer: ReturnType<typeof setInterval> | null = null;
  private target = '';

  constructor() {
    effect(() => {
      const next = this.text() ?? '';
      const shouldAnimate = this.animate();
      untracked(() => this.startReveal(next, shouldAnimate));
    });
  }

  ngOnDestroy(): void {
    this.clearTimer();
  }

  private startReveal(next: string, shouldAnimate: boolean): void {
    this.clearTimer();
    this.target = next;

    if (!shouldAnimate || !next) {
      this.visible.set(next);
      this.showCaret.set(false);
      return;
    }

    const current = this.visible();
    if (!next.startsWith(current)) {
      this.visible.set('');
    }

    this.showCaret.set(true);
    const step = Math.max(1, this.charsPerTick());
    this.timer = setInterval(() => {
      const shown = this.visible();
      if (shown.length >= this.target.length) {
        this.visible.set(this.target);
        this.showCaret.set(false);
        this.clearTimer();
        return;
      }
      this.visible.set(this.target.slice(0, shown.length + step));
    }, Math.max(8, this.tickMs()));
  }

  private clearTimer(): void {
    if (this.timer != null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
