# ADR 0004 - Integracion no-oficial con FUXION AWARE

Fecha: 2026-07-23

## Contexto

Los agentes IA de la plataforma necesitan generar "links de pago" para clientes
finales (WhatsApp) que resulten en compras del catalogo del distribuidor
FUXION. FUXION es un negocio multinivel en el que cada distribuidor tiene su
propio portal en `app-aware.fuxion.com` (SPA autenticada) desde el que puede
crear sales-links personalizados.

FUXION **no publica una API oficial** para terceros. La generacion de
sales-links se hace manualmente desde el portal, o programaticamente contra el
API interno del SPA que descubrimos inspeccionando su trafico:

- `POST https://api-aware.fuxion.com/api/products/user/{userId}/generate-power-link`
  con `{country, description, items:[{itemId, amount}]}`, respuesta
  `{status,data:{url}}`.
- Auth por JWT Bearer que la SPA guarda en
  `localStorage.CapacitorStorage.xcorptoken`.
- Verificacion de sesion en `POST /api/auth/user/verify-session`.

## Decision

Integrar contra ese API interno con las siguientes salvaguardas para tolerar
cambios de contrato o inestabilidad:

1. **Todo configurable por agente** (`AiAgent.Payment*`), editable desde
   `/agentes` sin redeploy:
   - `PaymentApiBaseUrl` (default `https://api-aware.fuxion.com`)
   - `PaymentApiPathTemplate` (default
     `/api/products/user/{userId}/generate-power-link`)
   - `PaymentResponseUrlPath` (default `data.url`, dot-separated JSON path)
   - `PaymentUserId` (id de distribuidor)
   - `PaymentCountry` (ISO2 lowercase)
   - `PaymentTokenEncrypted` (JWT cifrado con DataProtection)
   - Catalogo: `PaymentCatalogContainerName`, `NameColumn`, `ProductIdColumn`.
2. **Feature flag** `PaymentEnabled` por agente. Si algo se rompe, el operador
   apaga la feature y el agente cae al fallback graceful.
3. **Fallback graceful**: en cualquier error (token vencido, catalogo mal,
   red, contrato roto) el processor sustituye el marker
   `[[link_pago: ...]]` por `"un asesor te contactara en un momento para
   completar tu pago"`. El cliente nunca ve un error tecnico.
4. **Bitacora detallada** (`AiAgentRunLog`) por cada llamada con el detalle
   del error o el conteo de links generados. Sin exponer el token, el body ni
   los headers de autenticacion.
5. **Retry con backoff** exponencial (1s + 3s) para errores transitorios
   (5xx / 429 / timeout). No se reintentan errores logicos (400/401/403).
6. **Worker de vigilancia** (`FuxionPaymentMaintenanceWorker`) cada 4h llama
   `/api/auth/user/verify-session`, actualiza
   `PaymentTokenLastVerifiedAt`, y notifica al operador via
   `TenantAlertConfig` (misma via que las alertas de TikTok) cuando el token
   fue rechazado o esta a menos de 24h de expirar. Dedupe por
   `PaymentTokenExpiryNotifiedAt` (no reenvia en las siguientes 24h).
7. **Token cifrado en la BD** con DataProtection (llaves en
   `data_protection_keys`), nunca loggeado ni expuesto en la UI. El operador
   solo ve `TokenPresent` bool + `expira en Xd/Xh` (parseando la claim `exp`
   del JWT sin validar firma).

## Consecuencias

**Positivas**

- Automatizacion end-to-end: el cliente pide, el agente genera el link, el
  cliente paga. Sin intervencion del operador para links validos.
- Si FUXION cambia el path/response de forma sencilla, se ajusta desde
  `/agentes` sin deploy.
- Si algo grande cambia (auth completo, dominio, contrato), el fallback
  protege la experiencia del cliente y las alertas avisan al operador rapido.
- Bitacora permite depurar cualquier cambio de contrato o rechazo puntual.

**Negativas / riesgos**

- Cualquier cambio de FUXION en el contrato o auth **puede romper la
  feature**. Es un API interno, no publico.
- El token se copia manualmente del navegador (no hay refresh oficial). Cuando
  expira (JWT, tipicamente dias / semanas), el operador debe volver a
  copiarlo.
- FUXION podria detectar y bloquear el uso automatizado. Mitigado con retry
  suave y rate limit propio (todavia no implementado; TODO cuando la feature
  este en produccion con volumen).
- Legalmente esto es zona gris (uso automatizado de un portal de
  distribuidor). Es el distribuidor quien decide asumir el riesgo; nosotros
  proveemos la herramienta, no la usamos.

## Alternativas descartadas

- **API oficial FUXION**: no existe. Se abandono cuando FUXION dejo claro que
  no publica una.
- **Playwright headless que hace login + navega + genera link**: mas fragil
  (cualquier cambio de UI la rompe) y detectable. Descartado.
- **Modo manual siempre** (el agente le dice al cliente "un asesor te
  contactara"): funciona pero elimina el valor diferencial. Es el
  fallback ahora, no el modo principal.

## Migracion / rollback

Rollback simple: apagar `PaymentEnabled` en todos los agentes o
`FuxionPaymentMaintenance:Enabled=false`. La migracion aditiva (columnas
nullable) no requiere down-migration para desactivar.

## Referencias

- Investigacion: task #96 en el tracker.
- Fase 1 (config + UI): commit `6399321`.
- Fase 2 (marker + cliente HTTP + processor): commit `2d61bec`.
- Fase 3 (worker + retry + ADR): este ADR.
