import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';

export interface SeoMetadata {
  title: string;
  description: string;
  image?: string;
  jsonLd?: Record<string, unknown>;
}

@Injectable({
  providedIn: 'root'
})
export class SeoService {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly document = inject(DOCUMENT);

  setMetadata(metadata: SeoMetadata): void {
    this.title.setTitle(metadata.title);
    this.meta.updateTag({ name: 'description', content: metadata.description });
    this.meta.updateTag({ property: 'og:title', content: metadata.title });
    this.meta.updateTag({ property: 'og:description', content: metadata.description });
    this.meta.updateTag({ property: 'og:type', content: 'website' });
    this.meta.updateTag({ property: 'og:image', content: metadata.image ?? '/images/og-default.png' });

    this.updateJsonLd(metadata.jsonLd);
  }

  private updateJsonLd(jsonLd?: Record<string, unknown>): void {
    this.document.querySelector('script[data-seo-json-ld="true"]')?.remove();

    if (!jsonLd) {
      return;
    }

    const script = this.document.createElement('script');
    script.type = 'application/ld+json';
    script.dataset['seoJsonLd'] = 'true';
    script.text = JSON.stringify(jsonLd);
    this.document.head.appendChild(script);
  }
}
