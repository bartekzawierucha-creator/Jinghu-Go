# Obserwowalność

**Jest (Etap 1–2):**
- `GET /health/live` (proces żyje), `GET /health/ready` (magazyn odpowiada w ≤ 2 s).
- Logi strukturalne JSON (UTC), scope `CorrelationId` na każde żądanie; nagłówek `X-Correlation-Id` (przyjmowany od klienta jeśli `[A-Za-z0-9_-]{1,64}`, inaczej TraceId) zwracany w odpowiedzi i w każdym problem+json oraz wpisie audytu.
- Log żądania: metoda, ścieżka (bez query), status, czas w ms. Nie logujemy: ciał żądań, tokenów, haseł, lokalizacji.
- Metryki HTTP z wbudowanych `Meter`ów ASP.NET Core (`Microsoft.AspNetCore.Hosting`, `…Server.Kestrel`, `…RateLimiting`) — gotowe do zebrania przez OpenTelemetry/`dotnet-counters`; eksporter OTLP dojdzie, gdy dostępny będzie NuGet.

- **Outbox/Worker (Etap 3):** `Meter` `JianghuGo.Outbox`: `jianghu.outbox.processed`, `.failed`, `.dead_lettered` (z tagiem `type`), histogram `jianghu.outbox.lag` [s]. Readiness `outbox`: `Degraded`, gdy najstarsze oczekujące zdarzenie ma > 5 min lub istnieje dead letter.
- Worker loguje w JSON; nieudane handlery z typem, id i numerem próby.

**Planowane:** eksporter OTLP (wymaga paczek OpenTelemetry), alert ekonomiczny = niepusty widok `characters.destiny_ledger_mismatches` (zapytanie okresowe Workera).
