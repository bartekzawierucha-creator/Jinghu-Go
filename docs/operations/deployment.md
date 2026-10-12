# Wdrożenie

## Składniki
| Proces | Obraz / artefakt | Skalowanie | Uwagi |
|---|---|---|---|
| API (`JianghuGo.Api`) | `infrastructure/docker/api.Dockerfile` | poziomo, bezstanowe | `/health/live` (liveness), `/health/ready` (readiness: baza + outbox), `/health/economy` tylko w sieci wewnętrznej |
| Worker (`JianghuGo.Worker`) | `infrastructure/docker/worker.Dockerfile` | 1+ instancji (outbox: `FOR UPDATE SKIP LOCKED`, retencja: idempotentne partie) | wymaga PostgreSQL |
| PostgreSQL 16 + PostGIS 3 | zarządzany lub `postgis/postgis:16-3.4` | pionowo; repliki do odczytu niewykorzystywane w v1 | jedyne źródło prawdy (waluta, przedmioty, transakcje) |
| Redis | opcjonalny | — | w v1 nieużywany (rate limiting jest w pamięci procesu — patrz ograniczenia) |

## Kolejność wdrożenia
1. **Kopia zapasowa** (`pg_dump --format=custom`), sprawdzona procedurą z `docs/operations/backup-restore.md` (`database/backup-restore-test.sh` ćwiczy ją w CI).
2. **Migracje jako osobny krok**: `dotnet JianghuGo.Api.dll migrate` (lub `database/migrate.sh`) z rolą właściciela schematu. Migrator odmawia pracy, jeśli zastosowany plik zmieniono; migracje są addytywne (nowe tabele/kolumny), więc stara wersja API działa na nowym schemacie w trakcie wdrożenia.
3. **Worker**, potem **API** (rolling update; readiness odcina instancję, która nie widzi bazy).
4. Weryfikacja: `/health/ready` = Healthy, `/health/economy` = Healthy, metryki `jianghu.outbox.lag` i błędy 5xx.

## Konfiguracja (zmienne środowiskowe)
| Klucz | Wymagany | Opis |
|---|---|---|
| `ConnectionStrings__Jianghu` | tak | rola aplikacji bez uprawnień DDL; `Maximum Pool Size` dobrany do liczby instancji × rdzeni bazy |
| `Storage__Provider` | tak | `Postgres` (`InMemory` jest odrzucany w `Production`) |
| `World__Seed` | **sekret**, tak | 64-bitowa liczba ≠ 0; zmiana = nowa mapa świata (wszystkie punkty) — tylko świadomie |
| `Worker__RunInProcess` | nie | `false` w produkcji (osobny Worker) |
| `RateLimiting__AuthPermitsPerMinute`, `RateLimiting__GlobalPermitsPerMinute` | nie | domyślnie 10 i 300 na IP/minutę |
| `Retention__IntervalMinutes` | nie | Worker, domyślnie 60 |
| `GameConfig__*Path` | nie | ścieżki plików reguł (domyślnie `game-config/*.json` z obrazu) |

Sekretów nie ma w repozytorium ani w obrazie (gitleaks w CI). TLS kończy się na ingressie/load balancerze; klient w wersjach release odmawia `http://` (`UnityWebRequestTransport`).

## Zmiana reguł gry
Pliki w `content/game-config` są walidowane przy starcie (błąd = proces nie wstaje, readiness nie przechodzi — wadliwa konfiguracja nie trafi do ruchu). Każda zmiana liczb wymaga nowej `rulesVersion`. Walki, przełomy, rzemiosło i techniki Dao zapisują wersję reguł / zamrażają definicje, więc historia pozostaje odtwarzalna.

## Wycofanie
API i Worker: poprzedni obraz (schemat jest zgodny wstecz dzięki addytywnym migracjom). Migracji się nie cofa — błędną naprawia nowa migracja. Utrata danych: odtworzenie z kopii (RPO = częstotliwość kopii + WAL, jeśli włączony PITR).

## Lokalnie
`docker compose up --build` (Postgres+PostGIS, migracje, Redis, API, Worker; sekrety deweloperskie z `.env`). **Uwaga:** w środowisku budowy tego repozytorium Docker nie miał dostępu do rejestrów — compose i Dockerfile'e nie zostały tu uruchomione (CI też ich nie buduje).
