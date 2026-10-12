# Walka (Etap 4)

Reguły: `content/game-config/combat.json` (`combat-1`), walidowane przy starcie (`CombatConfigValidator`). Silnik: `CombatEngine` — czysta funkcja, arytmetyka całkowita (‰).

## Pojedynek
- 1v1, tury **jednoczesne**: obie strony wybierają intencję, serwer rozstrzyga turę jako oś czasu (timeline).
- Kolejność na osi: najpierw postawy/ruch/odpoczynek, potem ataki; w obrębie fazy wg inicjatywy = `agility + szybkość techniki + losowe 0–9`.
- **Dystans** 1–3 (start 2). Techniki ataku mają zasięg; ruch zmienia dystans o 1 i daje unik na tę turę.
- HP = `100 + 8 × constitution` (Ciało +25%), Qi = `30 + 3 × spirit`; regeneracja +5 Qi/turę, odpoczynek +15 Qi.
- Maks. 30 tur → zwycięża wyższy % HP. Termin na turę 90 s; brak akcji = odpoczynek; 3 przegapione tury z rzędu = walkower.

## Osiem slotów
4 × atak, 2 × obrona, 1 × ruch (Qinggong), 1 × kontra/kradzież Qi. Zestaw wybiera gracz (`PUT …/combat/loadout`) spośród technik znanych ścieżce: uniwersalne + technika sygnaturowa ścieżki (Fala Mieczowego Qi, Żelazna Pięść, Płomienna Dłoń, Zatruta Igła).

## Formuły
- Trafienie: `850‰ + 10‰ × (agility atak. − agility obr.) + celność techniki − unik obrońcy`, przycięte do 150–970‰.
- Obrażenia: `moc × (1 + 10% × (poziom−1)) × (1 + 3% × statystyka skalująca) × (1 − osłabienie) × (1 ± 10%) × (1 − blok)`, min. 1.
- Kontra: odbija ‰ obrażeń i kradnie Qi trafiającemu. Efekty: krwawienie/trucizna (obrażenia na koniec tury), ogłuszenie (utrata następnej akcji), osłabienie (‰ mniej obrażeń).

## Techniki 1–10
Każde wykonane (nie wymuszone) użycie techniki = 1 XP; poziom *n* → *n+1* kosztuje `10 × n` XP. Poziom podnosi moc (+10%/poziom).

## Nagrody
- PvE: Qi za zwycięstwo (Manekin 5, Wilk 40, Bandyta 60), maks. 20 nagradzanych zwycięstw na 24 h. Qi dociera do kultywacji **przez outbox** (`combat.battle_finished` → handler Cultivation, jednokrotnie dzięki `cultivation.reward_grants`).
- PvP: bez nagród w Qi (zapobiega „win-tradingowi”); wynik + mastery technik + powiadomienia.

## Uczciwość i audyt
- Seed walki z CSPRNG; na starcie publikowany jest `seedHash = SHA-256(seed)`, po zakończeniu **ujawniany seed** (commit-reveal). Z seeda, zamrożonych statystyk i zapisanych intencji każdy może odtworzyć walkę — test `Pve_battle_runs_to_completion_reveals_seed_and_replays_exactly`.
- Decyzje NPC używają osobnego strumienia losowego, więc odtworzenie z zapisanych intencji nie zależy od AI.
- Test „złotej walki” (`Golden_battle_is_unchanged_for_rules_combat_1`) blokuje przypadkowe zmiany reguł: zmiana wyniku wymaga nowej wersji reguł.

## Balans
`dotnet run --project tools/balance-simulator -- 500` — macierz zwycięstw AI vs AI. Dla `combat-1` ścieżki startowe między sobą: 41–54%; startowa postać vs Szary Wilk ≈ 50–80%, vs Bandyta ≈ 48–59%.

## Odłożone (z uzasadnieniem)
- **Laboratorium Dao** (projektowanie własnych technik) i **handel zwojami** — wymagają rynku i ekonomii przedmiotów (Etap 6); walidator technik już istnieje i zostanie użyty dla technik autorskich.
- Animowany widok boczny Pixel Art — w kliencie jest prototyp tekstowy; protokół (`lastTurnEvents`) już niesie wszystko do animacji osi czasu.
