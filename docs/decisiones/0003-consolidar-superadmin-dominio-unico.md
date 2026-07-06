# ADR 0003 - Consolidar SuperAdmin en dominio unico bajo /admin

- **Fecha**: 2026-07-06
- **Estado**: Aceptado
- **Supera**: reparte parcial de la regla dura "Super Admin separado" del `CLAUDE.md` (seccion 5).
- **Reemplaza**: ADR 0002 pactaba dos servicios Railway (`web` en `red.cubot.com.co` + `superadmin` en `admin.red.cubot.com.co`).

## Contexto

El piloto en Railway arranco con dos servicios Blazor Server (`CubotRedManager.Web` y
`CubotRedManager.SuperAdmin`) apuntando a dos subdominios (`red.` y `admin.red.`). El brinco
entre subdominios exige compartir cookie via `Domain=".cubot.com.co"` y mantener DNS + TLS + apps
duplicados, con el costo de dos servicios Railway corriendo en paralelo.

Al validar el primer deploy real el usuario detecto:

- El SSO por cookie compartida funciona pero es fragil (se rompe si un subdominio no propaga a
  tiempo el TLS, o si `SameSite` cambia entre navegadores).
- El costo Railway se duplica sin traer valor de aislamiento (ambos servicios apuntan a la MISMA
  BD Postgres, comparten `DataProtection`, comparten `AddInfrastructure`).
- Solo tres personas manejaran la consola de gobierno en el corto plazo. La separacion "de
  proceso" no compra nada de seguridad extra frente a la separacion "de rutas + politicas".

## Decision

Consolidar la consola de plataforma dentro del proyecto `CubotRedManager.Web`, servida bajo el
prefijo de ruta `/admin/*` en el mismo dominio `red.cubot.com.co`. Se elimina el proyecto
`CubotRedManager.SuperAdmin`, el `Dockerfile.superadmin`, el servicio Railway equivalente y el
subdominio `admin.red.cubot.com.co`.

La separacion se preserva a los niveles que realmente importan:

- **Rutas**: todas las paginas de gobierno viven bajo `/admin/*`.
- **Politica de autorizacion**: `PlatformOperator` sigue exigiendo `platform_role`; el
  `AdminLayout` no se instancia jamas para un tenant_user.
- **Auditoria**: `IAuditWriter` sigue separando eventos por actor + tenant + entidad.
- **Layout dedicado**: `AdminLayout` + `AdminNavMenu` mantienen el shell de gobierno (branding,
  paleta, nav). El shell de agencia (`MainLayout` + `NavMenu`) no aparece cuando el usuario esta
  bajo `/admin/*`.

## Consecuencias

**Positivas**

- Un solo servicio Railway. Reduce ~50% el costo del piloto.
- Un solo dominio. Sin necesidad de DNS + TLS + cookie compartida.
- Login unificado sin brinco cross-subdominio: `/login` -> `/admin` (operator) o `/dashboard`
  (tenant), todo en el mismo host.
- El desarrollador local corre UN solo `dotnet run` en `:5036` (antes eran dos: `:5036` + `:5037`).
- Menos superficie de configuracion: se retira `Deployment:SuperAdminUrl` y `Cookie.Domain`.

**Negativas / a vigilar**

- La regla dura del `CLAUDE.md` "UI separada" se relaja a "layout separado". Requiere que
  cualquier futuro modulo de gobierno **respete la ruta `/admin/*` y la policy `PlatformOperator`**
  sin excepcion.
- Si en el futuro (Fase 3 enterprise) hay que separar procesos por escalado o multi-region, el
  cambio es mayor que "cambiar el dominio del subservicio". Se documenta como reto de esa fase.

## Alternativas descartadas

- **Mantener dos servicios**: era la opcion original (ADR 0002). Descartada por costo Railway y
  por fragilidad de la cookie compartida.
- **Reverse proxy que enrute `/admin/*` a un servicio y `/` a otro**: agrega infra (mas caro, mas
  puntos de fallo), no menos.
- **SuperAdmin como Razor Class Library referenciada por Web**: viable, pero mover fisicamente
  los archivos es mas simple y menos magia. Solo agregaria valor si hubiera un segundo host que
  reusara la biblioteca, que no es el caso.

## Trabajo asociado

- Movimiento de 7 paginas Razor a `Web/Components/Pages/Admin/` con rutas `/admin/*`.
- Portado del shell como `AdminLayout` + `AdminNavMenu` en `Web/Components/Layout/`.
- Cambio de redirects en `Web/Program.cs` (`/dashboard` -> `/admin` para operadores).
- Retiro de `superAdminUrl`, `Cookie.Domain` de produccion y del `IsSafeReturnUrl` para
  `admin.red.cubot.com.co`.
- Eliminacion del proyecto `CubotRedManager.SuperAdmin` del `.slnx` y del filesystem.
- Eliminacion de `apps/backend/Dockerfile.superadmin`.
- Actualizacion de `tools/run.ps1` (un solo puerto: `5036`).

## Sesion de deploy (despues del merge a `deploy`)

1. Railway: **borrar el servicio `zesty-courage`** (superadmin) -> detiene su facturacion.
2. Railway (servicio web): quitar el dominio `admin.red.cubot.com.co` si sigue configurado;
   retirar la variable de entorno `Deployment__SuperAdminUrl`.
3. Namecheap: eliminar los CNAME `admin.red` y su TXT de verificacion (el usuario los limpiara).
4. Verificar login `fulano03022012@gmail.com` -> aterriza en `/admin` de `red.cubot.com.co`.
5. Verificar login `cliente@cubot.local` -> aterriza en `/dashboard` de `red.cubot.com.co`.

Ref: `06. Deploy/Camino B - Consolidar SuperAdmin en dominio unico (brief sesion de codigo).md`.
