# Jianghu Go — MVP 1.5.0

Data: 2026-10-19. Wersja: **1.5.0** (aplikacja Android `pl.bz.jianghugo`, kod wersji 9). Poprzednie: 1.4.0, 1.3.0, 1.2.0, 1.1.x, 1.0.0-mvp.

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
| Walka i wyzwania (1.4) | Zapowiedź ruchu przeciwnika (serwer pokazuje, co NPC zrobi w tej turze; auto-walka na to odpowiada), rozpęd (seria trafień +8% obrażeń na stos, maks. 3) i wykorzystanie słabości (+15% w krwawiącego/osłabionego/ogłuszonego), Wieża Prób (50 pięter, strażnicy co 10, nagrody za pierwsze przejście, ranking), Boss Świata (wspólna pula HP na tydzień, 3 próby dziennie, Qi za obrażenia, nagroda za ubicie, ranking obrażeń), czat sekty (członkowie, limit 5 wiadomości / 30 s, retencja 30 dni) |
| Oprawa (1.4) | Osobny arkusz bohatera dla każdej ścieżki (ta sama postać we wszystkich ruchach: postawa, cios, technika, specjał, garda, trafienie, porażka, zwycięstwo, medytacja), szarża do przeciwnika, błysk trafienia, wstrząs ekranu, aury ścieżek, efekty świetlne z wideo (leczenie, trafienia krytyczne, statusy, piorun, deszcz, zwycięstwo, niskie HP), mniejsze postacie na arenie (głębia), nowe ikony technik i menu w jednym stylu, czytelny krój tekstu |
| Postać (1.3) | Premie każdej ścieżki w walce (Qi: więcej Qi i regeneracja; Ciało: HP, gardy, leczenie; Płomień: oparzenia; Trucizna: szansa efektów), opisy premii cech pochodzenia/krwi/konstytucji, tytuły z premiami do cech, poziomy Alchemii i Kowalstwa (szansa, rabat, receptury od poziomu) |
| Kultywacja (1.5) | Drabina 66 królestw × 12 etapów w 6 erach (nazwy Ziemskich Gałęzi, hanzi, odblokowania, tytuły), Qi płynie samo 24/7 (×1), medytacja ×5, odosobnienie 8/12/24 h ×8, naczynie z rozbudową za kryształy, limit offline 12/24/72 h, kompresja Qi, dzienny przyspieszacz, weekend ×1,5, podsumowanie „gdy cię nie było”, 9 metod z mistrzostwem i podręcznikami, pigułka wąskiego gardła na 11. etapie każdej ery, Niebiańska Próba jako walka na 12. etapie, jakość fundamentu/rdzenia/duszy, kolor rdzenia, demony serca, oświecenie, karma i reinkarnacja, auto-przełom |
| Sztuki ścieżek (1.5) | 18 niebiańskich płomieni (odkrywanie: bestie, ślady żaru o różnych porach, pełnia, Wieża, Boss Świata, królestwa; walka z duchem płomienia, pochłanianie z ryzykiem poparzenia, karmienie, gniazda, fuzje, premie do alchemii i kuźni, kodeks z tytułami), 16 jadów i Ciało Toksyn (20 poziomów, ryzyko zatrucia, odtrutki), powłoki na broń, gniazdo Gu (5 rodzajów, wzrost w czasie, mutacje rang), hartowanie ciała +1…+99 (kroki, przyjęte ciosy, kąpiele, pigułki progów) widoczne jako „+N” przy imieniu, Intencja Miecza ⚔1…⚔9, 3 gałęzie każdej ścieżki w 3 stopniach |
| Tytuły i zadania (1.5) | Wybrany tytuł nad postacią (kolor rzadkości, widoczny w walce i na arenie), tytuły z osiągnięć, królestw, płomieni, jadów, hartowania, ligi areny i wydarzeń; 62 osiągnięcia, w tym 15 tajnych z zagadkami; 8 wydarzeń sezonowych; 12 rozdziałów opowieści z nagrodami |
| Automatyzacja (1.5) | Posiadłość: ogród (3–9 poletek, 3 uprawy), uczniowie (do 6, talenty, poziomy) i 10 wypraw z łupami; kolejka kotła i kuźni (partie do 50, kończą się offline) |
| Walka i świat (1.5) | Arena rankingowa (Elo, 6 lig, walka z kopią gracza, nagroda tygodnia), towarzysz walczy obok, pięć żywiołów, regiony z nazwami, terenem, pogodą, dniem i nocą oraz władcą regionu, ukryte miejsca, nowe miejsca (trujące bagna, pomniki miecza, pogorzeliska, ołtarze słońca, studnie księżyca) |
| Ekwipunek i sekty (1.5) | Losowe właściwości przedmiotów (stałe, zależne od rzadkości), 8 zestawów z premiami, rozbieranie na materiały; budynki sekty (premie dla członków), misje tygodnia, boss sekty z pieczęciami i nagrodą dla walczących |
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
