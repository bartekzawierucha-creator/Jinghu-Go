# Architektura — przegląd

**Modularny monolit** (ASP.NET Core, .NET 10) z autorytatywną logiką gry. Jeden proces API; moduły komunikują się
wyłącznie przez jawne kontrakty. Granice są egzekwowane testem `ArchitectureTests` na skompilowanych assembly.

```
Unity client (Presentation → Core: Application/Infrastructure)
        │  HTTPS, JSON, Bearer (opaque token), Idempotency-Key
        ▼
Api (composition root: middleware, auth, rate limiting, health, problem+json)
        │
Modules ─ Identity ─ Characters ─ Cultivation ─ Notifications ─ (Combat, … kolejne etapy)
        │                    └── zdarzenia → platform.outbox_messages ──► Worker (OutboxDispatcher) ──► handlery modułów
Infrastructure (unit of work SERIALIZABLE, idempotency, audit, outbox; PostgreSQL / in-memory)
        │
Application (abstrakcje: IUnitOfWork, Idempotency, IAuditLog)  ←  Domain (Result/Error, typed IDs, DeterministicRandom)
```

## Zasady przekrojowe

| Zasada | Implementacja |
|---|---|
| Serwer jest autorytetem | Klient wysyła intencje (`expectedVersion`, `lockedCategories`, nazwa). Nieznane pola JSON → 400 (`UnmappedMemberHandling.Disallow`). |
| Atomowość i izolacja | Każda operacja = jeden `IUnitOfWork` uruchamiany przez `RunAsync` (ponawianie konfliktów). PostgreSQL: `SERIALIZABLE`; in-memory: jedna transakcja naraz. ADR 0005. |
| Zdarzenia | Transactional outbox + idempotentne handlery; Worker osobnym procesem. ADR 0006. |
| Idempotencja | `Idempotency.ExecuteAsync`: odpowiedź zapisywana w tej samej transakcji co skutki; ponowienie z tym samym kluczem zwraca zapisaną odpowiedź, inny body → 409. |
| Współbieżność | Optimistic concurrency (`version`) + `TryUpdate…(expectedVersion)`. Przegrany wyścig → 409 `version_conflict`. |
| Losowość | Seed z CSPRNG, przebieg z `DeterministicRandom` (xoshiro256**, wektory referencyjne w testach) → każdy rzut odtwarzalny z seeda + `rulesVersion`. |
| Audyt | Append-only `audit_log` z `correlationId`; w SQL trigger blokuje UPDATE/DELETE. |
| Błędy | RFC 9457 `application/problem+json`, stabilne `code`, `correlationId`. |
| Czas | Wyłącznie `TimeProvider` po stronie serwera (testy używają `ManualTimeProvider`). |

## Moduły

Zob. [modules.md](modules.md).
