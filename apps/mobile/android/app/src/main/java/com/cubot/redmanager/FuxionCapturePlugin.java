package com.cubot.redmanager;

import android.app.Activity;
import android.app.Dialog;
import android.graphics.Color;
import android.util.Base64;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.webkit.CookieManager;
import android.webkit.WebSettings;
import android.webkit.WebStorage;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

import org.json.JSONObject;

import java.nio.charset.StandardCharsets;

/**
 * Plugin propio (no publicado en npm) para abrir una WebView Android modal dentro de la app y
 * capturar automaticamente un valor de localStorage cuando aparece. Usado por AgentsPage para
 * renovar el token FUXION sin que el user tenga que hacer copy/paste manual.
 *
 * Flujo:
 *   1. openAndCapture({url, localStorageKey}) -> abre Dialog fullscreen con toolbar + WebView.
 *   2. El user hace login en el sitio (cookies + localStorage persisten entre aperturas).
 *   3. Cada pageFinished el plugin ejecuta evaluateJavascript leyendo la clave del localStorage.
 *   4. Cuando la clave aparece con longitud > 20 chars, cierra el WebView y resuelve con el valor.
 *   5. Si el user cierra manualmente, rechaza con "cancelled".
 *
 * v2: fullscreen real (Dialog sin AlertDialog chrome) + WebView con viewport mobile + user-agent
 *     Chrome mobile para forzar la version responsiva del portal FUXION.
 */
@CapacitorPlugin(name = "FuxionCapture")
public class FuxionCapturePlugin extends Plugin {

    @PluginMethod
    public void openAndCapture(final PluginCall call) {
        final String url = call.getString("url");
        final String key = call.getString("localStorageKey", "CapacitorStorage.xcorptoken");
        if (url == null || url.isEmpty()) {
            call.reject("url required");
            return;
        }

        final Activity activity = getActivity();
        activity.runOnUiThread(() -> {
            // Dialog custom SIN chrome de AlertDialog: usamos android.R.style.Theme_Black_NoTitleBar_Fullscreen.
            final Dialog dialog = new Dialog(activity, android.R.style.Theme_Black_NoTitleBar_Fullscreen);
            Window win = dialog.getWindow();
            if (win != null) {
                win.setLayout(WindowManager.LayoutParams.MATCH_PARENT, WindowManager.LayoutParams.MATCH_PARENT);
                // Aseguramos que la Dialog se dibuje detras de la status bar (fullscreen real).
                win.setFlags(WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                             WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN);
            }

            // Contenedor vertical: toolbar arriba + WebView abajo con peso 1.
            final LinearLayout root = new LinearLayout(activity);
            root.setOrientation(LinearLayout.VERTICAL);
            root.setLayoutParams(new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            root.setBackgroundColor(Color.WHITE);

            // Toolbar simple: titulo a la izquierda + boton "Cerrar" a la derecha.
            final LinearLayout toolbar = new LinearLayout(activity);
            toolbar.setOrientation(LinearLayout.HORIZONTAL);
            toolbar.setBackgroundColor(Color.parseColor("#A03DC9")); // morado CUBOT
            toolbar.setPadding(24, 24, 24, 24);
            toolbar.setGravity(Gravity.CENTER_VERTICAL);
            LinearLayout.LayoutParams toolbarLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            toolbar.setLayoutParams(toolbarLp);

            final TextView title = new TextView(activity);
            title.setText("Login FUXION - captura de token");
            title.setTextColor(Color.WHITE);
            title.setTextSize(15);
            LinearLayout.LayoutParams titleLp = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
            title.setLayoutParams(titleLp);
            toolbar.addView(title);

            // Boton "Cerrar sesion FUXION": limpia cookies + storage + recarga. Imprescindible
            // porque el WebView PERSISTE la sesion entre aperturas (ver DomStorage y Cookies abajo),
            // y si el token quedo caducado el user no tenia como forzar re-login desde la propia
            // Dialog. Antes del fix, el plugin devolvia inmediatamente el token viejo del localStorage
            // (una vez logueado, nunca mas pedia credenciales) y nunca refrescaba el exp.
            final Button logoutBtn = new Button(activity);
            logoutBtn.setText("Cerrar sesion");
            logoutBtn.setTextColor(Color.WHITE);
            logoutBtn.setBackgroundColor(Color.TRANSPARENT);
            toolbar.addView(logoutBtn);

            final Button closeBtn = new Button(activity);
            closeBtn.setText("Cerrar");
            closeBtn.setTextColor(Color.WHITE);
            closeBtn.setBackgroundColor(Color.TRANSPARENT);
            toolbar.addView(closeBtn);
            root.addView(toolbar);

            // WebView ocupa todo el espacio restante.
            final WebView webView = new WebView(activity);
            LinearLayout.LayoutParams wvLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
            webView.setLayoutParams(wvLp);

            WebSettings settings = webView.getSettings();
            settings.setJavaScriptEnabled(true);
            settings.setDomStorageEnabled(true);
            settings.setDatabaseEnabled(true);
            // Viewport responsive: hace que la pagina se ajuste al ancho de pantalla.
            settings.setUseWideViewPort(true);
            settings.setLoadWithOverviewMode(true);
            // Zoom disponible por si el user quiere acercar (sin controles nativos superpuestos).
            settings.setBuiltInZoomControls(true);
            settings.setDisplayZoomControls(false);
            settings.setSupportZoom(true);
            settings.setTextZoom(100);
            // Aseguramos que el sitio no muestre la version desktop.
            settings.setUserAgentString(settings.getUserAgentString() + " CUBOTMobile/1.0");

            // Aceptar cookies del sitio (necesario para que el login FUXION persista entre aperturas).
            CookieManager.getInstance().setAcceptCookie(true);
            CookieManager.getInstance().setAcceptThirdPartyCookies(webView, true);

            root.addView(webView);
            dialog.setContentView(root);
            dialog.setOnCancelListener(d -> call.reject("cancelled"));
            closeBtn.setOnClickListener(v -> { dialog.dismiss(); call.reject("cancelled"); });

            // Cerrar sesion FUXION: borra cookies + Web/DomStorage + recarga, para que el portal
            // pida login nuevo y emita un JWT fresco. Causa raiz del "mi token sigue caducado":
            // la sesion persistida devolvia siempre el mismo token viejo sin volver a autenticar.
            logoutBtn.setOnClickListener(v -> {
                try {
                    title.setText("Sesion limpiada. Haz login para generar token nuevo.");
                    CookieManager.getInstance().removeAllCookies(null);
                    CookieManager.getInstance().flush();
                    WebStorage.getInstance().deleteAllData();
                    webView.clearCache(true);
                    webView.clearFormData();
                    webView.clearHistory();
                    webView.loadUrl("javascript:try{localStorage.clear();sessionStorage.clear();}catch(e){}");
                    webView.loadUrl(url);
                } catch (Exception ignored) { /* best-effort */ }
            });

            final boolean[] resolved = { false };

            webView.setWebViewClient(new WebViewClient() {
                @Override
                public void onPageFinished(WebView view, String pageUrl) {
                    super.onPageFinished(view, pageUrl);
                    if (resolved[0]) { return; }
                    // Lee el localStorage; el resultado viene como JSON string ("valor" o null).
                    view.evaluateJavascript(
                        "(function(){try{return localStorage.getItem('" + key + "');}catch(e){return null;}})()",
                        result -> {
                            if (resolved[0]) { return; }
                            // result es tipo: "\"eyJhbGci...\"" o "null" o "\"\"".
                            if (result == null || result.equals("null") || result.length() < 25) {
                                return; // aun no esta seteado
                            }
                            // Extraer el string (quitar comillas envolventes + un-escapar).
                            String token = result;
                            if (token.startsWith("\"") && token.endsWith("\"")) {
                                token = token.substring(1, token.length() - 1)
                                    .replace("\\\"", "\"")
                                    .replace("\\\\", "\\");
                            }
                            if (token.length() < 20) { return; }

                            // CRITICO: validar que el token NO este expirado. El portal FUXION guarda
                            // el token en localStorage y lo deja ahi aunque haya caducado — asi que
                            // sin este chequeo el plugin devolveria el mismo token viejo despues de
                            // cada "Renovar auto" y el PaymentLinkProcessor seguiria recibiendo 401.
                            // Si el token ya expiro, mostramos aviso y seguimos esperando al user
                            // (debe pulsar "Cerrar sesion" y hacer login para que FUXION emita uno
                            // nuevo); si no es JWT valido, tambien lo rechazamos.
                            long exp = readJwtExp(token);
                            long now = System.currentTimeMillis() / 1000L;
                            if (exp == 0) {
                                title.setText("Token sin claim exp - esperando token valido...");
                                return;
                            }
                            if (exp <= now) {
                                title.setText("Token caducado. Pulsa 'Cerrar sesion' y vuelve a loguear.");
                                return;
                            }

                            resolved[0] = true;
                            dialog.dismiss();
                            JSObject ret = new JSObject();
                            ret.put("token", token);
                            ret.put("url", pageUrl);
                            call.resolve(ret);
                        });
                }
            });

            dialog.show();
            webView.loadUrl(url);
        });
    }

    /**
     * Parsea la claim "exp" (unix seconds) del payload del JWT, sin validar la firma.
     * Devuelve 0 si el token no es JWT valido o no tiene exp. Solo se usa para rechazar
     * tokens que capturamos del localStorage del portal FUXION y que ya expiraron: la
     * validez real contra el API de FUXION la decide el servidor cuando el backend use
     * el token para pedir el sales-link.
     */
    private static long readJwtExp(String token) {
        try {
            String[] parts = token.split("\\.");
            if (parts.length < 2) { return 0; }
            // Base64 URL-safe sin padding ni wrap: el payload JWT viene asi por spec.
            byte[] raw = Base64.decode(parts[1], Base64.URL_SAFE | Base64.NO_WRAP | Base64.NO_PADDING);
            JSONObject json = new JSONObject(new String(raw, StandardCharsets.UTF_8));
            return json.optLong("exp", 0);
        } catch (Exception e) {
            return 0;
        }
    }
}
