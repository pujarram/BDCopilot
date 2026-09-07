import { CUSTOM_ELEMENTS_SCHEMA, Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';
import { PartnerOnboardingProfile } from '../../core/models/api-models';

@Component({
  selector: 'app-admin-onboard',
  templateUrl: './admin-onboard.html',
  imports: [RouterLink],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class AdminOnboard implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  protected readonly step = signal(1);
  protected readonly profile = signal<PartnerOnboardingProfile | null>(null);
  protected readonly saving = signal(false);
  protected readonly manifestBusy = signal(false);

  ngOnInit(): void {
    this.api.getOnboardingProfile().subscribe({
      next: p => this.profile.set(p),
      error: err => {
        console.error(err);
        this.toast.show('Could not load onboarding profile.');
      }
    });
  }

  protected next(): void {
    this.step.update(s => Math.min(4, s + 1));
  }

  protected back(): void {
    this.step.update(s => Math.max(1, s - 1));
  }

  protected save(): void {
    const p = this.profile();
    if (!p) return;
    this.saving.set(true);
    this.api.saveOnboardingProfile(p).subscribe({
      next: saved => {
        this.profile.set(saved);
        this.saving.set(false);
        this.toast.show('Profile saved (merge view — persist via Key Vault in production).');
        this.next();
      },
      error: err => {
        console.error(err);
        this.saving.set(false);
        this.toast.show('Save failed.');
      }
    });
  }

  protected downloadManifest(): void {
    this.manifestBusy.set(true);
    this.api.downloadTeamsManifest().subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'bd-copilot-manifest.json';
        a.click();
        URL.revokeObjectURL(url);
        this.manifestBusy.set(false);
        this.toast.show('Teams manifest downloaded — add color.png / outline.png and sideload or publish.');
      },
      error: err => {
        console.error(err);
        this.manifestBusy.set(false);
        this.toast.show('Manifest download failed.');
      }
    });
  }

  protected patch(field: keyof PartnerOnboardingProfile, value: string): void {
    this.profile.update(p => (p ? { ...p, [field]: value } : p));
  }

  protected patchGroupIds(raw: string): void {
    const ids = raw.split(/[\n,;]+/).map(s => s.trim()).filter(Boolean);
    this.profile.update(p => (p ? { ...p, plannerGroupIds: ids } : p));
  }
}
