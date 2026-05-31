# ADR 0001 - Estructura de repositorio, stack y puertos locales

- Fecha: 2026-05-31
- Estado: Aceptado

## Contexto

CUBOT.redmanager arranca desde cero en `C:\DesarrolloIA\CUBOT.redmanager`. El vault Obsidian es la fuente de verdad funcional y propone .NET 10 + Blazor + EF Core 10. Existen proyectos hermanos de la familia CUBOT en la misma maquina (CUBOT.travels, nails, meals) que ya fijaron convenciones. CUBOT.crm (referencia citada para el menu) NO esta en disco.

## Decisiones

1. **Estructura `apps/backend/src` + `tests`** (no el `src/` plano de la hoja de ruta). Razon: alinear con CUBOT.travels, la referencia viva mas completa de la familia. La hoja de ruta del vault precede a esa convencion; se respeta el contenido (proyectos Domain/Application/Infrastructure/Shared/Api/Web/SuperAdmin/Workers + 3 tests) y se moderniza el layout.

2. **.NET 10 (SDK 10.0.300)**, fijado en `global.json`. Se instalo .NET 10 expresamente. A diferencia de CUBOT.travels (que quedo en net9 como puente temporal), redmanager apunta a `net10.0` desde el inicio, que es el target de la spec.

3. **Blazor interactividad Server** en Web y SuperAdmin (sin proyecto Web.Client/WASM). Razon: la hoja de ruta pide Server interactivo explicitamente; mas simple para el MVP. Se reevaluara WASM si un modulo lo exige.

4. **Solucion en formato `.slnx`** (formato XML nuevo, default de .NET 10).

5. **Puertos Docker dedicados** para no colisionar con los stacks ya corriendo en la maquina (propia: 5433/6380; cubot-travels: 5434/6381/5673/15673/5051; visal: 5435/6382/5674/15674; pgAdmin 5050 ocupado):

   | Servicio   | Puerto host |
   |------------|-------------|
   | PostgreSQL | 5436        |
   | Redis      | 6383        |
   | RabbitMQ   | 5675        |
   | RabbitMQ UI| 15675       |
   | pgAdmin    | 5052        |

   Project Docker `cubotrm`, contenedores `cubotrm-*`, base `cubot_redmanager_dev`.

## Consecuencias

- El comando `dotnet new sln` del roadmap produce `.slnx`; los scripts deben detectar `*.sln*`.
- Reutilizar codigo Razor de la familia (net9) en redmanager (net10) es compatible; si surge friccion se documenta aparte.
- El menu (Modulo 1.5) se construye desde la spec del vault + paleta morado/magenta, ya que crm no esta disponible y el NavMenu de travels aun es plantilla por defecto.
