# Raport weryfikacji — Etapy 0–2

Data: 2026-10-12. Wszystko poniżej oznaczone ✅ zostało **uruchomione** w środowisku budowy; reszta jest wyraźnie oznaczona.

## Etap 0 — audyt środowiska

| Element | Wynik |
|---|---|
| .NET SDK | brak → zainstalowano 10.0.112 z repozytorium Ubuntu |
| NuGet (api.nuget.org) | **zablokowany** przez politykę sieci → zero zewnętrznych paczek (ADR 0002–0004) |
| Rejestry obrazów (Docker Hub, MCR) | **zablokowane**; daemon Docker uruchamia się, ale nie pobierze obrazów |
| PostgreSQL 16.15 + PostGIS 3 | zainstalowane z apt, używane do testów migracji |
| Unity | **niedostępne** |
| GitHub CLI | token nieważny → repozytorium tylko lokalne |

## Co zweryfikowano

| Sprawdzenie | Wynik |
|---|---|
| `dotnet build` całego solution, Release, ostrzeżenia = błędy, analizatory `latest-recommended` | ✅ 0 ostrzeżeń |
| Testy jednostkowe (RNG z wektorami referencyjnymi, walidator reguł, rozkład rzadkości, imiona, transakcje in-memory, idempotencja) | ✅ 17/17 |
| Testy integracyjne HTTP na prawdziwym Kestrelu: tożsamość, postacie, bezpieczeństwo, kontrakt OpenAPI, architektura, operacje | ✅ 36/36 |
| Rdzeń klienta Unity (`Assets/Scripts/Core`) przeciw prawdziwemu serwerowi: pełny przepływ, cichy refresh, utracona odpowiedź + ponowienie z tym samym kluczem, konflikt wersji z dwóch urządzeń | ✅ 6/6 |
| Migracje na PostgreSQL 16 + PostGIS: aplikacja, ponowne uruchomienie (no-op), wykrycie modyfikacji zastosowanej migracji | ✅ |
| Testy integralności bazy (28 asercji: unikalności, CHECK, append-only, rejestr żetonów, niezmienność tokenów, PostGIS) | ✅ |
| Smoke test opublikowanego API (`dotnet JianghuGo.Api.dll`): health, rejestracja, logowanie, szkic; odmowa startu in-memory w `Production` | ✅ |

## Czego NIE zweryfikowano

| Element | Powód | Jak zweryfikować |
|---|---|---|
| Skrypty Unity `Assets/Scripts/Unity/**` (UI, `UnityWebRequestTransport`, Newtonsoft, PlayerPrefs) | brak Unity | Sprawdzone tylko składniowo/typowo względem własnych atrap API Unity. Otworzyć w Unity 6 LTS. |
| `Packages/manifest.json` — wersje paczek Unity | brak Unity | Unity rozwiąże/zaktualizuje przy pierwszym otwarciu |
| `docker-compose.yml`, `api.Dockerfile` | rejestry obrazów zablokowane | `docker compose up --build` lokalnie |
| `.github/workflows/ci.yml` (w tym gitleaks, skan podatności) | brak zdalnego repo | pierwszy push |
| Adapter PostgreSQL dla serwera | brak Npgsql | ADR 0002 |

## Obowiązkowe testy akceptacyjne (specyfikacja §14)

| # | Scenariusz | Status |
|---|---|---|
| 1 | Brak dostępu do cudzej postaci | ✅ `SecurityTests.Cannot_read_or_modify_another_accounts_character_or_draft` |
| 2 | Podwójne żądanie nie daje podwójnego efektu | ✅ dla kosztów rerolla (`Reroll_with_same_idempotency_key_charges_once`, `Concurrent_duplicates_…`); nagrody — Etap 3 |
| 3 | Równoczesny zakup unikalnego przedmiotu | ⏳ Etap 6 (wzorzec wersjonowania przetestowany: `Concurrent_rerolls_on_same_version_succeed_exactly_once`) |
| 4 | Ujemne ilości odrzucane | 🟡 saldo żetonów (`database/tests`, `Insufficient_tokens_…`); przedmioty — Etap 6 |
| 5 | Nieudana transakcja bez częściowego pobrania | ✅ dla żetonów (`Insufficient_tokens_fail_without_partial_charge`, rollback UoW) |
| 6 | Klient nie ustawia wyników | ✅ dla statystyk (`Client_supplied_stats_are_rejected`); walka — Etap 4 |
| 7 | Zła kolejność tur | ⏳ Etap 4 |
| 8 | Powtórzona akcja walki | ⏳ Etap 4 |
| 9 | Skok GPS bez nagrody | ⏳ Etap 5 |
| 10 | Restart workera nie gubi zdarzeń outboxa | ⏳ Etap 3 |
| 11 | Awaria połączenia bez duplikacji | ✅ `ClientCoreTests.Lost_response_retry_reuses_idempotency_key_and_charges_once` |
| 12 | Migracja bez utraty danych | 🟡 aplikacja + niezmienność migracji ✅; test migracji z danymi — przy pierwszej migracji zmieniającej istniejące tabele |
| 13 | Wygasła/unieważniona sesja | ✅ `Expired_access_token_is_rejected`, `Logout_revokes_…`, `Refresh_token_reuse_revokes_whole_family` |

## Znane ograniczenia
- Dane serwera znikają po restarcie (in-memory). Projekt **nie jest gotowy produkcyjnie**.
- Jedna blokada globalna w in-memory: wydajność nieistotna, poprawność ponad wszystko.
- Rate limiting w pamięci procesu (jedna instancja); przy wielu instancjach → Redis.
- Refresh token po stronie klienta w PlayerPrefs (tylko DEV).
