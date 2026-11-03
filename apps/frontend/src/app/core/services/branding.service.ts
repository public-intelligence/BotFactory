import { Injectable, inject, signal, computed } from '@angular/core';
import { Api } from '../../api/api';
import {
  getOrgBranding,
  uploadOrgLogo,
  uploadOrgFavicon,
  deleteOrgLogo,
  deleteOrgFavicon,
} from '../../api/functions';
import type { OrgBrandingResponse } from '../../api/models/org-branding-response';

export const DEFAULT_LOGO_URL = 'cupula-gob-azul.webp';

const DEFAULT_FAVICON_LINKS: { rel: string; href: string; type?: string; sizes?: string }[] = [
  { rel: 'icon', type: 'image/x-icon', href: 'favicon.ico' },
  { rel: 'icon', type: 'image/png', sizes: '32x32', href: 'favicon-32.png' },
  { rel: 'icon', type: 'image/png', sizes: '16x16', href: 'favicon-16.png' },
  { rel: 'apple-touch-icon', href: 'apple-touch-icon.png' },
];

@Injectable({ providedIn: 'root' })
export class BrandingService {
  private readonly api = inject(Api);

  readonly meta = signal<OrgBrandingResponse | null>(null);
  private readonly _logoUrl = signal<string | null>(null);
  private readonly _faviconUrl = signal<string | null>(null);

  readonly logoUrl = computed(() => this._logoUrl() ?? DEFAULT_LOGO_URL);
  readonly faviconUrl = computed(() => this._faviconUrl());
  readonly hasLogo = computed(() => this.meta()?.hasLogo ?? false);
  readonly hasFavicon = computed(() => this.meta()?.hasFavicon ?? false);
  readonly logoFilename = computed(() => this.meta()?.logoFilename ?? null);
  readonly faviconFilename = computed(() => this.meta()?.faviconFilename ?? null);

  /** Load branding for the authenticated admin shell. */
  async loadForAdmin(): Promise<void> {
    try {
      const branding = await this.api.invoke(getOrgBranding);
      this.meta.set(branding);

      if (branding.hasLogo) {
        this._logoUrl.set(this.logoDownloadUrl(branding.logoVersion));
      } else {
        this._logoUrl.set(null);
      }

      if (branding.hasFavicon) {
        this.applyCustomFavicon(this.faviconDownloadUrl(branding.faviconVersion));
      } else {
        this.resetFavicon();
      }
    } catch {
      this.meta.set(null);
      this._logoUrl.set(null);
      this.resetFavicon();
    }
  }

  /** Apply org favicon on public chat/embed pages (by bot slug). */
  async loadFaviconForChat(slug: string): Promise<void> {
    if (!slug) return;
    const url = `${this.api.rootUrl}/api/bots/by-slug/${encodeURIComponent(slug)}/branding/favicon/download`;
    const ok = await this.probeImage(url);
    if (ok) this.applyCustomFavicon(url);
    else this.resetFavicon();
  }

  async uploadLogo(file: File): Promise<void> {
    const branding = await this.api.invoke(uploadOrgLogo, { body: { file } });
    this.meta.set(branding);
    this._logoUrl.set(this.logoDownloadUrl(branding.logoVersion));
  }

  async uploadFavicon(file: File): Promise<void> {
    const branding = await this.api.invoke(uploadOrgFavicon, { body: { file } });
    this.meta.set(branding);
    this.applyCustomFavicon(this.faviconDownloadUrl(branding.faviconVersion));
  }

  async removeLogo(): Promise<void> {
    await this.api.invoke(deleteOrgLogo);
    const branding = await this.api.invoke(getOrgBranding);
    this.meta.set(branding);
    this._logoUrl.set(null);
  }

  async removeFavicon(): Promise<void> {
    await this.api.invoke(deleteOrgFavicon);
    const branding = await this.api.invoke(getOrgBranding);
    this.meta.set(branding);
    this.resetFavicon();
  }

  /** Clear in-memory branding and restore default favicon (e.g. on logout). */
  clear(): void {
    this.meta.set(null);
    this._logoUrl.set(null);
    this.resetFavicon();
  }

  // The version changes on every upload, which is what lets the API mark these
  // immutable; the filename does not (re-uploading logo.png yields the same
  // string). Date.now() as a last resort keeps a versionless asset from being
  // held across an upload within the same session.
  private logoDownloadUrl(version: string | null | undefined): string {
    const v = encodeURIComponent(version || String(Date.now()));
    return `${this.api.rootUrl}/api/settings/branding/logo/download?v=${v}`;
  }

  private faviconDownloadUrl(version: string | null | undefined): string {
    const v = encodeURIComponent(version || String(Date.now()));
    return `${this.api.rootUrl}/api/settings/branding/favicon/download?v=${v}`;
  }

  private probeImage(url: string): Promise<boolean> {
    return new Promise((resolve) => {
      const img = new Image();
      img.onload = () => resolve(true);
      img.onerror = () => resolve(false);
      img.src = url;
    });
  }

  private applyCustomFavicon(url: string): void {
    this._faviconUrl.set(url);
    this.clearFaviconLinks();
    const link = document.createElement('link');
    link.rel = 'icon';
    link.href = url;
    document.head.appendChild(link);

    const apple = document.createElement('link');
    apple.rel = 'apple-touch-icon';
    apple.href = url;
    document.head.appendChild(apple);
  }

  private resetFavicon(): void {
    this._faviconUrl.set(null);
    this.clearFaviconLinks();
    for (const def of DEFAULT_FAVICON_LINKS) {
      const link = document.createElement('link');
      link.rel = def.rel;
      link.href = def.href;
      if (def.type) link.type = def.type;
      if (def.sizes) link.setAttribute('sizes', def.sizes);
      document.head.appendChild(link);
    }
  }

  private clearFaviconLinks(): void {
    document
      .querySelectorAll('link[rel="icon"], link[rel="apple-touch-icon"], link[rel="shortcut icon"]')
      .forEach((el) => el.remove());
  }
}
