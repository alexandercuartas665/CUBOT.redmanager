import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Blazor Server / red.cubot.com.co no acepta CORS de origenes locales.
    // Para 'npm run dev' usamos proxy: cualquier /api/... del dev server se
    // reenvia al backend en produccion. En el APK, Capacitor apunta directo.
    proxy: {
      '/api': {
        target: 'https://red.cubot.com.co',
        changeOrigin: true,
        secure: true,
      },
    },
  },
});
