import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { AdminLoginResponse } from '../models/api-models';
import { tap } from 'rxjs/operators';
import { Observable } from 'rxjs';

const STORAGE_KEY = 'bd-copilot-admin-session';

interface StoredSession {
  token: string;
  username: string;
  displayName: string;
  role: string;
  expiresAt: string;
}

/**
 * Temporary admin session until Entra user management is ready.
 * Credentials live in backend AdminAuth config (default admin / admin123).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly session = signal<StoredSession | null>(this.readStored());

  readonly isLoggedIn = computed(() => {
    const s = this.session();
    return !!s && new Date(s.expiresAt).getTime() > Date.now();
  });

  readonly displayName = computed(() => this.session()?.displayName ?? null);
  readonly role = computed(() => this.session()?.role ?? null);
  readonly token = computed(() => this.session()?.token ?? null);

  login(username: string, password: string): Observable<AdminLoginResponse> {
    return this.api.adminLogin(username, password).pipe(
      tap(res => {
        const stored: StoredSession = {
          token: res.token,
          username: res.username,
          displayName: res.displayName,
          role: res.role,
          expiresAt: res.expiresAt
        };
        localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
        this.session.set(stored);
      })
    );
  }

  logout(): void {
    localStorage.removeItem(STORAGE_KEY);
    this.session.set(null);
  }

  private readStored(): StoredSession | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const parsed = JSON.parse(raw) as StoredSession;
      if (new Date(parsed.expiresAt).getTime() <= Date.now()) {
        localStorage.removeItem(STORAGE_KEY);
        return null;
      }
      return parsed;
    } catch {
      return null;
    }
  }
}
