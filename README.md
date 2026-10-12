# Jianghu Go 江湖 GO

Mobilne, geolokalizacyjne MMO RPG w świecie Wuxia/Xianxia: kultywacja Qi, turowe pojedynki, sekty i terytoria na prawdziwej mapie.
Serwer jest jedynym autorytetem — klient wysyła intencje, nigdy wyniki.

## Stan projektu

| Etap | Zakres | Status |
|---|---|---|
| 0 | Audyt środowiska | ✅ zrobiony — [raport weryfikacji](docs/status/verification-report.md) |
| 1 | Fundament: solution, health checks, logi JSON, korelacja, CI, Docker Compose, testy | ✅ kod + testy (Compose/Docker **nieuruchomione** — patrz raport) |
| 2 | Konto i postać: rejestracja, logowanie, sesje, losowanie postaci, rerolle, blokady, finalizacja, klient Unity (rdzeń) | ✅ serwer + rdzeń klienta przetestowane; skrypty Unity tylko sprawdzone typowo |
| 3 | Kultywacja | ⏳ następny |
| 4–8 | Walka, świat/GPS, ekonomia, sekty, wydanie | ⏳ |

**Ważne ograniczenie Etapu 2:** serwer działa na transakcyjnym magazynie in-memory (dane znikają po restarcie, odmowa startu w `Production`).
Schemat PostgreSQL/PostGIS jest gotowy i przetestowany na prawdziwej bazie, ale adapter Npgsql czeka — patrz [ADR 0002](docs/decisions/0002-storage-adapter.md).

## Szybki start

Wymagania: .NET SDK 10, (opcjonalnie) PostgreSQL 16 + PostGIS 3 lub Docker.

```bash
./build.sh                                   # build (ostrzeżenia = błędy) + testy jednostkowe + integracyjne
ADMIN_URL=postgres://user:pass@localhost:5432/postgres ./build.sh   # + migracje i testy integralności bazy

dotnet run --project server/src/Api          # API na http://localhost:5000 (Development, in-memory)
docker compose up --build                    # Postgres+PostGIS, migracje, Redis, API
```

Przykład:
```bash
curl -XPOST localhost:5000/api/v1/auth/register -H 'content-type: application/json' -d '{"email":"ja@example.com","password":"miecz-wudang-123"}'
curl -XPOST localhost:5000/api/v1/auth/login    -H 'content-type: application/json' -d '{"email":"ja@example.com","password":"miecz-wudang-123"}'
curl -XPOST localhost:5000/api/v1/characters/drafts -H "Authorization: Bearer jga_…"
```

Klient Unity: otwórz `client/JianghuGo.Unity` w Unity 6 LTS, utwórz pustą scenę, dodaj obiekt z komponentem `GameBootstrap` i ustaw `serverUrl`.

## Struktura

```
contracts/openapi/      kontrakt API (test kontraktowy pilnuje zgodności z serwerem)
content/game-config/    reguły gry jako dane (walidowane przy starcie i w testach)
server/src/             Domain · Application · Infrastructure · Modules/{Identity,Characters} · Api
server/tests/           TestKit · UnitTests · IntegrationTests (integracja, bezpieczeństwo, kontrakt, architektura, klient)
client/                 Unity: Assets/Scripts/Core (czysty C#, testowany) + Assets/Scripts/Unity (UI, adaptery)
database/               migracje SQL, migrator z sumami kontrolnymi, testy integralności
infrastructure/docker/  Dockerfile API
docs/                   architektura, decyzje (ADR), game design, bezpieczeństwo, operacje
```

## Zasady pracy

- Każdy etap kończy się zielonym `./build.sh`; nic nie jest oznaczane jako działające bez testu.
- Formuły i liczby gry tylko w `content/game-config`, nigdy w kodzie.
- Zastosowanych migracji się nie edytuje (migrator to wykrywa) — zawsze nowy plik `Vnnnn__*.sql`.
- Operacje kosztowe/nagrody wymagają `Idempotency-Key`; zmiany stanu współdzielonego — `expectedVersion`.
