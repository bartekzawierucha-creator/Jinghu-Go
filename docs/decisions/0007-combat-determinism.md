# ADR 0007 — Walka: deterministyczny silnik, intencje, commit-reveal

**Status:** przyjęta (Etap 4)

**Decyzja.**
1. Reguły walki to czysta funkcja `CombatEngine.ResolveTurn(config, combatants, state, intentA, intentB, rng)` — bez zegara, I/O ani liczb zmiennoprzecinkowych.
2. Klient wysyła wyłącznie **intencję** (numer tury, slot, kierunek). Nieznane pola JSON są odrzucane (400).
3. Każda tura ma własny RNG `TurnRng(seed, turn)`; AI NPC korzysta z osobnego strumienia. Zapis: zamrożone combatanty, intencje, zdarzenia i stan po każdej turze (`combat.turns`, append-only, PK `(battle_id, turn)` = tura rozstrzyga się raz).
4. Commit-reveal: `seed_hash` publikowany na starcie, `seed` po zakończeniu.
5. Jedna akcja na stronę na turę (PK `(battle_id, turn, side)`), zgodność numeru tury, idempotencja kluczem, SERIALIZABLE.
6. Upływ czasu (terminy tur, wygasłe wyzwania) stosowany leniwie przy każdym odczycie/zapisie walki — rozłączenie nie unieważnia walki, 3 przegapione tury = walkower. Brak potrzeby zegarowego zadania w tle.

**Konsekwencje.** Każdy spór („nie trafiłem!”) można rozstrzygnąć odtworzeniem. Zmiana reguł = nowa `rulesVersion` + aktualizacja testu złotej walki; stare walki odtwarza się regułami swojej wersji (do zachowania historycznych plików reguł przed pierwszą zmianą).
