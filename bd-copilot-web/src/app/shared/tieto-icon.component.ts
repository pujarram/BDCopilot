import { Component, Input, OnChanges } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { TIETO_ICONS, TietoIconKey } from './tieto-icons';

@Component({
  selector: 'tieto-icon',
  standalone: true,
  templateUrl: './tieto-icon.html',
  styleUrl: './tieto-icon.css'
})
export class TietoIconComponent implements OnChanges {
  @Input({ required: true }) name!: TietoIconKey;
  svg: SafeHtml = '';

  constructor(private readonly sanitizer: DomSanitizer) {}

  ngOnChanges(): void {
    this.svg = this.sanitizer.bypassSecurityTrustHtml(TIETO_ICONS[this.name]);
  }
}
