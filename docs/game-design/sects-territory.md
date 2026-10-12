# Sekty i terytoria (Etap 7)

Konfiguracja: `sects.json` (`sects-1`), `territory.json` (`territory-1`).

## Sekty
- **Założenie**: 500 KD z portfela założyciela, nazwa 3–24 znaki (litery, cyfry, pojedyncze spacje, myślniki, apostrofy), unikalna bez względu na wielkość liter i **nigdy nie używana ponownie** (także po rozwiązaniu). Postać należy do najwyżej jednej sekty (klucz główny w bazie).
- **Role**: przywódca, starszy, uczeń. Uprawnienia (kod `SectPolicy`, nie konfiguracja — błąd w tabeli uprawnień to błąd bezpieczeństwa):

| Działanie | Przywódca | Starszy | Uczeń |
|---|---|---|---|
| decyzje o prośbach | ✅ | ✅ | — |
| usunięcie członka (tylko niższej rangi) | ✅ | ✅ (uczniów) | — |
| nadawanie ról, przekazanie przywództwa | ✅ | — | — |
| siedziba | ✅ | — | — |
| zajmowanie terytoriów, wojny, danina | ✅ | ✅ | — |
| wpłaty do skarbca, odejście | ✅ | ✅ | ✅ |

- **Prośby**: wiadomość ≤ 200 znaków, najwyżej 3 oczekujące na postać, jedna na sektę; przyjęcie do jednej sekty wycofuje pozostałe. Odpowiedź trafia do kandydata jako powiadomienie.
- **Wkłady**: wpłata KD z portfela do **skarbca** (osobny niezmienny rejestr z triggerem, jak portfele) w jednej transakcji; wkład członka rośnie o kwotę.
- **Siedziba**: przywódca ustanawia ją tam, gdzie **stoi** (zweryfikowana pozycja). Zapisywana z dokładnością do 3 miejsc po przecinku (~100 m), bo jest publiczna; koszt 300 KD ze skarbca, przeniesienie co 7 dni. Historia sekty nie zawiera współrzędnych.
- **Odejście**: przywódca musi najpierw przekazać przywództwo, chyba że jest ostatni — wtedy sekta zostaje rozwiązana (skarbiec przepada, nazwa pozostaje zajęta).
- **Historia** (`sects.events`, tylko dla członków) i audyt rejestrują każde przyjęcie, odrzucenie, usunięcie, zmianę roli, wpłatę i siedzibę.

## Terytoria
- Terytorium to stała komórka mapy 0,05° (~5 km). **Klucz zawsze wylicza serwer ze zweryfikowanej pozycji** działającej postaci — klient nie może wskazać odległego terytorium.
- **Zajęcie** niczyjego terytorium: przywódca lub starszy na miejscu, 200 KD ze skarbca. Limit: 1 + 1 na każdych 5 członków.
- **Wojna (GvG)**: przywódca lub starszy atakującej sekty, stojąc na terytorium innej sekty, wypowiada wojnę (500 KD ze skarbca). 12 h przygotowań, potem 24 h okna walki. Każdy **pojedynek PvP** zakończony w oknie między członkami obu sekt daje 1 punkt sekcie zwycięzcy — dokładnie raz na (wojnę, walkę). Remis wyniku wojny zostawia terytorium obrońcy. Na terytorium najwyżej jedna otwarta wojna; sekta atakuje najwyżej jedno terytorium naraz.
- **Rozstrzygnięcie** następuje przy pierwszym odczycie lub działaniu po zamknięciu okna (bez zadania w tle, które mogłoby się zatrzymać): zmiana właściciela + wpis do niezmiennej historii własności + powiadomienia dla przywódców obu sekt.
- **Danina**: przywódca lub starszy sekty-właściciela pobiera 50 KD do skarbca raz na 24 h z każdego terytorium.
- Terytoria rozwiązanej sekty są niczyje (można je zająć).
