# Raport weryfikacji — Etapy 0–8

Data: 2026-10-12. Wszystko oznaczone ✅ zostało **uruchomione** w środowisku budowy; reszta jest wyraźnie oznaczona.

## Środowisko budowy (Etap 0)

| Element | Stan |
|---|---|
| .NET SDK 10.0.112 | z repozytorium Ubuntu |
| NuGet | **zablokowany**. Npgsql 10.0.3 zbudowany ze źródeł oficjalnego tagu (z Scriban 6.5.1 dla generatora źródeł) do lokalnego feedu — tylko w sandboxie; repo wskazuje oficjalną paczkę |
| PostgreSQL 16 + PostGIS 3 | lokalnie (apt) — używane przez testy |
| Docker | daemon działa, ale rejestry obrazów zablokowane → obrazy/compose nieuruchomione |
| Unity | niedostępne |
| GitHub | `bartekzawierucha-creator/Jinghu-Go`, gałąź `main` (push od Etapu 6) |

## Wyniki

| Sprawdzenie | Wynik |
|---|---|
| Build Release całego solution, ostrzeżenia = błędy | ✅ 0 ostrzeżeń |
| Testy jednostkowe (RNG, UUIDv7, reguły postaci i kultywacji, silnik walki, walidacja próbek GPS, generator świata, haversine, przedmioty/łupy, rzemiosło, targ, Laboratorium Dao, macierz uprawnień sekt, reguły terytoriów, spójność plików konfiguracji) | ✅ 78/78 |
| Zestaw integracyjny na **in-memory** | ✅ 138/140, 2 NOT RUN (restart procesu i retencja wymagają trwałej bazy) |
| Zestaw integracyjny na **PostgreSQL** (te same testy + migracje + prawdziwy proces Workera + retencja + konflikt SERIALIZABLE przy COMMIT) | ✅ 140/140 (Etapy 0–6 powtórzone 3× bez niestabilności; po zmianach Etapu 8 — 1 pełny przebieg) |
| Testy SQL: migracje, ponowne uruchomienie (no-op), wykrycie zmiany, integralność (28 + 14 + 16 + 9 + 24 + 11 + 26 asercji, w tym zgodność haversine z PostGIS `ST_Distance` na 2000 losowych par oraz rejestry, przedmioty, oferty, dziennik rzemiosła, techniki Dao, sekty, skarbiec, wojny) | ✅ |
| Ćwiczenie kopii zapasowej (`database/backup-restore-test.sh`): pg_dump → pg_restore → 43 tabele identyczne co do wiersza, migrator „up to date”, rekonsyliacja czysta, triggery aktywne | ✅ |
| Test obciążeniowy (`tools/load-test`, wyniki niżej) | ✅ uruchomiony; ⚠️ budżet opóźnień walki NIESPEŁNIONY |
| `JianghuGo.Api migrate` na pustej bazie + API w `Production` na PostgreSQL: `/health/ready` = Healthy | ✅ |
| Skrypty Unity (UI, adaptery) | ⚠️ tylko kontrola typów: projekt `tools/unity-typecheck` kompiluje wszystkie skrypty (C# 9, `UNITY_ANDROID`) względem ręcznie napisanych atrap API Unity — od Etapu 6 jest częścią solution, więc build i CI go uruchamiają (wcześniej tylko doraźnie w sandboxie). Wykrył błąd składni w nowym ekranie sakwy, który został poprawiony. Ekrany Laboratorium Dao i sekt sprawdzone tak samo. Nie zastępuje kompilacji w edytorze Unity ani uruchomienia |
| Docker Compose, Dockerfile'e | ❌ NIEURUCHOMIONE (brak rejestrów obrazów) |
| CI GitHub Actions | ✅ zielone dla commitów Etapu 6 (server, database, secrets); pierwszy przebieg wykrył fałszywy alarm gitleaks (przykładowe hasło w README) — oznaczony i usunięty. Przebieg dla commitu Etapu 8 (`5e78e1a`): ✅ wszystkie kroki, w tym build, oba zestawy integracyjne, smoke load (20 graczy, bramka: 0 błędów i p95 < 3 s), testy SQL i ćwiczenie backup/restore |

## Obowiązkowe testy akceptacyjne (§14)

| # | Scenariusz | Status |
|---|---|---|
| 1 | Brak dostępu do cudzej postaci | ✅ postać, szkic, kultywacja, powiadomienia (`SecurityTests`, `CultivationTests`, `OutboxTests`) |
| 2 | Podwójne żądanie nagrody | ✅ Qi z medytacji (`Finishing_meditation_twice_with_same_key_grants_once`, `Concurrent_finish_requests_grant_once`), kroki, koszt rerolla, pigułki, rzemiosło, zakup (replay klucza), łupy i materiały ze świata (`inventory.grants`), wpis rejestru nie do powtórzenia (`Ledger_lines_cannot_be_applied_twice`) |
| 3 | Równoczesny zakup unikalnego przedmiotu | ✅ `Concurrent_buyers_of_a_unique_item_cannot_both_win` (4 kupujących naraz × 3 rundy: dokładnie 1 sukces, przedmiot istnieje raz, pieniądze przesunięte raz, rekonsyliacja czysta); w bazie: jedna aktywna oferta na przedmiot, wersja +1 |
| 4 | Ujemne ilości odrzucane | ✅ Qi, kroki, żetony, przedmioty (użycie, wyrzucenie, rzemiosło ×n, wystawienie: ilość i cena), wewnętrzny `IInventory` (`quantity_invalid`, `amount_invalid`) + CHECK w bazie |
| 5 | Nieudana transakcja bez częściowego pobrania | ✅ żetony, Qi; rzemiosło bez kamieni / bez miejsca (`Missing_stones_or_bag_space_consume_nothing`), wystawienie bez opłaty (depozyt wycofany), zakup z pełną sakwą (`a failed purchase charges nothing`), odrzucone ulepszenie |
| 6 | Klient nie ustawia wyników | ✅ walka (`Client_cannot_send_damage_or_outcome`), statystyki, czas medytacji, rzut przełomu |
| 7 | Zła kolejność tur | ✅ `Wrong_turn_number_and_foreign_battles_are_rejected` |
| 8 | Powtórzona akcja walki | ✅ `Repeated_action_with_same_key_replays_and_resolves_the_turn_once`, `Concurrent_actions_for_the_same_turn_resolve_it_once`, PK w bazie |
| 9 | Skok GPS bez nagrody | ✅ `Teleporting_next_to_a_node_does_not_earn_its_reward` (+ test domenowy, słaby GPS bez kar) |
| 10 | Restart workera nie gubi zdarzeń | ✅ `WorkerProcessTests` (prawdziwy proces, `kill`), `Pending_events_survive_a_full_process_restart`, `Handler_crash_rolls_back_…` |
| 11 | Awaria połączenia bez duplikacji | ✅ `Lost_response_retry_reuses_idempotency_key_and_charges_once` |
| 12 | Migracja bez utraty danych | ✅ `Upgrade_from_stage2_schema_preserves_existing_data` + nieudana migracja wycofana w całości |
| 13 | Wygasła/unieważniona sesja | ✅ |

## Etap 8 — wyniki i znalezione defekty

**Test obciążeniowy** (2 vCPU współdzielone przez API, PostgreSQL i generator; 50 graczy, 60 s; pełne wyniki: `docs/status/release-readiness.md`):

| Profil | Żądania/s | Błędy | p95 odczytów | p95 walki (`pve_action`) |
|---|---|---|---|---|
| realistyczny (500 ms namysłu) | 101 | 0 % | 112–138 ms | 3,4 s |
| stres (bez namysłu) | 70 | 0 % | 176–283 ms | 4,0 s |

Defekty znalezione testem obciążeniowym i **naprawione** (z testami regresji / ponownym pomiarem):
1. Konflikt SERIALIZABLE wykryty dopiero przy COMMIT zostawiał jednostkę pracy „otwartą”; jej zamknięcie rzucało `InvalidOperationException` (HTTP 500) i maskowało ponawialny konflikt → `PostgresUnitOfWorkTests` (80 błędów 500 → 0).
2. Hashe tokenów jako `char(64)` przy parametrach `text` → rzutowanie kolumny, brak użycia indeksu, pełny skan `identity.sessions` przy KAŻDYM uwierzytelnionym żądaniu i blokady predykatowe całej tabeli → migracja `V0016__session_hash_text.sql`.
3. Skany sekwencyjne małych tabel pod SERIALIZABLE (blokady predykatowe na całą relację) → sesje aplikacji z `enable_seqscan=off` (zalecenie dokumentacji PostgreSQL dla SSI) i `jit=off`; po pomiarze 0 skanów sekwencyjnych.
4. Wyczerpane ponowienia konfliktu kończyły się 500 → teraz `503 busy_retry` + `Retry-After` (klient ponawia z tym samym kluczem idempotencji); wykładniczy backoff z jitterem.

**Testy bezpieczeństwa Etapu 8:** `RobustnessTests` — każdy endpoint POST/PUT × 7 wrogich ciał (zły JSON, typy, nieznane pola, przepełnienia, 5000-znakowe napisy, znaki sterujące) → zawsze 4xx bez śladu stosu; każdy chroniony endpoint × brak/fałszywy token → 401. ✅

**Przepływ end-to-end:** `EndToEndTests` (eksploracja → zbiór → handel → medytacja → sekta → rekrutacja → wkłady → zajęcie terytorium → rekonsyliacja). ✅

**Profilowanie klienta:** ❌ NIEWYKONANE — Unity niedostępne w środowisku budowy.

## Znane ograniczenia
Pełna, aktualna lista (z oceną wpływu i warunkiem usunięcia) jest w `docs/status/release-readiness.md`.
