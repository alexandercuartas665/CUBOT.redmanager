# CLAUDE.md - Memoria del agente de desarrollo para CUBOT.redmanager

> Primera lectura obligatoria para cualquier agente antes de modificar codigo en este repo. Reglas pequenas, concretas y verificables.

---

## 1. Contexto del proyecto

CUBOT.redmanager es un **SaaS multi-tenant para agencias de marketing digital** que gestionan las redes sociales (Meta, TikTok, YouTube) de sus clientes (marcas) via OAuth oficial. Parte de la familia CUBOT y reusa su estilo y patrones (ver CUBOT.travels como referencia viva de la familia).

Repo: `https://github.com/alexandercuartas665/CUBOT.redmanager.git`

### Terminologia critica (no negociable)

- **Agencia = Tenant SaaS** (la empresa que paga el plan de redmanager).
- **Cliente = Marca/empresa** cuyas redes gestiona la agencia. NO es el inquilino.

---

## 2. Fuente de verdad

Las especificaciones funcionales viven en el **vault Obsidian** (no contiene codigo de produccion):

```
C:\Users\acuartas\Documents\Personal\OneDrive\Proyectos\02. Gestor de marqueting\CUBOT.redmanager
```

Leer en este orden antes de implementar un modulo (no reinterpretar a memoria):

1. `01. Requerimiento/Capa 0 Vision General/CUBOT.redmanager.md` - arquitectura general
2. `02. Inventario de modulos/INVENTARIO GENERAL.md` - mapa de modulos, capas, dependencias, tracker, orden de construccion
3. `02. Inventario de modulos/Modelo de Datos - Entidades y Tablas.md` - modelo de datos objetivo
4. `03. Hoja de Ruta desarrollo/HOJA DE RUTA DESARROLLO.md` - plan paso a paso (contrato de trabajo)
5. `04. Notas para desarrollador/Notas de desarrollo.md` - decisiones delicadas (cifrado, idempotencia, IA)
6. `01. Requerimiento/Capa 1/2/3 ...` - Super Admin, Nucleo Operativo, Agentes de IA
7. `00. Codigo de referencia VB.NET/README - Lo Aprendido VB.NET.md` - OBLIGATORIO antes de TikTok

---

## 3. Estructura del repositorio

```txt
CUBOT.redmanager/
  apps/backend/
    src/
      CubotRedManager.Domain          # entidades, enums, eventos de dominio
      CubotRedManager.Application     # casos de uso, CQRS, validaciones, interfaces
      CubotRedManager.Infrastructure  # EF Core, proveedores OAuth, DataProtection, repos
      CubotRedManager.Shared          # DTOs y contratos compartidos Web/Api
      CubotRedManager.Api             # ASP.NET Core Web API, endpoints, SignalR, webhooks
      CubotRedManager.Web             # Blazor Server (consola de la agencia + /admin SuperAdmin, ADR 0003)
      CubotRedManager.Workers         # BackgroundService + Hangfire (sync, refresh, publish)
    tests/
      CubotRedManager.Domain.Tests
      CubotRedManager.Application.Tests
      CubotRedManager.Integration.Tests   # incluye aislamiento multi-tenant
    CubotRedManager.slnx
  deploy/docker/                      # docker-compose, .env.example
  docs/decisiones/                    # ADRs
  docs/arquitectura/
  global.json                         # pin SDK .NET 10
  CLAUDE.md
```

Estructura espejo de CUBOT.travels (convencion de la familia). La hoja de ruta del vault describe un layout plano `src/`; aqui se usa `apps/backend/src` para alinear con la familia (ver `docs/decisiones/0001-estructura-y-stack.md`).

---

## 4. Stack tecnico

- .NET 10 / ASP.NET Core 10 (SDK 10.0.300, fijado en global.json).
- Blazor Server (interactividad Server) en la app Web (la consola SuperAdmin vive dentro bajo `/admin/*`, ver ADR 0003). **Sin Node/npm/React/Vue.** El prototipo HTML/Tailwind es solo referencia visual.
- EF Core 10 sobre PostgreSQL, snake_case, enums como texto, jsonb para campos dinamicos, Guid v7.
- Redis (cache de metricas, locks de sync, rate limiting OAuth).
- RabbitMQ + MassTransit (sync, webhooks, scheduled posts).
- SignalR (bandeja, metricas en vivo, Kanban).
- Hangfire / BackgroundService (cron de sync, refresh de tokens, publicaciones).
- Wompi (cobro SaaS), QuestPDF (reportes), Serilog + OpenTelemetry, MediatR, FluentValidation, Polly.
- Pruebas: xUnit + Testcontainers (Postgres) + Playwright .NET (E2E, sin Node).
- Referencia visual: paleta CUBOT.crm (morado `#A03DC9`, magenta `#C7398B`, degradado violeta) sobre Bootstrap.

---

## 5. Reglas no negociables

- **Multi-tenancy:** toda entidad operativa lleva `TenantId`; toda consulta tenant-scoped filtra con `HasQueryFilter`. Operator solo ve clientes asignados (UserClientLink). Tests de aislamiento desde el primer modulo.
- **Super Admin separado logicamente** (rutas + politicas + layout + auditoria): el rol `PlatformOperator` no se mezcla con `TenantMember`. Toda la consola de gobierno vive bajo `/admin/*` con `[Authorize(Policy = PlatformOperator)]` y su propio `AdminLayout`. La separacion de **proceso/dominio** se elimino en el piloto (ADR 0003, Camino B) para reducir costo Railway y simplificar SSO.
- **Tokens OAuth cifrados** con DataProtection (llaves en tabla `data_protection_keys`). Columnas `*Encrypted`. JAMAS en logs (ni Base64). Mascara en UI.
- **Webhooks idempotentes** por `(network_code, provider_event_id)`. Responder 200 en < 200ms; procesar async.
- **IA gobernada:** sugiere; el operador aprueba. Unica excepcion: Modulo 2.11 Autorespuesta (feature flags `autoreply_*`, blacklist obligatoria, delay anti-bot, MaxRepliesPerRun, horario activo, log inmutable).
- **No loggear:** access/refresh tokens, llaves Wompi/IA, DMs completos, datos personales sensibles, authorization codes.
- **Secretos** en `.env`/user-secrets/secret store, nunca versionados.

---

## 6. Estrategia de ramas

- `main`: desarrollo. Todo PR de feature merge aqui con CI verde (build + unit + integracion + cifrado + format).
- `deploy`: produccion (protegida). Solo recibe merges desde `main` y solo con la suite COMPLETA verde (CI + sandbox Meta/TikTok/YouTube + E2E Playwright). Cada commit dispara despliegue.
- `feature/*`, `fix/*`, `refactor/*` nacen de `main`. `hotfix/*` nace de `deploy` y se cherry-pick a `main`.

---

## 7. Orden de construccion (no romper sin ADR)

```
0.1 Super Admin -> 0.2 Planes -> 1.1 Onboarding -> 1.2 Usuarios/Roles
  -> 1.5 Menu del Sistema (PRIMERA tarea funcional)
    -> 2.1 Clientes -> 2.2 OAuth + 2.3 Cuentas -> 2.4 Sync -> 2.5 Calendario
      -> 2.6 Bandeja -> 2.7 Kanban -> 2.8 Reportes
        -> 3.1 AI Gateway -> 3.3 Copywriter -> 3.4 Bandeja IA
          -> 2.11 Autorespuesta de Comentarios
```

---

## 8. Entorno local (Docker)

Bloque de puertos DEDICADO para no chocar con otros stacks de la maquina (propia/cubot-travels/visal):

| Servicio   | Puerto host | Contenedor          |
|------------|-------------|---------------------|
| PostgreSQL | 5436        | cubotrm-postgres    |
| Redis      | 6383        | cubotrm-redis       |
| RabbitMQ   | 5675        | cubotrm-rabbitmq    |
| RabbitMQ UI| 15675       | cubotrm-rabbitmq    |
| pgAdmin    | 5052        | cubotrm-pgadmin     |

Base de datos dev: `cubot_redmanager_dev`. Project Docker: `cubotrm`.

```powershell
cd C:\DesarrolloIA\CUBOT.redmanager\deploy\docker
docker compose up -d
```

---

## 9. Checklist antes de cada commit

- [ ] `dotnet build` y `dotnet test` verdes.
- [ ] Sin secretos versionados (claves Meta/TikTok/Google/Wompi fuera del codigo).
- [ ] Sin queries tenant-scoped sin filtro de tenant.
- [ ] Ninguna ruta loggea access/refresh token ni DMs completos.
- [ ] Si toca Super Admin: auditoria. Si toca IA: medicion de tokens.
- [ ] Si cierra un modulo: actualizar `INVENTARIO GENERAL.md` del vault.
- [ ] Si hay decision arquitectonica nueva: ADR en `docs/decisiones`.

---

## 10. Convenciones de codigo

- Nombres de clases/metodos en ingles; mensajes de usuario final en espanol.
- Scripts de consola (.ps1/.bat/.sh): SOLO ASCII puro (sin cajas Unicode, flechas ni emojis).
- No duplicar reglas entre capas: una regla de negocio vive en Application, no se replica en Api/Web.
