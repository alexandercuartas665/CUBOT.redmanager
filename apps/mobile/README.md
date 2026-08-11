# CUBOT redmanager mobile

App Android para operadores del tenant. Consume `/api/mobile/*` del backend en
`https://red.cubot.com.co`. Login con el mismo email/password que la web.

## Sprint 1 (actual)

- Login (email + password + selector de tenant si el user tiene varios).
- Dashboard con KPIs (mensajes 7d, agentes, tokens x expirar, videos TikTok).
- Conversaciones: listado + detalle con historial cronologico.
- Agentes: listado, renovar token FUXION (pegando el JWT manual), sincronizar
  precios.
- Config: info del usuario/agencia + logout.

## Sprint 2 (proximo)

- WebView interno para app-aware.fuxion.com que captura el JWT automatico
  (sin que el usuario tenga que abrir DevTools).
- Push notifications para nuevos mensajes / tokens x vencer.

## Desarrollo (browser dev)

```bash
cd apps/mobile
npm install       # ya deberia estar hecho
npm run dev       # http://localhost:5173, con proxy /api → red.cubot.com.co
```

Login: usa el mismo email/password que la web.

## Compilar APK (Android)

Prerrequisitos:
- JDK 17+
- Android Studio (o Android SDK CLI) con `platform-tools` + `build-tools`
- Variable de entorno `ANDROID_HOME` apuntando al SDK

Pasos:

```bash
# 1. build de la UI web
npm run build

# 2. Solo la primera vez: agregar plataforma Android
npx cap add android

# 3. Copia el build al proyecto Android
npx cap sync android

# 4. Abrir en Android Studio (recomendado para iterar)
npx cap open android

# 5. En Android Studio: Build → Build Bundle(s) / APK(s) → Build APK(s)
#    El APK sale en: android/app/build/outputs/apk/debug/app-debug.apk
```

Alternativa CLI sin Android Studio:

```bash
cd android
./gradlew assembleDebug
# APK en: android/app/build/outputs/apk/debug/app-debug.apk
```

## Distribucion

El APK se instala manualmente en el celular (habilitando "Origenes desconocidos"
en Ajustes → Seguridad). Sin Play Store por ahora. Para actualizar: se manda un
APK nuevo por WhatsApp / email / URL y el user lo instala encima.

## Config Capacitor

- `appId`: `com.cubot.redmanager`
- `appName`: `CUBOT redmanager`
- `webDir`: `dist`
- `server.androidScheme`: `https` (para no marcar mixed-content al llamar a
  `https://red.cubot.com.co`).

## Estructura

```
src/
├── api/               # Cliente HTTP tipado (fetch → /api/mobile/*)
├── auth/              # AuthContext + storage (Capacitor Preferences)
├── pages/             # Una pantalla por archivo
├── shell/             # AppShell + bottom nav
├── App.tsx            # Router principal
├── main.tsx           # Entry point
└── index.css          # Tailwind + componentes utilitarios
```
