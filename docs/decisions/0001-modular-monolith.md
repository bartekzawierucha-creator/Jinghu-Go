# ADR 0001 — Modularny monolit

**Status:** przyjęta (Etap 1)

**Kontekst.** Specyfikacja wymaga docelowego MMO, ale też zakazuje mikroserwisów bez potrzeby. Jeden zespół, wczesny etap, transakcje obejmujące wiele obszarów (np. reroll = szkic + rejestr żetonów + idempotencja + audyt).

**Decyzja.** Jeden proces ASP.NET Core, moduły jako osobne projekty z jawnie dozwolonymi referencjami (test `ArchitectureTests`). Jedna baza PostgreSQL ze schematem na moduł (`identity`, `characters`, `platform`, …).

**Konsekwencje.** Transakcje lokalne zamiast sag. Wydzielenie usługi później = zamiana wywołań in-process na zdarzenia z outboxa; dlatego moduły nie czytają cudzych tabel.
