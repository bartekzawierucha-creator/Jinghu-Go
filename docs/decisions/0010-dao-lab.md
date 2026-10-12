# ADR 0010 — Laboratorium Dao: budżet mocy, zamrożone definicje, zwoje jako przedmioty

**Status:** przyjęta (Etap 6+)

**Decyzje.**
1. **Walidacja = twarde limity + budżet.** Same limity nie wystarczą (kombinacja maksymalnych wartości), sam budżet też nie (skrajności typu 100% ogłuszenia). Wagi budżetu w konfiguracji; kalibracja pilnowana testem: każda oficjalna technika bez ścieżki mieści się w budżecie.
2. **Niezmienne definicje, zamrażane w walce.** `EquippedTechnique` niesie opcjonalną `TechniqueDefinition`; silnik pobiera ją przed konfiguracją (`CombatConfig.Technique(equipped)`). Stare walki odtwarzają się identycznie; brak migracji danych walk przy zmianach Laboratorium.
3. **Wiedza ≠ przedmiot.** Znane techniki to `combat.known_techniques`; zwój to unikalny przedmiot Inventory z niezmiennym `bound_ref`. Handel zwojami korzysta z istniejącego targu (depozyt, atomowy zakup) — zero nowej logiki handlu.
4. **Tylko autor spisuje zwoje.** Kupujący może się nauczyć, ale nie powielać cudzej techniki.

**Konsekwencje.** Liczba technik w świecie rośnie liniowo (≤ 6 na postać); definicje są małe (JSON). Rebalans oficjalnych technik nie zmienia autorskich — to celowe (gracze zachowują to, co stworzyli), ale zmiana wag budżetu nie działa wstecz.
