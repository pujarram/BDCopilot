import { Injectable, computed, inject, signal } from '@angular/core';
import {
  AccountInfo,
  AuthenticationResult,
  InteractionRequiredAuthError,
  PublicClientApplication,
  RedirectRequest
} from '@azure/msal-browser';
import { Observable, from, firstValueFrom, throwError } from 'rxjs';
import { catchError, map, switchMap, tap } from 'rxjs/operators';
import { ApiService } from './api.service';
import { AdminLoginResponse, AuthConfig } from '../models/api-models';

const STORAGE_KEY = 'bd-copilot-admin-session';
const AUTH_MODE_KEY = 'bd-copilot-auth-mode';

interface StoredSession {
  token: string;
  username: string;
  displayName: string;
  role: string;
  expiresAt: string;
  objectId?: string;
  authMode: 'pilot' | 'entra';
}

/**
 * Dual-mode auth: pilot admin token OR Microsoft Entra SSO (MSAL).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly session = signal<StoredSession | null>(this.readStored());
  private msal: PublicClientApplication | null = null;
  private authConfig = signal<AuthConfig | null>(null);
  private initPromise: Promise<void> | null = null;

  readonly isLoggedIn = computed(() => {
    const s = this.session();
    return !!s && new Date(s.expiresAt).getTime() > Date.now();
  });

  readonly displayName = computed(() => this.session()?.displayName ?? null);
  readonly role = computed(() => this.session()?.role ?? null);
  readonly token = computed(() => this.session()?.token ?? null);
  readonly authMode = computed(() => this.session()?.authMode ?? null);
  readonly objectId = computed(() => this.session()?.objectId ?? null);
  readonly entraEnabled = computed(() => !!this.authConfig()?.entraEnabled);
  readonly allowPilotAdminLogin = computed(() => this.authConfig()?.allowPilotAdminLogin !== false);

  /** Load /api/auth/config and prepare MSAL when Entra is enabled. */
  ensureConfigured(): Observable<AuthConfig> {
    return this.api.getAuthConfig().pipe(
      tap(c => {
        this.authConfig.set(c);
      }),
      switchMap(c => from(this.initMsal(c)).pipe(map(() => c))),
      catchError(err => {
        this.authConfig.set({
          entraEnabled: false,
          enforceAcl: false,
          requireAuthOnApi: false,
          allowPilotAdminLogin: true
        });
        return throwError(() => err);
      })
    );
  }

  login(username: string, password: string): Observable<AdminLoginResponse> {
    return this.api.adminLogin(username, password).pipe(
      tap(res => {
        const stored: StoredSession = {
          token: res.token,
          username: res.username,
          displayName: res.displayName,
          role: res.role,
          expiresAt: res.expiresAt,
          authMode: 'pilot'
        };
        localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
        localStorage.setItem(AUTH_MODE_KEY, 'pilot');
        this.session.set(stored);
        // Lazy import avoided — TeamsService applies identity on next initialize / page load
      })
    );
  }

  async loginWithMicrosoft(): Promise<void> {
    let cfg = this.authConfig();
    if (!cfg) {
      cfg = await firstValueFrom(this.ensureConfigured());
    }
    if (!cfg?.entraEnabled || !this.msal) {
      throw new Error('Microsoft Entra SSO is not configured for this environment.');
    }

    const scopes = this.apiScopes(cfg);
    const request: RedirectRequest = {
      scopes,
      redirectStartPage: window.location.origin + '/dashboard'
    };
    await this.msal.loginRedirect(request);
  }

  /** Call once at app bootstrap to complete MSAL redirect. */
  async handleRedirect(): Promise<boolean> {
    let cfg: AuthConfig | null = null;
    try {
      cfg = await firstValueFrom(this.ensureConfigured());
    } catch {
      return false;
    }
    if (!cfg?.entraEnabled) {
      return false;
    }
    await this.initMsal(cfg);
    if (!this.msal) return false;

    const result = await this.msal.handleRedirectPromise();
    if (result) {
      this.persistEntraResult(result, cfg);
      return true;
    }

    const account = this.msal.getActiveAccount() ?? this.msal.getAllAccounts()[0] ?? null;
    if (account && this.session()?.authMode === 'entra') {
      try {
        const silent = await this.msal.acquireTokenSilent({
          account,
          scopes: this.apiScopes(cfg)
        });
        this.persistEntraResult(silent, cfg);
        return true;
      } catch {
        // leave existing session; interceptor may force re-login
      }
    }
    return false;
  }

  async getAccessToken(): Promise<string | null> {
    const s = this.session();
    if (!s) return null;
    if (s.authMode === 'pilot') return s.token;

    const cfg = this.authConfig();
    if (!this.msal || !cfg) return s.token;

    const account = this.msal.getActiveAccount() ?? this.msal.getAllAccounts()[0];
    if (!account) return s.token;

    try {
      const result = await this.msal.acquireTokenSilent({
        account,
        scopes: this.apiScopes(cfg)
      });
      this.persistEntraResult(result, cfg);
      return result.accessToken;
    } catch (err) {
      if (err instanceof InteractionRequiredAuthError) {
        await this.msal.acquireTokenRedirect({ scopes: this.apiScopes(cfg), account });
      }
      return null;
    }
  }

  logout(): void {
    const mode = this.session()?.authMode;
    localStorage.removeItem(STORAGE_KEY);
    localStorage.removeItem(AUTH_MODE_KEY);
    this.session.set(null);

    if (mode === 'entra' && this.msal) {
      const account = this.msal.getActiveAccount() ?? this.msal.getAllAccounts()[0];
      void this.msal.logoutRedirect({
        account: account ?? undefined,
        postLogoutRedirectUri: window.location.origin + '/login'
      });
      return;
    }
  }

  private apiScopes(cfg: AuthConfig): string[] {
    const scope = cfg.apiScope?.trim();
    return scope ? [scope, 'openid', 'profile'] : ['openid', 'profile'];
  }

  private async initMsal(cfg: AuthConfig): Promise<void> {
    if (!cfg.entraEnabled || !cfg.clientId || !cfg.tenantId) {
      this.msal = null;
      return;
    }
    if (this.msal) return;
    if (this.initPromise) {
      await this.initPromise;
      return;
    }

    this.initPromise = (async () => {
      const authority =
        cfg.authority?.trim() ||
        `https://login.microsoftonline.com/${cfg.tenantId}`;
      const pca = new PublicClientApplication({
        auth: {
          clientId: cfg.clientId!,
          authority,
          redirectUri: window.location.origin + '/login',
          postLogoutRedirectUri: window.location.origin + '/login'
        },
        cache: {
          cacheLocation: 'localStorage'
        }
      });
      await pca.initialize();
      this.msal = pca;
    })();

    await this.initPromise;
  }

  private persistEntraResult(result: AuthenticationResult, cfg: AuthConfig): void {
    const account: AccountInfo = result.account;
    this.msal?.setActiveAccount(account);
    const roles = this.extractRoles(result);
    const expires = result.expiresOn ?? new Date(Date.now() + 60 * 60 * 1000);
    const stored: StoredSession = {
      token: result.accessToken,
      username: account.username,
      displayName: account.name ?? account.username,
      role: roles.includes('BdCopilot.Admin') ? 'Admin' : roles[0] ?? 'User',
      expiresAt: expires.toISOString(),
      objectId: (account.idTokenClaims?.['oid'] as string) ?? account.localAccountId,
      authMode: 'entra'
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
    localStorage.setItem(AUTH_MODE_KEY, 'entra');
    this.session.set(stored);
  }

  private extractRoles(result: AuthenticationResult): string[] {
    const claims = result.idTokenClaims as Record<string, unknown> | undefined;
    const roles = claims?.['roles'];
    if (Array.isArray(roles)) return roles.map(String);
    if (typeof roles === 'string') return [roles];
    return [];
  }

  private readStored(): StoredSession | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const parsed = JSON.parse(raw) as StoredSession;
      if (!parsed.authMode) {
        parsed.authMode = 'pilot';
      }
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
