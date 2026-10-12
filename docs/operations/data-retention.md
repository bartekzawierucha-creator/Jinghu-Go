# Retencja danych

| Dane | Cel | Retencja | Dostęp |
|---|---|---|---|
| `identity.accounts` | logowanie | do usunięcia konta | serwer |
| `identity.sessions` | uwierzytelnianie, wykrywanie kradzieży | 30 dni po wygaśnięciu refresh tokena | serwer |
| `platform.idempotency_records` | ochrona przed ponowieniami | 7 dni | serwer |
| `platform.audit_log` | audyt bezpieczeństwa i ekonomii | 2 lata (do potwierdzenia prawnie) | administracja, append-only |
| rejestr Żetonów Losu | ekonomia | bez limitu (księgowość) | serwer |
| `platform.outbox_messages` | dostarczanie zdarzeń | przetworzone: 7 dni; `dead`: do ręcznej analizy | serwer, operator |
| `cultivation.breakthroughs` | audyt i odtwarzanie rzutów | bez limitu (mały wolumen) | serwer |
| `combat.turns`, `combat.battles` | rozstrzyganie sporów, odtwarzanie walk | 180 dni po zakończeniu (do potwierdzenia) | uczestnicy walki, operator |
| `combat.intents` | bieżąca tura | po rozstrzygnięciu tury redundantne z `combat.turns`; czyszczenie po 7 dniach | serwer |
| `notifications.notifications` | powiadomienia w grze | 90 dni (do potwierdzenia) | właściciel konta |
| `geolocation.sessions.last_position` | weryfikacja pozycji do nagród | tylko **ostatni** punkt, kasowany przy końcu sesji (CHECK); surowe próbki nie są zapisywane wcale | serwer |
| `geolocation.sessions` (agregaty) | statystyki, anty-nadużycia | 90 dni | serwer, zespół anty-nadużyć |
| `world.node_claims` | jednorazowość zebrań | 7 dni (klucz ≈ komórka ~550 m) | serwer |
| `world.flame_attunements` | cooldown miejsc Płomienia | do usunięcia postaci (12 publicznych miejsc) | serwer |

Zadania czyszczące: do dodania jako okresowe zadania Workera (Etap 8, razem z testami obciążeniowymi); indeksy pod nie już istnieją. Usunięcie konta z postaciami: blokowane przez FK (`ON DELETE RESTRICT`) — wymaga procedury anonimizacji (do zaprojektowania przed wydaniem).
| rejestry `inventory.wallet_entries`, `inventory.stack_entries` | księgowość ekonomii, rekonsyliacja | bez limitu (partycjonowanie po czasie rozważane w Etapie 8) | serwer, operator |
| `inventory.items`, `inventory.item_events` | własność i pochodzenie przedmiotów unikalnych | do usunięcia postaci | właściciel, operator |
| `inventory.grants` | jednorazowość nagród z outboxa | 30 dni (dłużej niż retencja przetworzonych wiadomości) | serwer |
| `crafting.log` | audyt i odtwarzanie rzutów | 180 dni (do potwierdzenia) | właściciel, operator |
| `market.listings` | handel, spory | aktywne: do zamknięcia; zamknięte: 1 rok (do potwierdzenia) | strony transakcji, operator |
| `combat.custom_techniques`, `combat.known_techniques` | techniki autorskie i wiedza | bez limitu (odwołują się do nich walki i zwoje) | serwer |
| `sects.*` (sekty, członkowie, prośby, skarbiec, historia) | organizacja graczy, audyt uprawnień | sekty i historia: bez limitu; zamknięte prośby: 180 dni (do potwierdzenia) | członkowie, operator |
| `territory.*` (terytoria, wojny, punktacja, historia własności) | kontrola mapy, rozstrzyganie sporów | bez limitu (mały wolumen) | publiczne (bez danych osobowych) |

**Wykonanie (Etap 8, ADR 0012).** `RetentionRunner` w procesie Worker usuwa partiami: `platform.idempotency_records` > 7 dni, przetworzone `platform.outbox_messages` > 7 dni, `notifications.notifications` > 90 dni, `world.node_claims` > 7 dni, `inventory.grants` > 30 dni, `combat.intents` > 7 dni zakończonych walk. Pozostałe okresy z tabeli („do potwierdzenia”) wymagają decyzji prawnej przed wydaniem i nie są jeszcze automatyczne.
