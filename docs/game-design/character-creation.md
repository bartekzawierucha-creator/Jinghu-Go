# Tworzenie postaci (Etap 2)

Wszystkie liczby: `content/game-config/character-creation.json` (wersja reguł `char-creation-1`). Walidacja przy starcie serwera i w testach (`CharacterConfigValidator`).

## Przepływ
1. `POST /characters/drafts` — serwer losuje szkic (darmowo). Pierwszy szkic na koncie przyznaje **10 Żetonów Losu** (jednorazowo, wpis w rejestrze).
2. `POST /drafts/{id}/reroll` — ponowne losowanie niezablokowanych cech i wszystkich statystyk. Koszt = `1 + 2 × liczba blokad`, maks. 2 blokady (z 3 kategorii), maks. 50 rerolli na szkic.
3. `POST /drafts/{id}/finalize` — imię (2–24 znaki: litery dowolnego alfabetu, pojedyncze spacje, `-`, `'`; unikalne bez względu na wielkość liter) + ścieżka kultywacji: `qi`, `body`, `flame`, `poison`. Limit 3 postaci na konto.

## Losowanie (deterministyczne)
Dla każdej kategorii w kolejności `background → bloodline → constitution`: rzadkość wg wag (`common 600, rare 250, epic 110, legendary 35, mythic 5` ≈ 60/25/11/3.5/0.5%), potem cecha jednostajnie spośród cech tej kategorii i rzadkości (sortowanie po id). Losowanie zachodzi także dla kategorii zablokowanych (wynik odrzucany), by blokada nie przesuwała strumienia losowego pozostałych.
Statystyka = `base + U[-variance, +variance] + Σ modyfikatorów cech`, przycięta do `[min, max]`.

## Statystyki
`strength, spirit (Qi), agility, constitution, insight, willpower, focus, elementalAffinity, fate` — bazowo 10±3 (afinitet i los 5±2), zakres 1–40 (0–40 dla afinitetu i losu).

## Decyzje projektowe (wartości domyślne do strojenia)
- Reroll kosztuje zawsze ≥ 1 (walidator odrzuca darmowe rerolle — inaczej nieskończone polowanie na cechy mityczne).
- Blokada kosztuje więcej niż reroll, bo celowanie w konkretną kombinację mityczną jest wykładniczo cenniejsze.
- Żetony Losu są osobną walutą w module Characters; ogólny portfel i rejestr księgowy powstaną w Etapie 6 i mogą je wchłonąć.
