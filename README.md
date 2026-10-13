# Jianghu Go 江湖 GO

Mobilne, geolokalizacyjne MMO RPG w świecie Wuxia/Xianxia: kultywacja Qi, turowe pojedynki, sekty i terytoria na prawdziwej mapie.
Serwer jest jedynym autorytetem — klient wysyła intencje, nigdy wyniki.

## Stan projektu

| Etap | Zakres | Status |
|---|---|---|
| 0 | Audyt środowiska | ✅ zrobiony — [raport weryfikacji](docs/status/verification-report.md) |
| 1 | Fundament: solution, health checks, logi JSON, korelacja, CI, Docker Compose, testy | ✅ kod + testy (Compose/Docker **nieuruchomione** — patrz raport) |
| 2 | Konto i postać: rejestracja, logowanie, sesje, losowanie postaci, rerolle, blokady, finalizacja, klient Unity (rdzeń) | ✅ serwer + rdzeń klienta przetestowane; skrypty Unity tylko sprawdzone typowo |
| — | Adapter PostgreSQL (SERIALIZABLE + ponawianie), migrator w C# | ✅ cały zestaw testów przechodzi na PostgreSQL i in-memory |
| 3 | Kultywacja (medytacja, kroki, noc trucizn, przełomy, odchylenie Qi), transactional outbox, Worker, powiadomienia | ✅ (ścieżka Płomienia bez 12 punktów GPS — Etap 5) |
| 4 | Walka turowa 1v1 (PvE, PvP), 8 slotów, techniki 1–10, nagrody przez outbox, replay i commit-reveal seeda | ✅ (Laboratorium Dao i zwoje → następny krok; animacja widoku bocznego → klient) |
| 5 | Świat i GPS: sesje eksploracji, walidacja próbek, proceduralne punkty, bestie → walka, 12 miejsc Płomienia, wydarzenia, minimalizacja danych lokalizacyjnych | ✅ (mapa w kliencie: abstrakcja + prototyp listy) |
| 6 | Przedmioty i ekonomia: sakwa, ekwipunek (5 slotów), pigułki, łupy i materiały ze świata, rzemiosło (kucie, alchemia, ulepszanie +1…+15), targ z depozytem, opłatą i podatkiem, rekonsyliacja rejestrów | ✅ ([zasady](docs/game-design/economy.md), [ADR 0009](docs/decisions/0009-economy-ledgers.md)); ekrany Unity — prototyp |
| 6+ | Laboratorium Dao (techniki z komponentów w budżecie mocy), zwoje technik na targu | ✅ ([zasady](docs/game-design/dao-lab.md), ADR 0010) |
| 7 | Sekty (role, prośby, wkłady, skarbiec, siedziba, historia) i terytoria (zajmowanie, wojny GvG, danina, historia własności) | ✅ ([zasady](docs/game-design/sects-territory.md), ADR 0011) |
| 8 | Integracja i wydanie: end-to-end, test obciążeniowy, przegląd bezpieczeństwa endpointów, ćwiczenie backup/restore, retencja danych, dokumentacja wdrożenia | ✅ wykonane — **[raport gotowości](docs/status/release-readiness.md): jeszcze nie do publicznego wydania** (klient Unity niezweryfikowany, opóźnienia walki pod obciążeniem) |
| MVP | **[Wersja 1.0.0-mvp](docs/status/mvp.md)**: natywny klient Android z grafiką i animacjami, tryb offline z wbudowanym serwerem, PvP, edycja technik, zarządzanie sektą, kroki, prognoza medytacji | ✅ przetestowane w CI na emulatorze; telefony fizyczne — następny krok |

Projekt **nie jest gotowy produkcyjnie** (brak testów obciążeniowych, odtwarzania backupu, bezpiecznego magazynu tokenów w kliencie) — patrz [raport weryfikacji](docs/status/verification-report.md).

## Szybki start

Wymagania: .NET SDK 10, (opcjonalnie) PostgreSQL 16 + PostGIS 3 lub Docker.

```bash
./build.sh                                   # build (ostrzeżenia = błędy) + testy jednostkowe + integracyjne (in-memory)
JIANGHU_TEST_POSTGRES="Host=localhost;Username=…;Password=…;Database=postgres" \
ADMIN_URL=postgres://user:pass@localhost:5432/postgres ./build.sh   # + cały zestaw na PostgreSQL + testy SQL

dotnet run --project server/src/Api          # API na http://localhost:5000 (Development: in-memory, outbox w procesie)
docker compose up --build                    # Postgres+PostGIS, migracje, Redis, API, Worker

# Wdrożenie: sekret świata World__Seed, migracje jako osobny krok, potem API i Worker
dotnet JianghuGo.Api.dll migrate             # ConnectionStrings__Jianghu=…
```

Przykład (hasło: dowolne, min. 10 znaków):
```bash
read -rs PASS
curl -XPOST localhost:5000/api/v1/auth/register -H 'content-type: application/json' -d "{\"email\":\"ja@example.com\",\"password\":\"$PASS\"}"
curl -XPOST localhost:5000/api/v1/auth/login    -H 'content-type: application/json' -d "{\"email\":\"ja@example.com\",\"password\":\"$PASS\"}"
curl -XPOST localhost:5000/api/v1/characters/drafts -H "Authorization: Bearer jga_…"
```

Klient Unity: otwórz `client/JianghuGo.Unity` w Unity 6 LTS, utwórz pustą scenę, dodaj obiekt z komponentem `GameBootstrap` i ustaw `serverUrl`.

## Gra na Androida (APK bez Unity)

`client/JianghuGo.Android` — natywny klient .NET for Android, korzysta z tego samego rdzenia klienta (ApiClient + flowy), który testy integracyjne sprawdzają z prawdziwym serwerem. Workflow `android-native-apk` buduje APK, instaluje go na emulatorze i przechodzi grę (rejestracja, postać, eksploracja z GPS, walka, sakwa) — dopiero wtedy publikuje plik w pre-release **android-latest**.

1. Na komputerze: `API_BIND=0.0.0.0 docker compose up --build`.
2. Na telefonie (ta sama sieć Wi-Fi): zainstaluj `JianghuGo-dev.apk` (zezwól na instalację z nieznanych źródeł), wpisz `http://<adres IP komputera>:8080`, załóż konto.

Wersja deweloperska: podpis debug, ruch http dozwolony w LAN, token w SharedPreferences (nie na produkcję).

## Struktura

```
contracts/openapi/      kontrakt API (test kontraktowy pilnuje zgodności z serwerem)
content/game-config/    reguły gry jako dane (walidowane przy starcie i w testach)
server/src/             Domain · Application · Infrastructure · Modules/{Identity,Characters,Cultivation,Combat,Geolocation,World,Inventory,Crafting,Market,Sects,Territory,Notifications} · Composition · Api · Worker
tools/balance-simulator/ macierz zwycięstw AI vs AI na prawdziwym silniku walki
tools/unity-typecheck/  kontrola typów skryptów Unity względem atrap API (bez uruchamiania)
tools/load-test/        generator obciążenia HTTP (wirtualni gracze, percentyle, budżet)
server/tests/           TestKit · UnitTests · IntegrationTests (integracja, bezpieczeństwo, kontrakt, architektura, klient)
client/                 Unity: Assets/Scripts/Core (czysty C#, testowany) + Assets/Scripts/Unity (UI, adaptery)
database/               migracje SQL, migrator z sumami kontrolnymi, testy integralności
infrastructure/docker/  Dockerfile API i Workera
docs/                   architektura, decyzje (ADR), game design, bezpieczeństwo, operacje
```

## Zasady pracy

- Każdy etap kończy się zielonym `./build.sh`; nic nie jest oznaczane jako działające bez testu.
- Formuły i liczby gry tylko w `content/game-config`, nigdy w kodzie.
- Zastosowanych migracji się nie edytuje (migrator to wykrywa) — zawsze nowy plik `Vnnnn__*.sql`.
- Operacje kosztowe/nagrody wymagają `Idempotency-Key`; zmiany stanu współdzielonego — `expectedVersion`.
