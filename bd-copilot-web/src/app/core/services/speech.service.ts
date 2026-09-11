import { Injectable, signal } from '@angular/core';

interface SpeechRecognitionResultLike {
  isFinal: boolean;
  0: { transcript: string };
}

interface SpeechRecognitionEventLike {
  results: SpeechRecognitionResultLike[];
}

interface SpeechRecognitionLike extends EventTarget {
  continuous: boolean;
  interimResults: boolean;
  lang: string;
  onresult: ((ev: SpeechRecognitionEventLike) => void) | null;
  onerror: ((ev: Event) => void) | null;
  onend: (() => void) | null;
  start(): void;
  stop(): void;
}

declare global {
  interface Window {
    SpeechRecognition?: new () => SpeechRecognitionLike;
    webkitSpeechRecognition?: new () => SpeechRecognitionLike;
  }
}

@Injectable({ providedIn: 'root' })
export class SpeechService {
  readonly listening = signal(false);
  readonly speaking = signal(false);
  readonly supported = signal(false);
  readonly ttsSupported = signal(typeof window !== 'undefined' && 'speechSynthesis' in window);

  private recognition: SpeechRecognitionLike | null = null;

  constructor() {
    const Ctor = window.SpeechRecognition ?? window.webkitSpeechRecognition;
    this.supported.set(!!Ctor);
    if (Ctor) {
      this.recognition = new Ctor();
      this.recognition.continuous = false;
      this.recognition.interimResults = false;
      this.recognition.lang = 'en-US';
    }
  }

  listen(onText: (text: string) => void, onError?: (message: string) => void): void {
    if (!this.recognition) {
      onError?.('Voice input is not supported in this browser. Try Chrome or Edge.');
      return;
    }

    this.stopSpeaking();

    this.recognition.onresult = (event: SpeechRecognitionEventLike) => {
      const last = event.results[event.results.length - 1];
      if (last?.isFinal) {
        onText(last[0].transcript.trim());
      }
    };

    this.recognition.onerror = () => {
      this.listening.set(false);
      onError?.('Voice capture failed. Check microphone permissions.');
    };

    this.recognition.onend = () => this.listening.set(false);

    try {
      this.listening.set(true);
      this.recognition.start();
    } catch {
      this.listening.set(false);
      onError?.('Could not start microphone.');
    }
  }

  stop(): void {
    try {
      this.recognition?.stop();
    } catch {
      // ignore
    }
    this.listening.set(false);
  }

  speak(text: string, delayMs = 400): void {
    if (!this.ttsSupported() || !text.trim()) {
      return;
    }
    window.speechSynthesis.cancel();
    this.speaking.set(false);

    window.setTimeout(() => {
      if (!text.trim()) return;
      const utter = new SpeechSynthesisUtterance(text);
      utter.rate = 1;
      utter.lang = 'en-US';
      utter.onstart = () => this.speaking.set(true);
      utter.onend = () => this.speaking.set(false);
      utter.onerror = () => this.speaking.set(false);
      this.speaking.set(true);
      window.speechSynthesis.speak(utter);
    }, Math.max(0, delayMs));
  }

  stopSpeaking(): void {
    if ('speechSynthesis' in window) {
      window.speechSynthesis.cancel();
    }
    this.speaking.set(false);
  }
}
