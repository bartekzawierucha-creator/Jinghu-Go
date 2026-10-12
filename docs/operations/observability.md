# Obserwowalność

**Jest (Etap 1–2):**
- `GET /health/live` (proces żyje), `GET /health/ready` (magazyn odpowiada w ≤ 2 s).
- Logi strukturalne JSON (UTC), scope `CorrelationId` na każde żądanie; nagłówek `X-Correlation-Id` (przyjmowany od klienta jeśli `[A-Za-z0-9_-]{1,64}`, inaczej TraceId) zwracany w odpowiedzi i w każdym problem+json oraz wpisie audytu.
- Log żądania: metoda, ścieżka (bez query), status, czas w ms. Nie logujemy: ciał żądań, tokenów, haseł, lokalizacji.
- Metryki HTTP z wbudowanych `Meter`ów ASP.NET Core (`Microsoft.AspNetCore.Hosting`, `…Server.Kestrel`, `…RateLimiting`) — gotowe do zebrania przez OpenTelemetry/`dotnet-counters`; eksporter OTLP dojdzie, gdy dostępny będzie NuGet.

**Planowane:** eksport OTLP (Etap 3), metryki outboxa i workera (Etap 3), alert ekonomiczny = niepusty widok `characters.destiny_ledger_mismatches` (od wdrożenia adaptera PostgreSQL).
