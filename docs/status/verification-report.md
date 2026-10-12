# Raport weryfikacji — Etapy 0–6

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
| Testy jednostkowe (RNG, UUIDv7, reguły postaci i kultywacji, silnik walki, walidacja próbek GPS, generator świata, haversine, przedmioty/łupy, rzemiosło, targ, spójność plików konfiguracji) | ✅ 68/68 |
| Zestaw integracyjny na **in-memory** | ✅ 121/122, 1 NOT RUN (restart procesu wymaga trwałej bazy) |
| Zestaw integracyjny na **PostgreSQL** (te same testy + migracje + prawdziwy proces Workera) | ✅ 122/122; powtórzony 3× bez niestabilności (w tym test równoczesnych zakupów) |
| Testy SQL: migracje, ponowne uruchomienie (no-op), wykrycie zmiany, integralność (28 + 14 + 16 + 9 + 24 asercji, w tym zgodność haversine z PostGIS `ST_Distance` na 2000 losowych par oraz rejestry, przedmioty, oferty i dziennik rzemiosła) | ✅ |
| `JianghuGo.Api migrate` na pustej bazie + API w `Production` na PostgreSQL: `/health/ready` = Healthy | ✅ |
| Skrypty Unity (UI, adaptery) | ⚠️ tylko kontrola typów: projekt `tools/unity-typecheck` kompiluje wszystkie skrypty (C# 9, `UNITY_ANDROID`) względem ręcznie napisanych atrap API Unity — od Etapu 6 jest częścią solution, więc build i CI go uruchamiają (wcześniej tylko doraźnie w sandboxie). Wykrył błąd składni w nowym ekranie sakwy, który został poprawiony. Nie zastępuje kompilacji w edytorze Unity ani uruchomienia |
| Docker Compose, Dockerfile'e | ❌ NIEURUCHOMIONE (brak rejestrów obrazów) |
| CI GitHub Actions | ⚠️ workflow wypchnięty na GitHub; jego wyniku stąd nie widać — NIEPOTWIERDZONE |

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

## Znane ograniczenia
- GPS: serwer nie wykryje fałszywego GPS, który porusza się „realistycznie” — potrzebna atestacja urządzenia (Play Integrity / App Attest), Etap 8. Zadania retencji (czyszczenie zebrań po 7 dniach) jeszcze nie istnieją. Mapa w kliencie: abstrakcja `IMapProvider` + projekcja Web Mercator gotowe, renderer zwoju to prototyp listy. `UnityLocationSource` (w tym uprawnienia Androida) niesprawdzone w Unity.
- Walka: brak Laboratorium Dao i zwojów technik (następny krok po Etapie 6); klient ma tekstowy prototyp zamiast animowanego widoku bocznego; historyczne pliki reguł walki trzeba będzie archiwizować przy pierwszej zmianie `rulesVersion`.
- Ścieżka Płomienia nie ma jeszcze 12 punktów geograficznych (wymaga Etapu 5).
- Brak testów obciążeniowych; wpływ SERIALIZABLE na przepustowość niezmierzony.
- Backup/restore niećwiczone; zadania retencji (czyszczenie outboxa, idempotencji, sesji) jeszcze nie istnieją.
- Rate limiting w pamięci procesu (wiele instancji API → Redis).
- Refresh token w kliencie w PlayerPrefs (tylko DEV).
- Powiadomienia tylko w grze (bez push na urządzenia).
- Ekonomia (Etap 6): balans liczb (ceny, szanse, przepływ KD) to pierwsza propozycja bez symulacji gospodarki; targ ma tylko listę „najnowsze” z filtrem przedmiotu (bez sortowania po cenie i wyszukiwania tekstowego); brak zadań retencji dla rejestrów i zamkniętych ofert; ekrany sakwy/rzemiosła/targu w Unity to prototyp listy (sprawdzony tylko typowo); pranie waluty między różnymi kontami wykrywa dopiero AntiAbuse (Etap 8).
