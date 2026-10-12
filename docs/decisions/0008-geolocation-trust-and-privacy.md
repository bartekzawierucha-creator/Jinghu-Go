# ADR 0008 — Zaufanie do GPS, świat proceduralny, minimalizacja danych

**Status:** przyjęta (Etap 5)

**Decyzje.**
1. **Pozycja jest sygnałem, nie dowodem.** Serwer ocenia każdą próbkę (zakresy, zegar, kolejność, tempo, prędkość z uwzględnieniem promieni błędu). Podejrzana próbka nie daje nagrody; seria anomalii wyłącza nagrody w sesji i trafia do audytu. Brak automatycznych sankcji na koncie.
2. **Tylko ostatnia pozycja.** `geolocation.sessions` przechowuje ostatni zaakceptowany punkt (`geography(Point,4326)`, indeks GiST) i agregaty; przy zakończeniu sesji punkt jest kasowany (wymuszone CHECK-iem w bazie). Surowe próbki nie są zapisywane nigdzie.
3. **Świat jako funkcja.** Punkty i wydarzenia = f(tajny seed, okres, komórka). Zero danych o spawnach w bazie, skalowalne globalnie; zapisujemy tylko zebrania (klucz komórki ≈ lokalizacja z dokładnością ~550 m → retencja 7 dni).
4. **Brak lokalizacji w logach i audycie.** Klucze punktów idą w ciele żądania (nie w URL logowanym przez serwer), audyt zapisuje „co”, nie „gdzie”.
5. **Geometria w aplikacji, PostGIS w bazie.** Odległości liczy `GeoMath.DistanceM` (haversine, sfera R = 6 371 008,7714 m) w obu magazynach; test SQL potwierdza zgodność z `ST_Distance(geography, false)` (< 1 cm na 2000 losowych par, < 1 mm na krótkich dystansach).
6. Seed świata to sekret (`World__Seed`), wymagany przy starcie; bez niego mapę dałoby się wyliczyć offline.
