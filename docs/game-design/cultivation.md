# Kultywacja (Etap 3)

Liczby: `content/game-config/cultivation.json` (`cultivation-1`), walidowane przy starcie (`CultivationConfigValidator`). Czas świata: `Europe/Warsaw`.

## Królestwa i etapy
Kondensacja Qi → Ustanowienie Fundamentu → Formowanie Rdzenia, po 9 etapów. Przejście z etapu 9 do następnego królestwa to **przełom główny**.
Qi potrzebne do przełomu z pozycji *i* (liczonej od 0 na całej drabinie): `100 × 1.2^i`, ×3 dla przełomu głównego (zaokrąglenie do całości). Przykłady: 100, 120, 144 … etap 9 KQ = 1290.

## Szansa przełomu
- Zwykły: `0.92 − 0.01 × i`; główny: `0.55`.
- Bonus ze statystyk: `insight × 0.004 + willpower × 0.003 + fate × 0.002`.
- Przycięcie do `[0.05, 0.97]` — nigdy pewność, nigdy beznadzieja.
- Rzut serwera: `roll ∈ [0, 1 000 000)` z `DeterministicRandom(seed)`; sukces gdy `roll < szansa_ppm`. Seed, rzut i szansa zapisane w `cultivation.breakthroughs` — każdą próbę można odtworzyć.
- **Porażka:** utrata 30% wymaganego Qi i **odchylenie Qi** na 30 min: brak przełomów, medytacja daje 50%.
- Ponowienie żądania z tym samym `Idempotency-Key` zwraca zapisany wynik — nie da się „przelosować” porażki.

## Źródła Qi
| Ścieżka | Mechanika |
|---|---|
| Qi | medytacja ×1.5 |
| Ciała | medytacja ×0.75 + **kroki**: 20 kroków = 1 Qi; maks. 220 kroków/min od ostatniego raportu (pierwszy raport: maks. 1 h), maks. 30 000 kroków/dzień. Nieprawdopodobne liczby są przycinane i odnotowywane w audycie (`cultivation.steps_clipped`) — bez kar za pojedynczy raport. |
| Płomienia | medytacja ×1.0. **12 stałych punktów geograficznych — Etap 5 (wymaga modułu Geolocation).** Świadome odstępstwo od pełnej specyfikacji. |
| Trucizn | medytacja ×1.0, **×2 w godz. 21:00–05:00** czasu świata (liczone minuta po minucie, więc sesja przez 21:00 dostaje bonus tylko za część nocną) |

**Medytacja:** start i koniec mierzone zegarem serwera; liczą się pełne minuty, maks. 120 min na sesję, 1500 Qi z medytacji dziennie (dzień świata). Bazowo `2 Qi/min × (1 + spirit × 0.02) × mnożnik ścieżki`.

## Tempo (do strojenia w Etapie 8 — symulator balansu)
Postać ze `spirit 10` na ścieżce Qi zbiera ~432 Qi z 2 h medytacji; dzienny limit z medytacji to 1500 Qi. Przy samym limicie medytacji (bez porażek i bez innych źródeł z przyszłych etapów):

| Królestwo | Suma Qi na 9 przełomów | Dni przy 1500 Qi/dzień |
|---|---|---|
| Kondensacja Qi | 2 940 | ~2 |
| Ustanowienie Fundamentu | 15 170 | ~10 |
| Formowanie Rdzenia | 78 271 | ~52 |

Trzecie królestwo jest celowo długie — przyszłe źródła Qi (świat, walka, alchemia) mają je skracać. Wartości do weryfikacji symulatorem.
