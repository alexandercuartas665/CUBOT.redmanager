# Prueba MCP -> Agente FUXION (modo mock)

## Estado de la API key

La tabla `ai_provider_configs` tiene una entrada para `Gemini` con `is_enabled = true` y `api_key_encrypted` no vacia (modelo `gemini-2.5-pro`). Al invocar el endpoint dev `POST /dev/test-agent-mcp`, el `AiInferenceService` responde:

```
La API key del proveedor esta cifrada con una version anterior. Vuelve a guardarla en Servidores de IA.
```

Eso indica que el anillo de DataProtection roto la llave que cifro la API key (es la pista que devuelve `ISecretProtector.Unprotect` al fallar). Para correr las pruebas reales:

1. Entrar como Super Admin a `http://localhost:5037`.
2. Ir a "Servidores de IA" -> proveedor Gemini.
3. Pegar de nuevo la API key y guardar (queda re-cifrada con el anillo actual `.dp-keys`).
4. Volver a correr `pwsh -File tools\test-agent-fuxion.ps1`.

## Pipeline MCP verificado

La integracion MCP funciona end-to-end:

- Endpoint dev `GET /dev/test-agent-mcp/prompt?agent=FUXION` devuelve **8.019 caracteres** de prompt ya con los placeholders `{{LIST.CONTAINERS}}`, `{{CONTAINER:Gestion de Productos}}` y `{{CONTAINER:Listado Precios Productos}}` expandidos.
- El prompt resuelto contiene los 20 productos y los 20 precios sembrados en formato markdown.
- El archivo completo del prompt resuelto se guardo en `tools/fuxion-prompt-resolved.txt` para auditoria.

## Las 6 preguntas que se enviarian al agente

| #  | Pregunta |
|----|----------|
| 1  | Hola, que productos tienes para el cansancio? |
| 2  | Cual es el precio de VIVA en Colombia? |
| 3  | Que producto recomiendas para el insomnio? |
| 4  | Tienes algun producto para fortalecer la inmunidad? |
| 5  | Cuanto cuesta FORZA en Mexico? |
| 6  | Que tienes en la linea de Belleza? |

## Lo que el agente "deberia" responder (segun los datos del prompt resuelto)

Esto es lo que el LLM va a ver y procesar cuando se arregle la API key:

1. **Cansancio** -> VIVA (Colombia, $49.99), FORZA (Mexico, $54.50), FORZA SPORT (Ecuador, $58.50), PROTEO MAX (Chile, $84.50). Todos listan "cansancio" o "fatiga" en SINTOMAS.
2. **Precio VIVA en Colombia** -> $49.99. Beneficio: Multinutriente diario con complejo B y adaptogenos suaves.
3. **Insomnio** -> NOX DREAM (Mexico, $44.50). Mezcla relajante con melatonina, magnesio y manzanilla.
4. **Inmunidad** -> NUTRA SHIELD (Argentina, $47.99) y NUTRA C PLUS (Panama, $38.50). Ambos en linea Inmunidad.
5. **Precio FORZA en Mexico** -> $54.50. Beneficio: activador mental con foco y concentracion sin cafeina excesiva.
6. **Linea Belleza** -> BELLA SKIN (Colombia, $59.50, colageno) y BELLA AGE (Mexico, $78.99, resveratrol antiage).

## Como volver a ejecutar

Una vez re-guardada la API key:

```powershell
pwsh -File tools\test-agent-fuxion.ps1
```

El script imprime para cada pregunta: enunciado, respuesta truncada a 300 caracteres, tokens IN/OUT y tiempo de respuesta.
