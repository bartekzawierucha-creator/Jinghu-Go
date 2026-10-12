# ADR 0005 — Transakcje SERIALIZABLE z automatycznym ponawianiem

**Status:** przyjęta (Etap 3)

**Kontekst.** Reguły gry wymagają, by równoległe żądania (reroll, przełom, zakończenie medytacji, rynek w przyszłości) zachowywały się tak, jakby wykonano je po kolei. Ręczne blokady w każdym repozytorium łatwo pominąć.

**Decyzja.** Każda jednostka pracy PostgreSQL to transakcja `SERIALIZABLE`. Błędy `40001` (serialization failure) i `40P01` (deadlock) zamieniane są na `TransientConflictException`; `IUnitOfWorkFactory.RunAsync` uruchamia **całą** operację od nowa (do 25 prób, losowy backoff). Naruszenie unikalności (`23505`) → `DuplicateKeyException` (np. `email_taken`, `name_taken`). Operacje nie mają efektów zewnętrznych wewnątrz transakcji — dlatego ponowienie jest bezpieczne.

Dodatkowo tam, gdzie liczy się ekonomia, zostają jawne zabezpieczenia: `version` (optimistic concurrency), `SELECT … FOR UPDATE` na saldzie, trigger rejestru.

**Konsekwencje.**
- (+) Ta sama semantyka co magazyn in-memory; testy wyścigów (10 równoległych rerolli, 8 zakończeń medytacji, 6 przełomów) przechodzą na obu.
- (−) Więcej ponowień pod dużą kontencją. Gorące ścieżki zmierzy test obciążeniowy (Etap 8); w razie potrzeby obniżymy izolację punktowo, z jawnym blokowaniem.
