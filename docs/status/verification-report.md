# Raport weryfikacji — Etapy 0–4

Data: 2026-10-12. Wszystko oznaczone ✅ zostało **uruchomione** w środowisku budowy; reszta jest wyraźnie oznaczona.

## Środowisko budowy (Etap 0)

| Element | Stan |
|---|---|
| .NET SDK 10.0.112 | z repozytorium Ubuntu |
| NuGet | **zablokowany**. Npgsql 10.0.3 zbudowany ze źródeł oficjalnego tagu (z Scriban 6.5.1 dla generatora źródeł) do lokalnego feedu — tylko w sandboxie; repo wskazuje oficjalną paczkę |
| PostgreSQL 16 + PostGIS 3 | lokalnie (apt) — używane przez testy |
| Docker | daemon działa, ale rejestry obrazów zablokowane → obrazy/compose nieuruchomione |
| Unity | niedostępne |
| GitHub | brak ważnego tokena → repozytorium lokalne |

## Wyniki

| Sprawdzenie | Wynik |
|---|---|
| Build Release całego solution, ostrzeżenia = błędy | ✅ 0 ostrzeżeń |
| Testy jednostkowe (RNG + wektory referencyjne, UUIDv7, reguły postaci i kultywacji, silnik walki: formuły, oś czasu, efekty, złota walka, 600 losowych walk) | ✅ 44/44 |
| Zestaw integracyjny na **in-memory** | ✅ 85/86, 1 NOT RUN (restart procesu wymaga trwałej bazy); powtórzony 3× bez niestabilności |
| Zestaw integracyjny na **PostgreSQL** (te same testy + migracje + prawdziwy proces Workera) | ✅ 86/86; powtórzony 3× bez niestabilności |
| Testy SQL: migracje, ponowne uruchomienie (no-op), wykrycie zmiany, integralność (28 + 14 + 16 asercji) | ✅ |
| `JianghuGo.Api migrate` na pustej bazie + API w `Production` na PostgreSQL: `/health/ready` = Healthy | ✅ |
| Skrypty Unity (UI, adaptery) | ⚠️ tylko kontrola typów względem atrap API Unity |
| Docker Compose, Dockerfile'e, CI GitHub Actions | ❌ NIEURUCHOMIONE (brak rejestrów / zdalnego repo) |

## Obowiązkowe testy akceptacyjne (§14)

| # | Scenariusz | Status |
|---|---|---|
| 1 | Brak dostępu do cudzej postaci | ✅ postać, szkic, kultywacja, powiadomienia (`SecurityTests`, `CultivationTests`, `OutboxTests`) |
| 2 | Podwójne żądanie nagrody | ✅ Qi z medytacji (`Finishing_meditation_twice_with_same_key_grants_once`, `Concurrent_finish_requests_grant_once`), kroki, koszt rerolla |
| 3 | Równoczesny zakup unikalnego przedmiotu | ⏳ Etap 6 (mechanizm: SERIALIZABLE + wersje — przetestowany na rerollach i przełomach) |
| 4 | Ujemne ilości odrzucane | 🟡 Qi, kroki, żetony (API + CHECK w bazie); przedmioty — Etap 6 |
| 5 | Nieudana transakcja bez częściowego pobrania | ✅ żetony, Qi (rollback UoW, test na obu magazynach) |
| 6 | Klient nie ustawia wyników | ✅ walka (`Client_cannot_send_damage_or_outcome`), statystyki, czas medytacji, rzut przełomu |
| 7 | Zła kolejność tur | ✅ `Wrong_turn_number_and_foreign_battles_are_rejected` |
| 8 | Powtórzona akcja walki | ✅ `Repeated_action_with_same_key_replays_and_resolves_the_turn_once`, `Concurrent_actions_for_the_same_turn_resolve_it_once`, PK w bazie |
| 9 | Skok GPS bez nagrody | ⏳ Etap 5 |
| 10 | Restart workera nie gubi zdarzeń | ✅ `WorkerProcessTests` (prawdziwy proces, `kill`), `Pending_events_survive_a_full_process_restart`, `Handler_crash_rolls_back_…` |
| 11 | Awaria połączenia bez duplikacji | ✅ `Lost_response_retry_reuses_idempotency_key_and_charges_once` |
| 12 | Migracja bez utraty danych | ✅ `Upgrade_from_stage2_schema_preserves_existing_data` + nieudana migracja wycofana w całości |
| 13 | Wygasła/unieważniona sesja | ✅ |

## Znane ograniczenia
- Walka: brak Laboratorium Dao i handlu zwojami (Etap 6); klient ma tekstowy prototyp zamiast animowanego widoku bocznego; historyczne pliki reguł walki trzeba będzie archiwizować przy pierwszej zmianie `rulesVersion`.
- Ścieżka Płomienia nie ma jeszcze 12 punktów geograficznych (wymaga Etapu 5).
- Brak testów obciążeniowych; wpływ SERIALIZABLE na przepustowość niezmierzony.
- Backup/restore niećwiczone; zadania retencji (czyszczenie outboxa, idempotencji, sesji) jeszcze nie istnieją.
- Rate limiting w pamięci procesu (wiele instancji API → Redis).
- Refresh token w kliencie w PlayerPrefs (tylko DEV).
- Powiadomienia tylko w grze (bez push na urządzenia).
