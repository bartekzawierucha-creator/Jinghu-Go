# ADR 0002 — Magazyn: PostgreSQL (produkcja) + in-memory (dev/testy)

**Status:** przyjęta. Wersja tymczasowa (tylko in-memory, Etap 1–2) **zamknięta w Etapie 3**.

**Decyzja.**
1. Logika aplikacyjna zależy wyłącznie od portów (`IUnitOfWork`, repozytoria modułów, `IIdempotencyStore`, `IAuditLog`, `IOutbox`).
2. **PostgreSQL** (`Storage:Provider=Postgres`, domyślnie): Npgsql bez ORM, ręczny SQL w repozytoriach modułów, transakcje `SERIALIZABLE` z ponawianiem (ADR 0005).
3. **In-memory** (`Storage:Provider=InMemory`): serializowalny, atomowy, nietrwały — do szybkiego developmentu i testów. Odmowa startu w `Production`.
4. Ten sam zestaw testów integracyjnych przechodzi na obu (`JIANGHU_TEST_STORAGE=InMemory|Postgres`); testy procesu workera i restartu wymagają PostgreSQL.
5. Migracje: `database/migrations`, wykonywane przez `JianghuGo.Api migrate` (krok wdrożenia, nigdy przy starcie serwera WWW) albo `database/migrate.sh` — oba używają tej samej tabeli i sum kontrolnych (test `Shell_migrator_and_dotnet_migrator_share_checksums`).

**Uwaga o środowisku budowy.** NuGet był niedostępny w sandboxie, więc Npgsql 10.0.3 zbudowano tam ze źródeł oficjalnego tagu `v10.0.3` (do lokalnego feedu). Repozytorium odwołuje się do zwykłej paczki `Npgsql 10.0.3` z nuget.org — CI i maszyny deweloperskie używają oficjalnej binarki.
