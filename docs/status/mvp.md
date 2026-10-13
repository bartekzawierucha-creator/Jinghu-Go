# Jianghu Go — MVP 1.0.0

Data: 2026-10-13. Wersja: **1.0.0-mvp** (aplikacja Android `pl.bz.jianghugo`, kod wersji 2).

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
| Tryb offline | Pełny serwer w aplikacji, stan zapisywany do pliku i odtwarzany po ponownym uruchomieniu |

## Jak zostało sprawdzone
- Serwer: testy jednostkowe i integracyjne na PostgreSQL i in-memory (w tym zapis/odczyt migawki stanu offline na wszystkich tabelach), testy SQL, ćwiczenie backup/restore, test obciążeniowy — CI `ci` zielone.
- Klient: rdzeń klienta testowany z prawdziwym serwerem (m.in. prognoza medytacji zgodna z tym, co serwer przyznaje).
- Android: CI `android-native-apk` buduje APK i przechodzi grę na emulatorze Androida 14 bez serwera: tworzenie postaci, medytacja, eksploracja z emulowanym GPS, walka, sakwa, zestaw technik, miejsca Płomienia, powiadomienia, ponowne uruchomienie z zachowanym postępem. Zrzuty ekranu z każdego przebiegu: pre-release `android-smoke`.

## Czego MVP nie obejmuje (świadomie)
- Testów na fizycznych telefonach i profilowania baterii/GPS — następny krok przed szerszym udostępnieniem.
- Wydania w Google Play: APK jest podpisany kluczem debug, ruch `http://` w sieci lokalnej jest dozwolony, token logowania leży w niezaszyfrowanych ustawieniach aplikacji (blokada B4).
- Pozostałe blokady publicznego wydania z [raportu gotowości](release-readiness.md): opóźnienia walki pod obciążeniem (B2), atestacja urządzeń przeciw fałszywemu GPS (B6), moduł AntiAbuse (B7), usuwanie konta (B8), push i czat (B9), dokumenty prawne (B13).
- Klient Unity — zastąpiony w MVP natywnym klientem Android; pozostaje w repozytorium jako alternatywa.

## Instalacja
1. Pobierz `JianghuGo-dev.apk` z wydania **v1.0.0-mvp** (telefony ARM64; `JianghuGo-dev-arm32.apk` dla starszych 32-bitowych).
2. Zezwól na instalację z nieznanego źródła i zainstaluj.
3. Graj offline od razu, albo uruchom serwer (`API_BIND=0.0.0.0 docker compose up --build`) i wpisz jego adres na ekranie logowania.
