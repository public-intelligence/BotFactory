import { environment } from '../../environments/environment';

/**
 * Public origin of this Bot Factory deployment — used for share links,
 * slug previews, and iframe embed snippets in the admin UI.
 *
 * Prefer runtime config (`window.__env.productUrl`) or the page's own
 * origin so Docker / ministry deploys show their domain instead of a
 * baked-in build-time host.
 */
export function resolveProductUrl(): string {
  const fromEnv = window.__env?.productUrl?.trim();
  if (fromEnv) return stripTrailingSlash(fromEnv);
  if (typeof location !== 'undefined' && location.origin) {
    return location.origin;
  }
  return stripTrailingSlash(environment.productUrl);
}

function stripTrailingSlash(url: string): string {
  return url.replace(/\/$/, '');
}
