# ADR 0009 — Ekonomia na rejestrach, depozyt targu, kontrakty między modułami

**Status:** przyjęta (Etap 6)

**Decyzje.**
1. **Rejestr jako źródło prawdy.** Kamienie Duchowe i przedmioty stackowalne zmieniają się wyłącznie przez dopisanie wiersza do `inventory.wallet_entries` / `inventory.stack_entries`. Saldo i ilość to projekcje utrzymywane przez trigger, który ponownie sprawdza `po = przed + delta` pod blokadą wiersza; bezpośredni `UPDATE` projekcji jest odrzucany. `(postać, [przedmiot,] powód, referencja)` jest unikalne — ta sama operacja nie może być zaksięgowana dwa razy. Widoki `wallet_mismatches` / `stack_mismatches` + `/health/economy` służą do rekonsyliacji.
2. **Przedmioty unikalne** to wiersze z wersją (każda zmiana podnosi ją dokładnie o 1 — trigger) i niezmiennym dziennikiem pochodzenia (`item_events`). Właściciel (konto) musi zgadzać się z postacią (trigger).
3. **Wszystko w transakcji wołającego.** `IInventory` działa w jednostce pracy modułu, który go woła (Rzemiosło, Targ): „zabierz składniki + pobierz kamienie + daj wynik + zapisz dziennik” to jedna transakcja SERIALIZABLE; każdy błąd biznesowy przerywa ją bez commitu (test akceptacyjny #5).
4. **Targ z depozytem i wersją oferty.** Towar opuszcza sakwę przy wystawieniu; zakup aktualizuje ofertę warunkiem `version = oczekiwana`. W PostgreSQL równoległe zakupy kończą się konfliktem serializacji, ponowieniem i odpowiedzią `listing_unavailable` dla przegranych; indeks częściowy gwarantuje najwyżej jedną aktywną ofertę na przedmiot. Wygasanie jest leniwe (filtr `expires_at`), bez zadań w tle.
5. **Kierunek zależności.** Combat potrzebuje premii ekwipunku (`Inventory.Contracts.IEquipmentStats`), więc Inventory nie może zależeć od Combat ani World. Zdarzenia `combat.battle_finished` i `world.items_gathered` Inventory czyta przez **własne lustrzane rekordy** ładunku JSON (`EventMirrors`) — kontraktem jest JSON zdarzenia, pilnowany testami integracyjnymi po obu stronach. Rzemiosło i Targ widzą katalog przedmiotów tylko przez `IItemCatalog`.
6. **Nagrody nie przepadają.** Handlery łupów/zbierania nie sprawdzają pojemności sakwy (stos przycinany do 999, nadwyżka w audycie); jednorazowość zapewnia `inventory.grants (id wiadomości, postać)`.

**Konsekwencje.** Ruchy waluty są w pełni audytowalne i odtwarzalne; koszt: każde przyznanie to INSERT do rejestru (wolumen rośnie liniowo z aktywnością — retencja bez limitu, możliwe partycjonowanie po czasie w Etapie 8).
