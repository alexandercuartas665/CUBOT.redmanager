import type { CapacitorConfig } from '@capacitor/cli';

/**
 * Config de Capacitor para CUBOT redmanager mobile.
 *
 * appId: reverse-DNS del grupo CUBOT + producto redmanager.
 * appName: nombre visible al usuario en Android (lanzador, notificaciones).
 * webDir: donde queda el build de Vite (dist/). Capacitor copia estos assets al bundle nativo.
 *
 * server.androidScheme = 'https' evita advertencias de mixed-content cuando llamamos a
 * https://red.cubot.com.co desde una app que se sirve como capacitor:// (el default seria http).
 */
const config: CapacitorConfig = {
  appId: 'com.cubot.redmanager',
  appName: 'CUBOT redmanager',
  webDir: 'dist',
  server: {
    androidScheme: 'https',
    // Solo dominios que la app tiene permiso de llamar sin CORS especial:
    allowNavigation: ['red.cubot.com.co', 'app-aware.fuxion.com'],
  },
  android: {
    // Fuerza el WebView a aceptar cookies persistentes (para que la sesion FUXION sobreviva
    // entre aperturas de la app cuando implementemos el WebView interno).
    webContentsDebuggingEnabled: false, // poner true SOLO para debugging con Chrome DevTools
  },
};

export default config;
