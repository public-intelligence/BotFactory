/** Anonymous visitors on public bots use /api/public + in-memory token. */
export function shouldUsePublicSession(
  isPublicBot: boolean,
  isAuthenticated: boolean,
): boolean {
  return isPublicBot && !isAuthenticated;
}
