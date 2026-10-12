# ADR 0002 — Magazyn in-memory teraz, adapter PostgreSQL w następnym kroku

**Status:** przyjęta tymczasowo (Etap 1–2). **Do zamknięcia przed Etapem 3.**

**Kontekst.** Środowisko, w którym zbudowano Etapy 1–2, nie miało dostępu do NuGet (blokada sieci), więc nie było sterownika Npgsql. Dostępne były: .NET 10 SDK oraz PostgreSQL 16 + PostGIS 3.4 z apt.

**Decyzja.**
1. Logika aplikacyjna zależy od portów (`IUnitOfWork`, repozytoria modułów, `IIdempotencyStore`, `IAuditLog`, `IDestinyTokenLedger`), nie od bazy.
2. Implementacja `InMemoryDatabase`: serializowalne transakcje (globalna blokada async), atomowy commit (niezmienne słowniki copy-on-write), indeksy unikalne odwzorowane jawnie. Odmowa startu w środowisku `Production`.
3. Schemat SQL (`database/migrations`) jest pisany równolegle i **testowany na prawdziwym PostgreSQL** (`database/test.sh`): ograniczenia, unikalności, append-only, rejestr żetonów pilnowany triggerem.

**Plan adaptera (do zrobienia, gdy dostępny NuGet):**
- `Npgsql` (bez ORM w gorących ścieżkach; ewentualnie EF Core tylko do odczytów administracyjnych).
- `PostgresUnitOfWork` = `NpgsqlConnection` + transakcja `READ COMMITTED`; operacje ekonomiczne: `SELECT … FOR UPDATE` na wierszu salda / szkicu albo `UPDATE … WHERE version = @expected` (0 wierszy → `version_conflict`).
- Unikalność `(account_id, operation, key)` w `platform.idempotency_records`: przegrany wyścig dwóch identycznych żądań → naruszenie unikalności → rollback → 409 `idempotency_in_progress` (klient ponawia i dostaje zapisaną odpowiedź).
- Te same testy integracyjne uruchamiane dla obu dostawców (parametr `Storage:Provider`).

**Konsekwencje.** Etap 2 jest w pełni testowalny, ale nie trwały. Każdy nowy moduł musi dostarczyć migrację SQL + test integralności razem z kodem.
