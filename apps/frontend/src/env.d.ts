declare global {
  interface Window {
    __env?: { apiUrl?: string; hubsUrl?: string; productUrl?: string };
  }
}

export {};
