# Jianghu Go — MVP 1.3.0

Data: 2026-10-13. Wersja: **1.3.0** (aplikacja Android `pl.bz.jianghugo`, kod wersji 7). Poprzednie: 1.2.0, 1.1.x, 1.0.0-mvp.

MVP to grywalna wersja całej pętli gry na Androidzie: od narodzin postaci, przez kultywację, eksplorację z GPS i walki, po ekonomię, sekty i wojny o terytoria. Działa w dwóch trybach: **offline** (serwer gry wbudowany w aplikację, jeden gracz) i **z serwerem** (wielu graczy, PostgreSQL). Wszystkie reguły liczy serwer — klient tylko pokazuje stan i wysyła intencje.

## Zakres

| Obszar | Co jest w MVP |
|---|---|
| Konto | Rejestracja, logowanie, sesje z odświeżaniem tokenu; w trybie offline konto lokalne tworzone automatycznie |
| Postać | Losowanie cech i atrybutów, blokady, ponowne losowanie za Żetony Losu, 4 ścieżki kultywacji (Qi, Ciało, Płomień, Trucizna), portret zależny od ścieżki |
| Kultywacja | Medytacja z naczyniem Qi (postęp z trwającą sesją, czas do 100%, limity), przełomy z efektem niebiańskiej próby, odchylenie Qi, 3 królestwa z emblematami, kroki z licznika telefonu dla Ścieżki Ciała, nocny bonus Trucizny |
| Świat | Eksploracja z GPS i weryfikacją pozycji (antycheat: skoki, prędkość, dokładność), malowana mapa z medalionami (źródła Qi, zioła, bestie), 12 miejsc Płomienia z emblematami i dostrajaniem, wydarzenia świata |
| Walka | Turowe pojedynki 1v1 na animowanej scenie (sprite'y, efekty, liczby obrażeń, wynik): PvE (manekin, wilk, bandyta, bestie ze świata) i PvP (wyzwania członków sekty, przyjmowanie z powiadomień), 5 aren, zestaw 8 technik do edycji |
| Laboratorium Dao | Tworzenie własnych technik w budżecie mocy, zwoje technik, nauka ze zwojów |
| Ekonomia | Sakwa z ikonami, ekwipunek, pigułki, wyrzucanie, kuźnia i alchemia, ulepszanie +1…+15, targ z depozytem, opłatą i podatkiem |
| Sekty i terytoria | Zakładanie, prośby, role (przywódca/starszy/uczeń), wyrzucanie, awanse, przekazanie przywództwa, skarbiec i wpłaty, siedziba, zajmowanie terytoriów, wojny, danina |
| Powiadomienia | Ekran wieści z ikonami, wyzwania PvP do przyjęcia |
| Treść (1.1) | 42 cechy, 32 techniki, 16 przeciwników w 5 poziomach zagrożenia (w tym 4 bossów w legowiskach świata), 64 przedmioty, 38 receptur, 6 królestw, 22 rodzaje miejsc na mapie, 10 wydarzeń |
| Rozwój (1.1) | Dzienne zadania (3 na dzień), dzienny meldunek z serią, 13 osiągnięć z tytułami, meridiany (punkt za każdy etap + premia królestwa), duchowy towarzysz (oswajanie bestii, poziomy, premie), rankingi (kultywacja, PvP, bossowie, zwycięstwa) |
| Gracze w okolicy (1.1) | Lista kultywatorów w promieniu 2 km z przybliżoną odległością (bez współrzędnych) i wyzwaniem na pojedynek |
| Wygląd (1.1) | Samouczek mistrza, awatar z kierunkiem z kompasu, mgła wojny, sylwetka bohatera ze slotami ekwipunku, okna łupu i przełomu, wygląd i aury każdego przeciwnika, efekty zależne od techniki, dźwięki |
| Interfejs (1.2) | Stała powłoka bez przewijania: pasek bohatera (portret, królestwo, Qi, kamienie, zadania/sakwa/wieści z licznikami), 5 sekcji w dolnym pasku (Kultywacja, Świat, Walka, Bohater, Sekta), listy na stronach przesuwanych w bok, szczegóły w panelach od dołu, samouczek wskazujący prawdziwe przyciski, kreator postaci w 3 krokach, czcionka Cinzel, wibracje i dźwięki |
| Walka (1.3) | Bez dystansu — każdy atak sięga; slot ruchu zastąpiła umiejętność specjalna (cios, leczenie, unik, osłona, efekt; każda ścieżka ma własną), walka automatyczna (serwer gra turę za gracza), polowanie idle: walki co 5 min przez 4 h także przy zamkniętej grze, z prawdziwymi nagrodami w dziennym limicie |
| Postać (1.3) | Premie każdej ścieżki w walce (Qi: więcej Qi i regeneracja; Ciało: HP, gardy, leczenie; Płomień: oparzenia; Trucizna: szansa efektów), opisy premii cech pochodzenia/krwi/konstytucji, tytuły z premiami do cech, poziomy Alchemii i Kowalstwa (szansa, rabat, receptury od poziomu) |
| Tryb offline | Pełny serwer w aplikacji, stan zapisywany do pliku i odtwarzany po ponownym uruchomieniu |

## Jak zostało sprawdzone
- Serwer: testy jednostkowe i integracyjne na PostgreSQL i in-memory (w tym zapis/odczyt migawki stanu offline na wszystkich tabelach), testy SQL, ćwiczenie backup/restore, test obciążeniowy — CI `ci` zielone.
- Klient: rdzeń klienta testowany z prawdziwym serwerem (m.in. prognoza medytacji zgodna z tym, co serwer przyznaje).
- Android: CI `android-native-apk` buduje APK i przechodzi grę (w 1.1 także samouczek, meldunek, zadania, meridiany, rankingi, towarzysz i ekran postaci) na emulatorze Androida 14 bez serwera: tworzenie postaci, medytacja, eksploracja z emulowanym GPS, walka, sakwa, zestaw technik, miejsca Płomienia, powiadomienia, ponowne uruchomienie z zachowanym postępem. Od 1.2 test idzie na trzech rozmiarach ekranu (16:9, 18.5:9, 20:9) i odrzuca każdy ekran, który wymagałby przewijania albo ucina tekst (samokontrola układu w aplikacji). Zrzuty ekranu z każdego przebiegu: pre-release `android-smoke`.

## Czego MVP nie obejmuje (świadomie)
- Testów na fizycznych telefonach i profilowania baterii/GPS — następny krok przed szerszym udostępnieniem.
- Wydania w Google Play: APK jest podpisany kluczem debug, ruch `http://` w sieci lokalnej jest dozwolony, token logowania leży w niezaszyfrowanych ustawieniach aplikacji (blokada B4).
- Pozostałe blokady publicznego wydania z [raportu gotowości](release-readiness.md): opóźnienia walki pod obciążeniem (B2), atestacja urządzeń przeciw fałszywemu GPS (B6), moduł AntiAbuse (B7), usuwanie konta (B8), push i czat (B9), dokumenty prawne (B13).
- Klient Unity — zastąpiony w MVP natywnym klientem Android; pozostaje w repozytorium jako alternatywa.

## Instalacja
1. Pobierz `JianghuGo-dev.apk` z wydania **v1.3.0** (telefony ARM64; `JianghuGo-dev-arm32.apk` dla starszych 32-bitowych).
2. Zezwól na instalację z nieznanego źródła i zainstaluj.
3. Graj offline od razu, albo uruchom serwer (`API_BIND=0.0.0.0 docker compose up --build`) i wpisz jego adres na ekranie logowania.
