# Świat i eksploracja GPS (Etap 5)

Reguły: `content/game-config/world.json` (`world-1`) i `geolocation.json` (`geo-1`).

## Sesja eksploracji
Klient wysyła partie próbek GPS (≤ 20, co 5–15 s). Serwer ocenia każdą:

| Wynik | Kiedy | Skutek |
|---|---|---|
| `accepted` | poprawna, dokładność ≤ 50 m | pozycja i dystans aktualizowane |
| `accepted_low_accuracy` | dokładność > 50 m | pozycja widoczna, bez nagród, bez dystansu |
| `relocated` | prędkość > 60 m/s po odjęciu promieni błędu obu próbek | pozycja przeniesiona, **nagrody wstrzymane na 5 min**, +1 anomalia |
| `invalid` / `future` / `stale` / `out_of_order` / `rate_limited` | złe współrzędne/dokładność, czas > 30 s w przyszłości lub > 120 s wstecz, duplikat lub starsza próbka, > 30 próbek/min | próbka odrzucona |

Pozycja jest **potwierdzona** (wolno za nią nagradzać), gdy: sesja aktywna i nieoznaczona, ostatnia próbka ≤ 60 s temu, dokładność ≤ 50 m, brak okresu przenosin, prędkość ≤ 10 m/s.
5 anomalii w sesji → sesja oznaczona (bez nagród do jej końca) + wpis audytu dla zespołu anty-nadużyć. **Nigdy automatycznej kary dla konta za pojedynczy pomiar.**
Sesja kończy się po 10 min bez próbek lub po 4 h.

## Świat proceduralny
- Mapa podzielona na komórki 0,005° (~550 m). W każdej 0–3 punkty, losowane z **tajnego** seeda świata, okresu (6 h) i komórki — nic nie jest zapisywane poza „zebraniami”.
- Rodzaje: Źródło Qi (15 Qi, 50%), Duchowe Zioło (10 Qi, 35%), Duchowa Bestia (walka z Szarym Wilkiem, 15%).
- Interakcja: potwierdzona pozycja ≤ 40 m od punktu, punkt istnieje w bieżącym okresie (serwer go odtwarza — klucza nie da się sfałszować), raz na postać, maks. 60 interakcji/h.
- Wydarzenia regionalne (~5,5 km, okresy 2 h, 15% szans): Burza Qi ×2, Błogosławieństwo ×1,5 nagród.
- Zioła na razie dają Qi; jako materiały alchemiczne trafią do ekwipunku w Etapie 6.

## Ścieżka Płomienia — 12 miejsc
Wudang, Shaolin, Emei, Hua, Tai, Kajlas, Fudżi, **Smocza Jama na Wawelu**, Etna, Teide, Kīlauea, Uluru. Dostrojenie: potwierdzona pozycja ≤ 150 m, raz na 20 h na miejsce: **250 Qi** dla Płomienia, 40 Qi dla innych ścieżek. To pielgrzymki — na co dzień Płomień kultywuje medytacją.

## Nagrody
Qi z punktów i Płomienia płynie zdarzeniem `world.qi_reward` przez outbox do Kultywacji (jednokrotnie, `cultivation.reward_grants`). Bestie uruchamiają walkę PvE w tej samej transakcji co zebranie punktu.
