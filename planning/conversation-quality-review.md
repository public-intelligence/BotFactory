# Revisión de calidad conversacional — VOZ

**Fecha del análisis:** 2026-07-19 (~02:15 UTC) · **Corpus:** conversaciones Id ≥ 590 · **Base:** VOZ-DB (SQL Server dev remoto, solo lectura)

## Resumen ejecutivo

Se reconstruyó la conversación 599 turno a turno y se agregaron métricas sobre las conversaciones 590–600 (excluyendo las bajas conocidas 593 y 596). Los tres hallazgos principales:

1. **La herramienta `clima_actual` funciona, pero la experiencia es mala en 8 de 9 usos.** Weatherstack resuelve `query=San José` como **San Marino** (el país europeo) en el 100 % de los casos. El modelo reaccionó de formas distintas y casi todas malas: presentó datos de San Marino como si fueran de San José (3 veces), se quedó mudo o esquivó la pregunta (3 veces, incluida la única respuesta `fallback` del corpus), rechazó datos correctos (1 vez) e ignoró el resultado (1 vez). Solo 1 de 9 llamadas terminó en una respuesta correcta y bien atribuida.
2. **Cuando la base de conocimiento no cubre un tema, el bot entra en bucle de aclaración en vez de admitirlo.** En la 599, el usuario dijo "licencias comerciales" y el bot respondió con la misma pregunta aclaratoria **cuatro turnos seguidos**, arrastrando al usuario hacia el contenido que sí tiene (telecomunicaciones) hasta que este se rindió. El `GroundedOnlyMessage` existe pero nunca se disparó: la búsqueda vectorial siempre devuelve 5 resultados por debajo del umbral de distancia (0.5), aunque sean irrelevantes.
3. **La latencia percibida en voz es ~4.2 s de mediana, y las herramientas no son el problema.** El desglose del pipeline: detección de fin de habla (EOU) ~1.7 s fijos + TTFT del LLM ~2.3 s + TTS ~0.17 s. Los segmentos de herramienta aportan solo el 12–16 % del tiempo de turno; lo que domina son las pasadas de Gemini (una de ellas llegó a 7.4 s tras un tool call, produciendo una no-respuesta de 48 caracteres).

Las recomendaciones priorizadas están en la sección 4; las de mayor impacto son de configuración (AiUsageNotes de `clima_actual`, MasterPrompt) y no requieren cambios de código.

> **Actualización 2026-07-19:** la §2.8 documenta la primera validación con la conversación 602, ya con los prompts mejorados: tres arreglos confirmados en producción de prueba, dos hallazgos nuevos (fuga del nombre «Jean» desde los ejemplos del MasterPrompt; el geocoding de weatherstack sigue fallando incluso con «Ciudad, País») y una recurrencia agravada del patrón de descarte post-herramienta. Varias recomendaciones fueron aplicadas (config) o implementadas (backend) ese mismo día — el estado por recomendación está al final de la §4. La §2.8 también corrige una afirmación errónea de la primera versión de este informe sobre el sonido de «pensando».

---

## 1. Alcance y metodología

- **Ventana de datos:** conversaciones `Id >= 590` (2026-07-19 00:42 – 02:04 UTC, hora local UTC-4). Organización única (`OrganizationId='1'`); toda la actividad con turnos ocurrió en el bot 2 ("Pura Vida IA"); el bot 16 solo registró sesiones vacías (saludo y cierre por timeout).
- **Exclusiones:** 593 y 596 quedan fuera de toda métrica (bajas conocidas del artefacto de entorno: error de clave de cifrado que rompía Gemini y `clima_actual`, e ICE de LiveKit roto, corregidos ~01:30 UTC del 2026-07-19). Los errores `credentials_error` previos a esa hora se listan como ruido de entorno, no como señal UX. La 596 se cita **solo cualitativamente** (patrón de fijación tras fallo de herramienta). La 595 (01:20–01:22, dentro de la ventana del artefacto) se incluye en las métricas pero sus dos turnos vacíos se discuten aparte (§2.4).
- **Modalidad:** no existe columna de modalidad; se infirió como voz = la conversación tiene eventos con `Source=0` (agente). Resultado: **todos los turnos del corpus son de voz**; no hubo turnos de texto en la ventana (el único fue de la 593, excluida). Por eso no se reporta el split voz/texto pedido: no hay población de texto que comparar.
- **Fuente autoritativa de outcome y tiempos:** `telemetry_events.EventName='backend_completed'` (`Data`: `ttftMs`, `totalMs`, `outcome`, `toolCount`, `charCount`, `segments`). Se cruzó contra `messages.Provider` sin discrepancias (§2.4). `messages` no tiene `TurnId`; el emparejamiento mensaje↔turno se hizo por orden temporal y se verificó con `messages.LatencyMs == totalMs` (coincidencia exacta en los 12 turnos de la 599).
- **Umbrales declarados para etiquetar calidad:** "lenta" = total > 3 500 ms o TTFT > 1 500 ms (voz); "casi vacía" = `charCount` < 40; "vacía" = 0.
- **Semántica del umbral de KB (verificada en código):** `bots.SimilarityThreshold` se pasa a `KbSearchService` como **distancia coseno máxima** (`WHERE Distance < {maxDistance}`, [KbSearchService.cs:57]); menor distancia = más relevante. El nombre de la columna es engañoso: no es un piso de similitud sino un techo de distancia.
- Todas las horas en UTC. Consultas ejecutadas vía sqlcmd del contenedor Docker local contra el servidor remoto; el SQL de cada cifra está al pie de su subsección o en el apéndice (§5).

---

## 2. Hechos medidos

### 2.1 Conversación 599 turno a turno

Contexto: bot 2, **acceso público** (`AccessMode='public'`), voz, 01:45:17–01:49:44 UTC, cerrada por `timeout`. 13 mensajes de usuario, 12 turnos backend completados + 1 saludo + 1 turno cancelado por barge-in. Usuario se identifica como "Domingo".

| # | Fin (UTC) | Pedido del usuario (cita) | Acción | TTFT | Total | ms tool (%) | Outcome | Calidad |
|---|---|---|---|---|---|---|---|---|
| 1 | 01:45:30 | «Hola.» | directa | 571 | 641 | — | ok | buena |
| 2 | 01:45:38 | «Domingo.» | directa | 715 | 812 | — | ok | buena |
| 3 | 01:45:48 | «¿Sobre qué servicios te das información?» | directa | 1 099 | 1 165 | — | ok | buena |
| 4 | 01:46:10 | «¿Cómo puedo sacar una licencia?» | directa | 882 | 945 | — | ok | buena (aclara tipo) |
| 5 | 01:46:26 | «Licencias comerciales.» | kb_search | 2 838 | 2 956 | 471 (16 %) | ok | **mala: repite la aclaración ya respondida** |
| 6 | 01:46:45 | «Una licencia comercial.» | kb_search | 2 321 | 2 439 | 346 (14 %) | ok | **mala: bucle, 2.ª vez** |
| 7 | 01:47:07 | «Telecomunicaciones.» | kb_search | 3 057 | 3 416 | 339 (10 %) | ok | regular: da info institucional pero re-pregunta (3.ª vez) |
| 8 | 01:47:37 | «Sería para radiodifusión.» | kb_search | 1 753 | 2 204 | 322 (15 %) | ok | **mala: enumera permisos y re-pregunta (4.ª vez), nunca responde** |
| — | 01:48:12 | «No, mejor ayúdame buscando información del pasaporte. ¿Qué me puedes decir de--» | kb_search (turno cancelado por barge-in del propio usuario al completar la frase) | — | — | — | cancelado | — |
| 9 | 01:48:15 | «el pasaporte para menores?» | kb_search | 2 061 | 2 479 | 372 (15 %) | ok | buena (integra las dos mitades de la frase partida) |
| 10 | 01:48:48 | «No, mejor dime cómo estás-» | directa | 854 | 915 | — | ok | buena |
| 11 | 01:49:21 | «Dime cómo está el clima en San José, por favor.» | clima_actual (Success, 200) | **8 908** | **8 972** | 660 (7 %) | ok | **crítica: 9 s de espera y responde «Domingo, ¿hay algo más en lo que pueda ayudarte?» — descarta el resultado exitoso** |
| 12 | 01:49:33 | «Dime cómo está el clima en San José, por favor.» (repetida textual) | clima_actual (Success, 200) | 1 751 | 1 931 | 256 (13 %) | ok | **respuesta fluida pero factualmente errónea: presenta datos de San Marino como San José** (§2.6) |

Notas medidas del turno 11: el desglose de `segments` es gemini 631 ms → tool 660 ms → **gemini 7 431 ms (TTFT 7 367 ms)** para producir 48 caracteres. La herramienta no fue el cuello de botella. Del lado del agente, `turn_completed` registró `llmTtftMs=9389`; con EOU 1 688 ms, el usuario esperó ~11 s sin respuesta hablada. *(Corrección — ver §2.8: la primera versión de este informe afirmó que no había sonido de «pensando»; en realidad el cliente web reproduce uno por defecto cuando `EnableTypingSound=1`, y `ThinkingSoundBlobKey=NULL` solo significa que no hay sonido personalizado.)*

Bucle de licencias (turnos 5–8): las cuatro búsquedas KB devolvieron 5 chunks cada una, todos de espectro radioeléctrico/telecomunicaciones de la institución (mejor distancia 0.30–0.37) porque la KB **no contiene nada sobre licencias comerciales** (patentes municipales). El bot nunca lo dijo; en su lugar repitió variantes de «¿a qué tipo de licencia te refieres?» y ofreció como opciones lo que su KB sí tiene, hasta que el usuario cambió de tema («No, mejor ayúdame buscando información del pasaporte»).

Señales de voz de la 599: **15 barge-ins** (frente a 2–6 en las demás), 1 `turn_cancelled`, EOU por turno 1 618–1 813 ms, `ttsTtfbMs` estable ~170–190 ms. La frase del usuario sobre pasaportes se partió en dos turnos (EOU disparó en la pausa de «...decir de--»); el turno partido se canceló y el siguiente respondió bien con ambas mitades en contexto.

```sql
-- Esqueleto de turnos (misma consulta con OPENJSON($.segments) para el waterfall)
SELECT ROW_NUMBER() OVER (ORDER BY t.OccurredAt) AS turno, t.TurnId,
  JSON_VALUE(t.Data,'$.ttftMs') AS ttft, JSON_VALUE(t.Data,'$.totalMs') AS total,
  JSON_VALUE(t.Data,'$.outcome') AS outcome, JSON_VALUE(t.Data,'$.toolCount') AS tools,
  JSON_VALUE(t.Data,'$.charCount') AS chars
FROM telemetry_events t
WHERE t.ConversationId=599 AND t.EventName='backend_completed' ORDER BY t.OccurredAt;
-- Transcripción: messages (Role IN ('User','Assistant')) ORDER BY Id;
-- Voz: telemetry_events EventName='turn_completed' (sttMs/eouMs/llmTtftMs/ttsTtfbMs/ttsMs)
-- Tools: tool_executions WHERE ConversationId=599 AND IsTest=0
```

### 2.2 Latencia agregada (n=32 turnos; todos de voz)

| Métrica (backend) | p50 | p95 | máx |
|---|---|---|---|
| TTFT (`ttftMs`) | 1 753 ms | 4 203 ms | 8 908 ms |
| Total de turno (`totalMs`) | 1 849 ms | 4 309 ms | 8 972 ms |

Descomposición de la latencia **percibida** (lado agente, `turn_completed`, sobre los turnos con métrica presente):

| Etapa | n | p50 | p95 |
|---|---|---|---|
| EOU (fin de habla → commit) | 26 | 1 704 ms | 1 891 ms |
| LLM TTFT (vista del agente) | 20 | 2 297 ms | 4 728 ms |
| TTS TTFB | 21 | 173 ms | 214 ms |

Suma de medianas ≈ **4.2 s** entre que el usuario calla y empieza a oír la respuesta. Dos observaciones medidas: (a) el EOU es un costo casi constante de ~1.7 s en todos los turnos — muy por encima de `SilenceEndMs=700` del bot; el componente dominante es `transcriptionDelayMs` (~1.6–1.8 s), es decir, la espera del transcript final de STT/detector de turno, no el silencio configurado; (b) el TTFT backend (p50 1 753) vs el TTFT visto por el agente (p50 2 297) implica ~400–500 ms de transporte/encolado entre agente y backend.

```sql
WITH turns AS (
  SELECT TRY_CAST(JSON_VALUE(t.Data,'$.ttftMs') AS float) AS ttft,
         TRY_CAST(JSON_VALUE(t.Data,'$.totalMs') AS float) AS total
  FROM telemetry_events t JOIN conversations c ON c.Id=t.ConversationId
  WHERE t.EventName='backend_completed' AND c.Id>=590 AND c.Id NOT IN (593,596) AND c.OrganizationId='1')
SELECT DISTINCT COUNT(*) OVER () AS n,
  PERCENTILE_CONT(0.5)  WITHIN GROUP (ORDER BY ttft)  OVER () AS p50_ttft,
  PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY ttft)  OVER () AS p95_ttft,
  PERCENTILE_CONT(0.5)  WITHIN GROUP (ORDER BY total) OVER () AS p50_total,
  PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY total) OVER () AS p95_total FROM turns;
-- Waterfall del agente: igual, sobre EventName='turn_completed' con $.eouMs/$.llmTtftMs/$.ttsTtfbMs
```

### 2.3 Herramientas: frecuencia y contribución a la latencia

- 19 de 32 turnos (59 %) usaron exactamente una herramienta; 13 ninguna. **Ningún turno llegó al tope `Gemini:MaxToolIterations=2`**, y ningún turno repitió herramienta internamente.
- Ninguna ejecución alcanzó `MaxCallsPerConversation` (10 para `clima_actual`) ni produjo bloqueos de política; `RetryCount=0` en todas.

| Segmento | n | p50 ms | p95 ms | máx | % del turno (p50 / p95) |
|---|---|---|---|---|---|
| gemini (pasada de inferencia) | 51 | 864 | 1 836 | **7 431** | — |
| kb_search | 10 turnos | 515 | 758 | 875 | 16 % / 25 % |
| clima_actual | 9 turnos | 256 | 1 469 | 1 470 | 12 % / 37 % |

Lectura directa: las herramientas son baratas; el costo de un turno con herramienta (p50 2.3–2.7 s vs ~0.9 s directo) proviene de hacer **dos pasadas de Gemini** (pre y post tool), no de la herramienta.

```sql
-- Distribución por segmento (CROSS APPLY OPENJSON(Data,'$.segments')) y
-- participación = SUM(ms tool)/totalMs por turno; ver apéndice §5.3
```

### 2.4 Resultados de turno y respuestas vacías

| Outcome (`backend_completed`) | n | % |
|---|---|---|
| ok | 31 | 96.9 % |
| fallback | 1 | 3.1 % |
| grounded-only | 0 | 0 % |
| partial (persistido) | 0 | 0 % |

Verificación cruzada con `messages.Provider` sobre las mismas conversaciones: 31 `gemini`, 1 `gemini-fallback`, 9 `greeting` (saludos, uno por sesión incluidas las vacías) — **sin discrepancias**.

- **El único fallback** fue en la 600 (02:04:06): el usuario preguntó «¿Y en San José cómo está?», `clima_actual` respondió Success/200 en 59 ms, y el modelo no generó texto → el backend habló el `FallbackMessage` («Lo siento, no pude procesar eso.»). Es decir: el fallback del corpus **no fue causado por un fallo de herramienta sino por el patrón San José→San Marino** (§2.6).
- **Turnos vacíos con outcome "ok"**: la 595 tiene dos turnos consecutivos con `charCount=0`, `toolCount=0`, `outcome='ok'`, `Provider='gemini'` (01:22:18 y 01:22:30, tras «como esta el clima en san jose» y «?»). El usuario oyó **silencio dos veces, sin mensaje de disculpa**. *Verificación de código posterior (`git blame`):* esos turnos son **anteriores al commit `67aff8c`** (2026-07-18 21:50 local), que añadió el guard «buffer vacío → FallbackMessage» en `ChatTurnService.cs:341` — el bug ya estaba corregido en HEAD cuando ocurrió el análisis; son datos históricos del código previo. Quedaba un hueco residual (salida de **solo espacios en blanco** evadía el guard, que comparaba `buffer.Length == 0`), cerrado el 2026-07-19 junto con este informe (§4-R6).
- Respuestas "casi vacías" (<60 chars) legítimas: saludos/preguntas de nombre (45–56 chars) — correctas. La excepción es la no-respuesta de 48 chars del turno 11 de la 599, ya descrita.
- **`GroundedOnlyMessage` jamás se disparó:** las 12 ejecuciones de `kb_search` del corpus devolvieron 5 resultados cada una (nunca lista vacía) con el techo de distancia actual (0.5).

```sql
SELECT JSON_VALUE(t.Data,'$.outcome') AS outcome, COUNT(*) AS n
FROM telemetry_events t JOIN conversations c ON c.Id=t.ConversationId
WHERE t.EventName='backend_completed' AND c.Id>=590 AND c.Id NOT IN (593,596) AND c.OrganizationId='1'
GROUP BY JSON_VALUE(t.Data,'$.outcome');
-- Vacías: ... AND TRY_CAST(JSON_VALUE(t.Data,'$.charCount') AS int)=0
```

### 2.5 Calidad de la recuperación KB (distancias medidas)

Mejor distancia coseno (mínimo de los 5 resultados) por búsqueda — recordar: **menor = más relevante**, y el filtro actual es `Distance < 0.5`:

| Conv | Query | Mejor distancia | ¿La KB cubre el tema? |
|---|---|---|---|
| 595/596/597/598/600 | variantes de "pasaporte (menores/renovación/costos)" | **0.243 – 0.296** | sí — respuestas correctas |
| 599 | «licencias comerciales» / «licencia comercial» | **0.361 / 0.365** | **no** — devolvió espectro radioeléctrico |
| 599 | «licencia comercial de telecomunicaciones» | 0.300 | sí (contenido institucional) |
| 599 | «licencia comercial de radiodifusión» | 0.334 | parcial (trámites radiodifusión) |
| 599 | «pasaporte» / «pasaporte para menores» | 0.290 / 0.250 | sí |

En este corpus, las consultas cuyo tema **sí** está en la KB quedaron en ≤ 0.33 y las dos genuinamente sin cobertura en ≥ 0.36. Un techo de distancia ~0.35 habría convertido exactamente esas dos búsquedas en "sin resultados" → ruta `GroundedOnlyMessage` (honestidad automática), sin afectar ninguna consulta bien cubierta. n=12 búsquedas: separación limpia pero muestra pequeña.

```sql
SELECT t.ConversationId, JSON_VALUE(t.Data,'$.payload.query') AS query,
  (SELECT MIN(TRY_CAST(JSON_VALUE(r.value,'$.distance') AS float))
     FROM OPENJSON(t.Data,'$.payload.results') r) AS best_dist
FROM telemetry_events t
WHERE t.ConversationId>=590 AND t.EventName='kb_retrieved' ORDER BY t.ConversationId, t.OccurredAt;
```

### 2.6 `clima_actual`: ejecuciones vs experiencia real

Las 9 ejecuciones post-fix fueron `Success` HTTP 200 (latencia 59–1 327 ms, sin reintentos). Pero la **atribución geográfica** del proveedor y la reacción del modelo convirtieron ese 100 % técnico en ~11 % de experiencia correcta:

| Conv/turno | Args del modelo | `lugar` devuelto por la API | Qué hizo el bot con el resultado |
|---|---|---|---|
| 598 · 01:42:47 | `{"lugar":"San José"}` | **San Marino** | Lo ignoró: respondió «¿cómo te llamas?» (ritual del nombre; §2.7) |
| 598 · 01:42:55 | `{"lugar":"San José"}` | **San Marino** | **Presentó los datos como San José** (factualmente erróneo) |
| 598 · 01:43:32 | `{"lugar":"Santo Domingo"}` | Santo Domingo ✔ | **Rechazó datos correctos**: «No pude obtener el clima para "Domingo"» |
| 598 · 01:43:51 | `{"lugar":"Santo Domingo"}` | Santo Domingo ✔ | ✔ Única respuesta correcta y bien atribuida |
| 599 · 01:49:13 (t11) | `{"lugar":"San José"}` | **San Marino** | Esquivó tras 9 s: «¿hay algo más en lo que pueda ayudarte?» |
| 599 · 01:49:32 (t12) | `{"lugar":"San José"}` | **San Marino** | **Presentó los datos como San José** |
| 600 · 02:03:45 | `{"lugar":"San José"}` | **San Marino** | Dijo literalmente «El clima actual en **San Marino**…» (a un usuario que preguntó por San José de Costa Rica) |
| 600 · 02:04:05 | `{"lugar":"San José"}` | **San Marino** | No generó texto → **fallback** «Lo siento, no pude procesar eso.» |
| 600 · 02:04:18 | `{"lugar":"San José de Costa Rica"}` | **San Ignacia De Acosta** (≈ San Ignacio de Acosta, provincia de San José) | Presentó los datos como "San José" (región correcta, etiqueta incorrecta) |

Datos duros adicionales: los payloads de resultado (redactados) están en `tool_executions.ResultSnippetJson`; ejemplo literal del turno 11 de la 599: args `{"lugar":"San José"}` → `{"lugar":"San Marino","temperatura_c":24,"condicion":"Clear","humedad_pct":89,"sensacion_c":26}`. Las repeticiones de llamada con los mismos argumentos en turnos consecutivos (599 t11→t12, 598, 600) fueron **el usuario repitiendo la pregunta tras una no-respuesta**, no bucles del modelo dentro de un turno.

Además, el `AiUsageNotes` vigente solo cubre el caso `temperatura_c` vacía; no dice nada sobre qué hacer cuando el `lugar` devuelto no coincide con el pedido — exactamente el caso que ocurrió 7 de 9 veces.

```sql
SELECT te.ConversationId, te.TurnId, te.Outcome, te.HttpStatus, te.LatencyMs,
  te.ArgumentsJson, LEFT(te.ResultSnippetJson,200) AS result
FROM tool_executions te
WHERE te.ConversationId>=590 AND te.IsTest=0 AND te.ToolName='clima_actual'
ORDER BY te.CreatedAt;
```

### 2.7 Comportamiento tras fallos y fricciones de guion

- **Fijación tras fallo (conv 596, cualitativo — ventana del artefacto, fuera de métricas).** Con `clima_actual` devolviendo `credentials_error` (3 intentos), el bot encadenó: «No pude obtener el clima de San José…» → usuario: «y en santo domingo» → bot: «no pude obtener el clima **de San José**…» (ni siquiera actualizó la ciudad) → usuario cambia de tema: «hablame del servicio de pasaporte, para menores» → el bot **ejecutó kb_search con buenos resultados (distancia 0.257)** y aun así respondió: «el servicio para consultar el clima no está funcionando en este momento». Tres turnos de fijación, incluyendo uno donde ignoró información correcta ya recuperada del nuevo tema.
- **El ritual del nombre bloquea la primera respuesta.** El MasterPrompt ordena obtener el nombre al inicio. Cuando el usuario abre con una pregunta real, el bot la pospone: 596 («como esta el clima…» → «¿cómo te llamas?») y 598 (ídem, y además **ejecutó la llamada a clima_actual de 1 327 ms, descartó el resultado y tardó 4 065 ms en preguntar el nombre**). En la 598 el saludo posterior quedó incoherente: «Mucho gusto, Domingo. Hola de nuevo. El clima…».
- **Barge-ins:** 29 en total en 5 conversaciones; la 599 concentra 15. Todos los turnos del bot terminan con una pregunta de seguimiento («¿Te gustaría saber…?», «¿Hay algo más…?»), lo que invita a superposiciones, y las esperas largas (turno 11) provocan que el usuario hable encima.
- **Positivo (funciona bien):** el formato de voz del MasterPrompt se cumple consistentemente — montos en palabras («setenta y cinco dólares… doscientos cincuenta colones»), cuentas dígito a dígito («dos cinco cuatro ocho nueve seis…»), temperaturas («veinticuatro grados Celsius»). La recuperación de la frase partida en la 599 (turnos cancelado→9) fue limpia. El emparejamiento mensajes↔telemetría cuadró exacto (12/12 con `LatencyMs==totalMs`).

### 2.8 Conversación 602 — primera validación de los cambios (actualización 2026-07-19)

Contexto: bot 2, voz, **autenticada**, 02:48:20–02:51:07 UTC, cerrada por `user_ended`. Es la primera conversación con la configuración mejorada activa: el MasterPrompt v2 se guardó a las 02:41:29 y las notas de `clima_actual` a las 02:48:10 — diez segundos antes de iniciar. 6 turnos backend, todos `outcome='ok'`.

| # | Pedido del usuario (cita) | Acción | TTFT | Total | Resultado |
|---|---|---|---|---|---|
| 1 | «Por favor, deme cómo traerme a San José.» | directa | 1 119 | 1 128 | buena — responde de inmediato, **sin pedir el nombre primero** |
| 2 | «Costa Rica.» | directa | 1 376 | 1 620 | buena — pide precisión razonable |
| 3 | «…dime cómo está el clima en San José.» | clima_actual (207 ms) | 1 291 | 1 640 | fluida y rápida, pero la API devolvió `lugar="San Andres"` y lo presentó como San José |
| 4 | «¿Cómo está el clima en **Miami, Florida**?» | clima_actual **×2** (tope `MaxToolIterations=2`) | **13 020** | **13 028** | **crítica** — datos correctos de Miami (200 OK) descartados; re-consultó San José y respondió «**Ya te había comentado** que el clima actual en San José…» — tema equivocado y tono de regaño; espera percibida ~14.3 s (`llmTtftMs=13 418`, EOU 728) |
| 5 | «¿Cómo está el clima en Miami, Florida?» (repetida) | clima_actual (358 ms) | 1 546 | 1 737 | respondió sobre Miami, pero con args `"Miami, Estados Unidos"` la API resolvió `lugar="Estados Unidos"` (25 °C despejado — contradice el Miami real de un minuto antes: 29 °C parcialmente nublado) |
| 6 | «¿Cómo saco un pasaporte en Costa Rica?» | kb_search (390 ms, dist 0.234) | 2 475 | 2 697 | buena — cambio de tema limpio, sin arrastrar el fiasco del clima |

**Confirmaciones (los cambios de prompt funcionan):**
1. *Ritual del nombre no bloqueante (R4):* el usuario abrió con un pedido concreto y fue atendido de inmediato — la misma apertura en 596/598 recibía «¿cómo te llamas?».
2. *Argumentos de `clima_actual` (R1):* las 4 llamadas llevaron «Ciudad, País/Estado» (`San José, Costa Rica`, `Miami, Florida`, `Miami, Estados Unidos`); antes iba la ciudad sola el 100 % de las veces.
3. *Anti-fijación (R3):* tras el desastre del turno 4, la transición a pasaporte fue limpia («Veo que ahora te interesa el pasaporte…») — en la 596 esa transición fracasó tres turnos seguidos.
4. Los turnos sin incidente quedaron en 1.1–2.7 s de total backend.

**Hallazgos nuevos:**
1. **Fuga del nombre «Jean» desde los ejemplos del prompt.** El bot llamó al usuario «Jean» dos veces (turnos 4 y 5); el usuario jamás dio su nombre y `conversations.UserId` es un GUID sin nombre. «Jean» solo existía en los ejemplos del MasterPrompt («Mucho gusto, Jean»…). → R9.
2. **El geocoding de weatherstack sigue roto incluso con «Ciudad, País»:** `"San José, Costa Rica"` → `"San Andres"` (2 veces); `"Miami, Estados Unidos"` → `"Estados Unidos"` (resolución a nivel país, con datos inconsistentes); solo `"Miami, Florida"` resolvió bien. Y el modelo **no aplicó la verificación de coincidencia** en los casos ambiguos — comparar solo nombres de lugar es difuso para él. → R1 ampliada (exponer país/región en el mapping; normalización de tildes determinista en backend).
3. **El patrón «pasada post-tool lenta + descarte» reapareció agravado** (turno 4): mismo defecto que 599-t11, ahora con una segunda llamada redundante que quemó el tope de iteraciones y una respuesta de tema equivocado. La instrucción «usa el resultado» redujo pero no eliminó el riesgo. → R10 (anclaje determinista en el pipeline).
4. 7 barge-ins en 6 turnos — tasa por turno similar a la 599.
5. **Corrección de este informe:** el sonido de «pensando» **sí existe y funciona** — el cliente web lo reproduce cuando `EnableTypingSound=1` (bot 2 lo tiene activo) y el agente está en estado *thinking*, con un audio por defecto (`/audio/default-thinking.mp3`) cuando no hay uno personalizado (`chat.ts:626-630`, `thinking-sound.service.ts`). La R7a original («configurar sonido de pensando») queda retirada; la espera del turno 4 no fue silencio absoluto, pero ~14 s de loop de audio sin respuesta siguen siendo una espera inaceptable.

**Config aplicada tras este análisis (verificada en BD):** MasterPrompt v3 (02:57 UTC) — ejemplos con `[nombre]` y regla «si la persona aún no te ha dicho su nombre, no uses ninguno; no inventes uno ni uses los de los ejemplos»; notas de `clima_actual` v3 (10:48 UTC) — añaden «No envíes el nombre de la ciudad o el país con tildes o acentos», «Haz una sola llamada por pregunta. Si ya recibiste un resultado exitoso, úsalo de inmediato. Responde siempre sobre el lugar indicado en el último mensaje de la persona»; `ResponseMappingJson` ahora proyecta `location.country` y `location.region` (nota menor: los alias conservan el punto — «location.country» — conviene renombrarlos a `pais`/`region` para que las notas de uso puedan referirlos con naturalidad). El mismo día se implementaron en backend: guard de turnos vacíos endurecido a whitespace (R6), flag `NormalizeArgDiacritics` por herramienta (R1) y anclaje `{solicitud, resultado}` en el functionResponse (R10).

```sql
-- Mismas consultas de la §2.1 con ConversationId=602 (esqueleto de turnos,
-- segments, tool_executions con ArgumentsJson/ResultSnippetJson, turn_completed,
-- transcripción). Config vigente: tool_definitions Id=1 / bots Id=2 (UpdatedAt).
```

---

## 3. Interpretación

*(Sin cifras nuevas; lectura de los hechos de §2.)*

1. **El problema del clima no es de infraestructura sino de contrato con el proveedor y de instrucciones al modelo.** Weatherstack geocodifica mal "San José" a secas (San Marino) y regular "San José de Costa Rica" (San Ignacio de Acosta). La herramienta devuelve el campo `lugar` resuelto — el dato para detectar el desajuste ya llega al modelo — pero nadie le dice qué hacer con él. El modelo entonces oscila entre los dos errores posibles: presentar datos ajenos como propios (peor caso para un bot gubernamental: información falsa con seguridad) o quedarse callado (fallback/deflexión). La variabilidad de reacción ante el mismo input (9 llamadas, 5 comportamientos distintos) es típica de un caso no cubierto por instrucciones.
2. **El bucle de "licencias comerciales" es el mismo defecto de honestidad con otra máscara.** La cadena RAG nunca produce "no hay resultados" con el techo de distancia actual, así que el camino honesto (`GroundedOnlyMessage`) es código muerto en la práctica; el modelo recibe siempre 5 chunks "usables" y hace lo que hace un LLM con contexto irrelevante: pregunta, rellena y arrastra al usuario hacia lo que sí sabe. La honestidad hay que provocarla por dos vías complementarias: instrucción explícita (MasterPrompt) y filtro de recuperación más estricto (techo de distancia).
3. **La latencia percibida se pelea en EOU y en las pasadas de Gemini, no en las herramientas.** ~1.7 s de EOU son un impuesto fijo por turno que domina la sensación de lentitud en respuestas cortas; la mediana de 4.2 s está por encima del presupuesto conversacional razonable (~2–3 s). El outlier de 7.4 s post-tool (599 t11) parece varianza del proveedor (misma llamada en t12: 0.7 s). *(Corregido — §2.8: durante esas esperas sí suena el audio de «pensando» por defecto; un loop de audio mitiga pero no sustituye una respuesta cuando la espera pasa de ~5 s.)*
4. **Los 15 barge-ins de la 599 son síntoma, no causa:** usuario esperando demasiado + bot que siempre re-pregunta = conversación a empujones.
5. **La 595 muestra que el sistema puede fallar en silencio:** dos turnos "ok" de 0 caracteres. *(Matizado tras verificar el código — §2.4: eran datos del código previo al commit `67aff8c`, que ya añadía el guard de buffer vacío; el hueco residual de solo-espacios se cerró el 2026-07-19.)*

---

## 4. Recomendaciones priorizadas

| # | Prioridad | Recomendación | Evidencia | Palanca exacta | Esfuerzo / riesgo |
|---|---|---|---|---|---|
| R1 | **Alta** | Reescribir las instrucciones de `clima_actual`: (a) en `lugar`, exigir ciudad + país («San José, Costa Rica»; si el usuario no da país, asumir Costa Rica); (b) regla obligatoria: *compara el campo `lugar` de la respuesta con el lugar pedido; si no coinciden, dilo explícitamente («encontré datos de X, no de Y») o indica que no pudiste obtenerlo — nunca presentes datos de otro lugar como si fueran del pedido, y nunca respondas vacío tras un resultado exitoso*. Reflejar (a) también en la descripción del parámetro en `ParametersSchemaJson`. | §2.6 (8/9 usos defectuosos; San José→San Marino 6/6) | `tool_definitions.AiUsageNotes`, `AiDescription`, `ParametersSchemaJson` (tdId=1) | Bajo / bajo — solo config; validar con el test console |
| R2 | **Alta** | Añadir al MasterPrompt una regla de honestidad RAG y anti-bucle: *si la búsqueda no devuelve información del tema específico que pidió la persona, dilo en la primera oportunidad y orienta al canal competente; no repitas una pregunta aclaratoria que la persona ya respondió; máximo una aclaración por tema*. | §2.1 turnos 5–8 (misma aclaración 4 veces; usuario abandona el tema) | `bots.MasterPrompt` (bot 2) | Bajo / bajo |
| R3 | **Alta** | Añadir al MasterPrompt una regla anti-fijación tras fallo de herramienta: *si una herramienta falló, discúlpate una sola vez; si la persona cambia de tema o de parámetro, atiende el nuevo pedido y no vuelvas a mencionar el fallo anterior*. | §2.7 (596: 3 turnos fijado en clima, ignoró resultados buenos de pasaporte) | `bots.MasterPrompt` | Bajo / bajo |
| R4 | Media | Suavizar el ritual del nombre: *si la persona abre con una pregunta concreta, respóndela primero y pregunta el nombre después, de forma natural; no pospongas una respuesta ya disponible por pedir el nombre*. | §2.7 (596/598; en 598 costó un tool call descartado y un turno de 4 s) | `bots.MasterPrompt` | Bajo / bajo |
| R5 | Media | Experimento de recuperación: bajar `SimilarityThreshold` (que en el código es **techo de distancia**) de 0.5 a ~0.35 en el bot 2, para que las consultas sin cobertura real devuelvan vacío y activen `GroundedOnlyMessage`. Acompañar con una mejora del mensaje: añadir orientación («…¿quieres que te indique la institución competente?») al texto actual de 62 chars. Medir tasa grounded-only resultante antes de fijarlo. | §2.5 (separación 0.30/0.36 en este corpus; grounded-only jamás disparado), §2.4 | `bots.SimilarityThreshold`, `bots.GroundedOnlyMessage` | Bajo / **medio** — n=12 búsquedas; riesgo de falsos vacíos si el corpus crece; iterar con datos |
| R6 | Media | Cerrar el hueco "turno vacío = ok". **Revisión:** el guard principal ya existía en HEAD (commit `67aff8c`, anterior a este informe; los turnos de la 595 son datos históricos del código previo). El hueco residual real era la salida de **solo espacios en blanco** (`buffer.Length == 0` no la detectaba). **Implementado 2026-07-19:** guard endurecido a whitespace en la ruta normal y en la de excepción, con tests de regresión. | §2.4 (595: dos turnos `charCount=0`, `outcome='ok'`, sin disculpa hablada) | `ChatTurnService.cs:341/382` + tests | Hecho |
| R7 | Media | Presupuesto de latencia de voz: investigar el EOU de ~1.7 s — `SilenceEndMs=700` no es el limitante, el grueso es `transcriptionDelayMs` del STT/detector de turno del agente; revisar la configuración del turn detector antes de tocar umbrales de silencio (bajarlos agravaría los cortes de frase como el de la 599). **La parte (a) original (configurar sonido de «pensando») queda retirada — §2.8: el sonido ya existe y funciona con un audio por defecto.** | §2.2 (EOU p50 1 704 ms; percibida ~4.2 s) | Config del agente de voz (describir; no implementado) | Medio / medio |
| R8 | Baja | Mantener `Gemini:MaxToolIterations=2` y los presupuestos de timeout actuales: en el corpus original ningún turno alcanzó el tope (el turno 4 de la 602 sí lo alcanzó, pero por la re-llamada redundante que R10 ataca — el tope funcionó como red de seguridad, no como problema). | §2.3, §2.8 | (sin cambio) | — |
| R9 | Alta | Ejemplos del MasterPrompt sin nombres propios: sustituir «Jean» por `[nombre]` y regla explícita de no usar nombre alguno si la persona no lo ha dado (evita la fuga observada en la 602). | §2.8 hallazgo 1 | `bots.MasterPrompt` | **Aplicada** (v3, 02:57 UTC) |
| R10 | Alta | Anclaje determinista del resultado de herramienta: el functionResponse que ve el modelo pasa a ser `{solicitud: args, resultado: payload}` — la pregunta y su respuesta quedan adyacentes en el objeto que el modelo lee como "resultado", en vez de depender de que correlacione el functionCall previo. El mensaje Tool persistido no cambia (auditoría intacta). Complementa la regla anti-re-llamada de las notas de uso (ya aplicada). | §2.8 hallazgo 3 (602 t4: Miami correcto descartado), §2.1 t11 | `ChatTurnService.cs` (functionResponse) + test | **Implementada** 2026-07-19 |

**Estado a 2026-07-19 (tras la conv 602 y los cambios de backend):**
- **R1 aplicada y ampliada:** notas de uso v3 (ciudad+país, sin tildes, verificación de coincidencia, anti-re-llamada, responder siempre); `ResponseMappingJson` proyecta ya `location.country`/`location.region` (pendiente menor: renombrar alias a `pais`/`region`); **flag `NormalizeArgDiacritics` implementado en backend** (entidad + migración + conector + admin UI) — pendiente activarlo en clima_actual tras el deploy y re-probar en la consola.
- **R2/R3/R4 aplicadas** (validadas con la 602: apertura respondida sin ritual, cambio de tema limpio; el bucle aclaratorio no se re-observó en esta conversación).
- **R5 pendiente** (experimento de techo de distancia — sin cambios).
- **R6 hecha** (guard endurecido a whitespace + 3 tests de regresión). **R7a retirada; R7b pendiente. R8 sin cambios. R9 aplicada. R10 implementada.**

Orden de ataque restante: deploy backend → activar flag de tildes en clima_actual y re-probar → validar con una conversación nueva de clima (San José y una ciudad extranjera) → R5 → R7b.

---

## 5. Apéndice: reproducibilidad

### 5.1 Entorno de consulta

Servidor SQL dev remoto (`ConnectionStrings__BotFactoryDb` en `apps/backend/BotFactory.Api/.env`), base `VOZ-DB`, consultado con el sqlcmd del contenedor local:

```bash
MSYS_NO_PATHCONV=1 docker compose -f docker-compose.dev.yml exec -T sqlserver \
  /opt/mssql-tools18/bin/sqlcmd -S <server> -d VOZ-DB -U <user> -P <pass> \
  -C -I -W -s '|' -w 65535 -Q "SET NOCOUNT ON; <query>"
# Para textos largos (Content, Data, prompts): sustituir -W por -y 0 (son incompatibles entre sí)
```

Solo SELECT; nunca se leyeron `app_settings.EncryptedApiKey` ni `tool_connections.EncryptedSecretsJson`.

### 5.2 Notas de esquema que condicionan las consultas

- Tablas snake_case, columnas PascalCase. `telemetry_events.Source` es int (0=agente, 1=backend). Enums string en PascalCase (`Outcome='Success'`), salvo `messages.Provider` y `conversations.AccessMode/Surface` (minúsculas literales).
- `messages` no tiene `TurnId`: emparejar por orden temporal y verificar `messages.LatencyMs == JSON_VALUE(backend_completed.Data,'$.totalMs')`.
- `tool_executions.IsTest=1` (consola de pruebas) excluido siempre.
- `kb_search` no escribe en `tool_executions`; su evidencia es el evento `kb_retrieved` (`$.payload.query`, `$.payload.results[].distance`).
- `bots.SimilarityThreshold` = techo de distancia coseno (`WHERE Distance < umbral`), no piso de similitud.

### 5.3 Consultas agregadas restantes

```sql
-- Distribución de toolCount por turno
SELECT TRY_CAST(JSON_VALUE(t.Data,'$.toolCount') AS int) AS toolCount, COUNT(*) AS n
FROM telemetry_events t JOIN conversations c ON c.Id=t.ConversationId
WHERE t.EventName='backend_completed' AND c.Id>=590 AND c.Id NOT IN (593,596) AND c.OrganizationId='1'
GROUP BY TRY_CAST(JSON_VALUE(t.Data,'$.toolCount') AS int);

-- Estadística por segmento y participación de la herramienta en el total del turno
WITH segs AS (
  SELECT t.Id AS evId, TRY_CAST(JSON_VALUE(t.Data,'$.totalMs') AS float) AS total,
    SUM(CASE WHEN JSON_VALUE(s.value,'$.type')='tool'
             THEN TRY_CAST(JSON_VALUE(s.value,'$.ms') AS float) ELSE 0 END) AS tool_ms,
    MAX(CASE WHEN JSON_VALUE(s.value,'$.type')='tool' THEN JSON_VALUE(s.value,'$.name') END) AS tool_name
  FROM telemetry_events t CROSS APPLY OPENJSON(t.Data,'$.segments') s
  JOIN conversations c ON c.Id=t.ConversationId
  WHERE t.EventName='backend_completed' AND c.Id>=590 AND c.Id NOT IN (593,596) AND c.OrganizationId='1'
  GROUP BY t.Id, TRY_CAST(JSON_VALUE(t.Data,'$.totalMs') AS float))
SELECT DISTINCT tool_name, COUNT(*) OVER (PARTITION BY tool_name) AS n,
  PERCENTILE_CONT(0.5)  WITHIN GROUP (ORDER BY tool_ms/NULLIF(total,0)) OVER (PARTITION BY tool_name) AS p50_share,
  PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY tool_ms/NULLIF(total,0)) OVER (PARTITION BY tool_name) AS p95_share
FROM segs WHERE tool_ms>0;

-- Señales UX de voz por conversación
SELECT t.ConversationId, t.EventName, COUNT(*) AS n
FROM telemetry_events t
WHERE t.ConversationId>=590 AND t.EventName IN
  ('barge_in','turn_cancelled','stt_failed','tts_failed','agent_error','llm_error')
GROUP BY t.ConversationId, t.EventName;

-- Cerca del artefacto: fallos de herramienta con etiqueta de ventana
SELECT te.ConversationId, te.ToolName, te.Outcome, te.ErrorCategory, te.CreatedAt,
  CASE WHEN te.CreatedAt < '2026-07-19T01:30:00' THEN 'ventana-artefacto' ELSE 'post-fix' END AS ventana
FROM tool_executions te
WHERE te.ConversationId>=590 AND te.IsTest=0 AND te.Outcome<>'Success' ORDER BY te.CreatedAt;
-- Resultado: los únicos fallos son 3 credentials_error de la 596 (ventana-artefacto). Post-fix: 0 fallos.
```

### 5.4 Limitaciones

- Muestra pequeña: 32 turnos, 5 conversaciones con actividad, 1 bot, ~80 minutos de ventana; los percentiles p95 se basan en pocas observaciones y las separaciones de distancia KB (§2.5) deben revalidarse con más tráfico.
- Sin turnos de texto en la ventana: el contraste voz/texto pedido no fue computable.
- La 595 está parcialmente dentro de la ventana del artefacto; sus dos turnos vacíos podrían tener causa ambiental, aunque el defecto de clasificación (ok con 0 chars) es observable independientemente de la causa.
- La conversación 600 estaba `Active` al momento del análisis (snapshot 02:15 UTC).
