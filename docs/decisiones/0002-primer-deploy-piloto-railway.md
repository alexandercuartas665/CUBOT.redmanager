# ADR 0002 - Primer deploy piloto 100% en Railway

- Fecha: 2026-07-05
- Estado: aceptado
- Contexto: preparacion del primer despliegue piloto de CUBOT.redmanager con dominio
  `red.cubot.com.co` (Web) y `admin.red.cubot.com.co` (SuperAdmin).

## Decision

El piloto se despliega **100% en Railway** (proyecto `cubot-redmanager`): servicios `web`,
`superadmin` y el addon Postgres. Esto se aparta de la "Fase 2 hibrida" (Railway + VPS) que
describe `06. Deploy/Deploy a Produccion - Railway.md` del vault.

## Razones

1. **Simplicidad del piloto**: un solo proveedor, TLS automatico (Let's Encrypt), deploy
   automatico por push a la rama `deploy`, rollback de un click desde el dashboard.
2. **Costo bajo**: web + superadmin + postgres estimados en USD 20-30/mes segun uso. Un VPS
   adicional no se justifica sin volumen.
3. **La Web no depende criticamente de Redis ni RabbitMQ hoy**: `AutoReplyWorker` y
   `TikTokMaintenanceWorker` son HostedService dentro del proceso Web, el sync es serial y no
   hay colas en uso. Se activan en Fase 2 cuando el volumen lo pida.
4. **Los proyectos Api y Workers son scaffolding** (verificado 2026-07-05): no se despliegan.

## Decisiones tecnicas derivadas

- **DataProtection persiste en Postgres** (`PersistKeysToDbContext`, tabla
  `data_protection_keys`): el filesystem de Railway es efimero y las llaves en disco se
  perderian en cada deploy, invalidando cookies y secretos cifrados (tokens OAuth, API keys
  YCloud). Mismo patron que CUBOT.travels.
  - Desviacion consciente del anexo del doc de deploy: el `DbSet<DataProtectionKey>` NO se
    expone en `IApplicationDbContext` (capa Application) para no acoplar Application a un
    paquete ASP.NET. Vive solo en `CubotRedManagerDbContext` (Infrastructure).
- **Seeds demo solo en Development**: el seed creaba en cada arranque usuarios demo con claves
  publicas (admin@cubot.local/admin123). En produccion eso seria un SuperAdmin con clave
  conocida. Ahora el primer SuperAdmin de produccion se crea via `Bootstrap__AdminEmail` +
  `Bootstrap__AdminPassword` (solo si la BD no tiene usuarios; retirar las variables despues).
- **`DATABASE_URL` en formato URI se convierte al arranque** a formato Npgsql
  (`DependencyInjection.NormalizeConnectionString`), para poder referenciar
  `${{Postgres.DATABASE_URL}}` sin transformaciones manuales.
- **Sin `railway.toml`** `[DEFAULT]`: igual que travels, el Dockerfile de cada servicio se
  configura en el dashboard de Railway (Root Directory `/` + Dockerfile Path). Menos archivos
  que mantener; si algun dia se necesita config-as-code, se agrega.
- **EF Core 10.0.4 -> 10.0.9**: requerido por `Microsoft.AspNetCore.DataProtection.
  EntityFrameworkCore` 10.0.9 y de paso resuelve advisories NU1903/NU1904 de la version 10.0.4.
- **Migraciones al arranque** (`db.Database.MigrateAsync()`): mecanismo oficial del piloto.
  Con 2 servicios apuntando a la misma BD hay riesgo teorico de carrera en el primer arranque;
  mitigacion: desplegar `web` primero y `superadmin` despues.

## Criterios para migrar a Fase 2 (hibrida Railway + VPS)

Cualquiera de estos dispara la revision:

- Mas de ~10 agencias (tenants) activas o mas de ~50 cuentas sociales sincronizando.
- Latencia de sync o de autorespuesta percibida (> 5 min de atraso sostenido) por competir
  con el trafico web en el mismo proceso.
- Necesidad real de colas (webhooks de Meta a volumen, publicaciones programadas masivas)
  o de locks distribuidos (multiples replicas del worker).
- Costo Railway superando el costo de un VPS equivalente (~USD 40-60/mes).

## Consecuencias

- Positivas: time-to-pilot corto, cero administracion de servidores, HTTPS y dominios
  gestionados, rollback trivial.
- Negativas: sin Redis/RabbitMQ los workers no escalan horizontalmente (una sola replica del
  servicio web); vendor lock-in suave (mitigado: todo es Docker + env vars).

## Referencias

- Vault: `06. Deploy/Deploy a Produccion - Railway.md` (estrategia oficial y anexo del piloto).
- Vault: `06. Deploy/2026-07-05 - Preparacion primer deploy piloto Railway.md` (guia operativa).
- ADR 0001 (estructura y stack).
