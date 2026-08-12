// Wrapper TS del plugin nativo Kotlin FuxionCapturePlugin. Solo funciona en Android nativo.
// En web (npm run dev) devuelve error informativo: la captura auto requiere el WebView del OS.

import { Capacitor, registerPlugin } from '@capacitor/core';

interface FuxionCapturePlugin {
  openAndCapture(opts: {
    url: string;
    localStorageKey?: string;
  }): Promise<{ token: string; url: string }>;
}

// Web fallback: solo lanza un error legible cuando alguien intenta usarlo en dev browser.
const webShim: FuxionCapturePlugin = {
  openAndCapture: () => Promise.reject(new Error(
    'La captura automatica del token FUXION requiere la app Android instalada. ' +
    'En el navegador web usa el flujo manual (copiar y pegar el token).')),
};

const native = registerPlugin<FuxionCapturePlugin>('FuxionCapture', { web: () => webShim });

export const FuxionCapture = {
  isSupported(): boolean {
    return Capacitor.getPlatform() === 'android';
  },
  async openAndCapture(url: string): Promise<string> {
    const { token } = await native.openAndCapture({ url, localStorageKey: 'CapacitorStorage.xcorptoken' });
    return token;
  },
};
