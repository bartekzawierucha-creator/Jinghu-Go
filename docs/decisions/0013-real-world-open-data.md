# ADR 0013 — Prawdziwa pogoda i prawdziwe miejsca z otwartych danych

**Status:** przyjęta (2.1, 2026-10-20)

**Kontekst.** Świat był w pełni proceduralny: pogoda regionu i węzły komórek z tajnego ziarna. Gracze chcą prawdziwego świata — świętych miejsc, do których można dojść, i pogody za oknem.

**Decyzje.**
- **Pogoda**: Open-Meteo (`api.open-meteo.com`, bez klucza). Kod WMO bieżącej pogody regionu → pogoda gry (deszcz, mgła, śnieg, burza, upał powyżej 28 °C, inaczej pogodnie); temperatura widoczna na ekranie świata.
- **Miejsca**: OpenStreetMap przez Overpass (`overpass-api.de`). Świątynie i kapliczki, ruiny i zamki, pomniki, szczyty, źródła, wodospady i punkty widokowe stają się węzłami (trzy nowe rodzaje tylko z prawdziwego świata: Święte Miejsce, Święty Szczyt, Święty Wodospad; reszta korzysta z ruin, pomnika miecza, źródła Qi i studni księżyca). Najwyżej 60 na region.
- Prawdziwy węzeł stoi tam, gdzie miejsce; klucz `-<dzień UTC>.<region x>.<region y>.<id OSM>` — raz dziennie na postać, bez kolizji z kluczami proceduralnymi (te zaczynają się nieujemną epoką).
- **Prywatność**: usługi dostają tylko region (komórkę ~3 km: środek dla pogody, prostokąt dla miejsc) — nigdy pozycji gracza, konta ani postaci. W logach przy błędzie jest tylko komórka regionu.
- **Opóźnienia i odporność**: pamięć podręczna na region (pogoda 30 min, miejsca 24 h, błędy 10 min); brak w pamięci uruchamia odświeżenie w tle, a odpowiedź korzysta ze świata proceduralnego. Najwyżej dwa zapytania do Overpass naraz.
- Przełącznik `RealWorld:Enabled` (włączony na serwerze i w aplikacji offline, wyłączony w testach, które podstawiają własne źródło).

**Konsekwencje.** Świat proceduralny pozostaje wszędzie zapasem (brak internetu, awaria usługi, puste regiony). Serwer (i telefon w trybie offline) potrzebuje wychodzącego HTTPS do obu usług. Dane © współtwórcy OpenStreetMap (ODbL); pogoda Open-Meteo (CC BY 4.0) — podpis w oknie „Miejsca w okolicy”.
