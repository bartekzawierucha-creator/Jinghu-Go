# Jianghu Go — Projekt rozwoju: Kultywacja, Idle, Ścieżki, Tytuły, Świat

Dokument projektowy wersji 1.5–1.8. Wszystkie reguły liczy serwer (jak dotąd): klient pokazuje stan i wysyła intencje. Wszystkie liczby są w plikach konfiguracji (game-config) i można je stroić bez zmiany kodu.

---

## 1. Rytm gry

Gra ma się rozwijać zawsze — także przy zamkniętej aplikacji — a każde wejście daje coś do odebrania i decyzję do podjęcia.

| Pora | Co robi gracz |
|---|---|
| Rano | Odbiera zbiór offline (podsumowanie), wyprawy, ogród, kocioł, Gu; ustawia kolejki na dzień |
| W ciągu dnia | Eksploracja z GPS: regiony, ukryte miejsca, zioła i jady z terenu, płomienie wędrujące, trening siłowy |
| Wieczorem | Wieża Prób, Boss Świata, arena, misje sekty, czat |
| Na noc | Odosobnienie albo łowy idle na 8 h |
| Codziennie | Zadania dzienne, meldunek, 1 darmowy Przyspieszacz (2 h kultywacji od razu) |

---

## 2. Qi i idle

### 2.1 Źródła Qi
| Źródło | Mnożnik | Uwagi |
|---|---|---|
| Przepływ pasywny | ×1 | Zawsze, 24/7, także offline |
| Medytacja | ×5 | Aktywna sesja; można przerwać w każdej chwili |
| Odosobnienie | ×8 | 8 / 12 / 24 h (24 h od Narodzin Duszy); w tym czasie brak walk i zbierania |
| Miejsce mocy | ×1,2–×2 | Medytacja w parku / nad wodą / w górach / w miejscu kultu |
| Walka, zadania, łowy | stałe kwoty | Jak dotąd |

Bazowy przepływ pasywny zależy od królestwa (rośnie razem z kosztem etapów, więc tempo jest stałe w odczuciu) i jest mnożony przez: jakość fundamentu/rdzenia/duszy, meridiany, metodę kultywacji, formację, korzeń duchowy, premie ścieżki i tytułów.

### 2.2 Naczynie Qi (dantian)
- Ma pojemność. Pełne naczynie zatrzymuje przepływ pasywny — trzeba wejść i zużyć Qi (etap, przełom, kompresja).
- Pojemność rośnie z królestwem i z ulepszeniami naczynia (osobna oś: Pigułki Rozszerzenia Dantianu, meridiany nadzwyczajne).
- Limit zbioru offline: 12 h (24 h od Daluo, 72 h od Przedwiecznego).

### 2.3 Podsumowanie offline
Po powrocie okno: „Przez 7 h 12 min: +1 240 Qi, łowy 84 walki (61 wygranych), 3× Duchowe Zioło, Gu Złodziej urósł do poz. 4, kocioł: 5× Pigułka Qi”. Jeden przycisk „Odbierz wszystko”.

### 2.4 Kompresja Qi
Nadmiar Qi (ponad naczynie) można skompresować w **Krystaliczne Qi** (100:1). Krystaliczne Qi:
- jest walutą przełomów i ulepszeń naczynia,
- daje trwałą premię pasywną +0,1% za każde 100 kryształów (do limitu na królestwo).

### 2.5 Miejsce kultywacji
Gracz wskazuje swój punkt kultywacji na mapie (raz dziennie można zmienić). Teren określa żywioł i premię:
- park/las → Drewno, +zioła,
- woda → Woda, +Qi nocą,
- wzgórza/góry → Ziemia, +hartowanie,
- centrum miasta → Metal, +kamienie duchowe,
- miejsca Płomienia → Ogień, +karmienie płomieni.

### 2.6 Formacje (szyki)
Układ 4–9 kamieni duchowych wokół punktu kultywacji. Lepsza formacja = większy mnożnik pasywny (×1,1–×1,6). Kamienie zużywają się z czasem (wymiana raz na kilka dni). Od Nieśmiertelnego Tajemnic formacje są wieczne.

---

## 3. Automaty (kolejki w tle)

| Automat | Od królestwa | Działanie |
|---|---|---|
| Łowy 2.0 | 3 | Wybór regionu, kolejka do 3 przeciwników, auto-leczenie pigułkami, raport |
| Ogród duchowy | 3 | 1→9 grządek; nasiona lotosu i ziół rosną 2–12 h; mutacje (1–3%) dają składniki legendarne |
| Kolejka alchemii | 4 | 3→10 slotów; pigułki gotują się same, czas zależy od poziomu Alchemii i płomienia |
| Kolejka kuźni | 6 | Przetapianie rud, ulepszanie w kolejce |
| Wyprawy zbierackie | 5 | Uczeń albo postać idzie na 1–8 h do odwiedzonego regionu po zioła/rudy/jady |
| Uczniowie | 5 | 1→5 uczniów (NPC); każdy ma talent (zbieracz, łowca, alchemik), poziom i lojalność; od Mędrca mogą osiągnąć Złoty Rdzeń |
| Towarzysz-zbieracz | 3 | Bestia przynosi łup zależny od gatunku (lis zioła, golem rudę, żmija jad) |
| Dusza na wyprawie | 6 | Dusza-niemowlę wędruje 2–12 h; wraca z Qi, łupem, czasem z oświeceniem |
| Klon | 16 | Drugie łowy idle równolegle |
| Domena | 18 | Własna baza z pawilonami produkującymi Qi, zioła, rudy, kamienie |
| Automatyczny przełom | 9 | Gdy naczynie pełne i szansa ≥ ustawiony próg, postać sama próbuje |
| Kocioł Jadów / Gniazdo Gu | Trucizna | Patrz 7.2 |
| Kąpiele hartujące | Ciało | Patrz 7.3 |
| Karmienie płomieni | Płomień | Patrz 7.1 |

Wszystkie automaty rozliczają się leniwie na serwerze przy odczycie (jak łowy w 1.3): wynik zależy tylko od czasu, stanu i ziarna — bez farmy po stronie klienta.

---

## 4. Struktura kultywacji

### 4.1 66 królestw × 12 etapów = 792 poziomy
66 królestw w 6 erach po 11. Każda era otwiera nową warstwę świata.

**12 etapów (Ziemskie Gałęzie):**

| Etap | Nazwa | Faza |
|---|---|---|
| 1 | Szczur | Wczesny I |
| 2 | Wół | Wczesny II |
| 3 | Tygrys | Wczesny III |
| 4 | Królik | Środkowy I |
| 5 | Smok | Środkowy II |
| 6 | Wąż | Środkowy III |
| 7 | Koń | Późny I |
| 8 | Koza | Późny II |
| 9 | Małpa | Późny III |
| 10 | Kogut | Szczyt — naczynie pełne |
| 11 | Pies | Półkrok — wąskie gardło (pigułka albo zadanie) |
| 12 | Świnia | Doskonałość — można próbować przełomu |

Im dłużej trzymasz Doskonałość (do 7 dni), tym wyższa jakość przełomu.

**Koszty i tempo:** koszt Qi ×1,15 na etap i ×1,6 na królestwo (zapis dużych liczb: K, M, B, T, aa, ab…). Przepływ pasywny rośnie proporcjonalnie, więc etap trwa: era I minuty–godziny, era II godziny, era III dzień, era IV–V dni, era VI tydzień+. Reinkarnacja (4.10) skraca drogę przy kolejnym przejściu.

### 4.2 Przełom i niebiańska próba
Przełom do nowego królestwa = walka z niebem (pioruny, ogień, demony, pustka — zależnie od królestwa). Przegrana: odchylenie Qi (−20% Qi, 1 h kary), można spróbować ponownie. Ścieżka Ciała może przyjąć dodatkowe pioruny dla hartowania.

### 4.3 Jakość fundamentu, rdzenia, duszy
Przy królestwach 4, 5 i 6 jakość: **zwykła / doskonała / niebiańska** — zależy od: pełnych meridianów, jakości pigułki, miejsca mocy, pory (pełnia, świt), czasu na Doskonałości. Niebiańska jakość: +15% do wszystkiego na zawsze. Tytuły za niebiańskie jakości.

### 4.4 Kolor rdzenia (Złoty Rdzeń)
Wyliczany ze stylu gry (statystyki z historii): Szkarłatny (walka, +obrażenia), Jadeitowy (alchemia/ogród, +zbiory), Srebrny (medytacja, +pasywne Qi), Fioletowy (noc/trucizny, +efekty), Złoty (równowaga, +wszystko trochę).

### 4.5 Demony serca
Szybka kultywacja (dużo pigułek, częste przełomy) podnosi niestabilność 0–100. Powyżej 70: szansa na odchylenie przy przełomie, powyżej 90: atak demona serca (walka). Obniżają: spokojna medytacja, Herbata Spokoju, dni bez pigułek. Wybór: tempo albo bezpieczeństwo.

### 4.6 Oświecenie
Rzadkie zdarzenie (0,5% na godzinę medytacji; ×3 w miejscu mocy, ×2 przy pełni): natychmiast +1 etap albo nowa technika albo +poziom Dao.

### 4.7 Metody kultywacji (podręczniki)
Zakładasz jedną. Poziom opanowania 1–10 rośnie z czasem kultywacji. Przykłady:

| Metoda | Efekt | Skąd |
|---|---|---|
| Sutra Spokojnej Wody | Pasywne Qi +30%, walka −10% | Start (Qi) |
| Księga Krwawego Księżyca | Qi z każdej wygranej, nocą ×2 | Boss, Trucizna |
| Kanon Żelaznego Ciała | Qi z kroków ×3, hartowanie +20% | Start (Ciało) |
| Pieśń Feniksa | Qi z miejsc Płomienia, odrodzenie raz na walkę | Wieża 25 |
| Traktat Tysiąca Zbiorów | Ogród i wyprawy +40%, medytacja −20% | Sekta |
| Zwój Burzowego Nieba | Próby łatwiejsze, Qi za przetrwane pioruny | Boss Świata |
| Sutra Pustego Serca | Demony serca −50%, Qi −10% | Ukryte miejsce |

Od Świętego Pierwotnego można tworzyć własne metody (Laboratorium Dao dla metod).

### 4.8 Meridiany 2.0
12 meridianów głównych + 8 nadzwyczajnych. Otwarcie kosztuje Qi i zioła. Połączone meridiany (łańcuchy) dają efekty łańcuchowe, np. Ręka Taiyin + Płuca: technika Qi tańsza o 1.

### 4.9 Korzeń duchowy i Dao
- **Korzeń duchowy** losowany przy narodzinach: jeden żywioł (szybki), dwa, trzy, pięć (wolny, ale wszechstronny), korzeń Niebiański (rzadki). Oczyszczanie rzadkimi pigułkami.
- **Dao** (od Transformacji Ducha): paski zrozumienia Miecza, Ognia, Wody, Ziemi, Wiatru, Cienia, Ciała, Życia. Rosną z użyciem technik danego typu, każdy poziom = pasywna premia.

### 4.10 Reinkarnacja i Karma
Od Doskonałego Świętego (33) można się odrodzić: powrót do królestwa 1 z **Karmą** (trwały mnożnik Qi, start z wyższym korzeniem, zachowane tytuły i kolekcje). Królestwo 66 kończy się Wielkim Wniebowstąpieniem: Reinkarnacja z wpisem do Kroniki Wieczności.

---

## 5. Wszystkie 66 królestw

### ERA I — Śmiertelna (świat: prawdziwe okolice)
| # | Królestwo | Co zmienia w grze | Próba | Tytuł |
|---|---|---|---|---|
| 1 | Hartowanie Ciała 煉體 | Medytacja, trening, pierwsza technika ścieżki | Cień własnego lęku | Hartowany |
| 2 | Otwarcie Meridianów 開脈 | 12 meridianów głównych, łańcuchy | Ból kanałów (3 tury bez Qi) | Otwarty Kanał |
| 3 | Kondensacja Qi 練氣 | Łowy idle, ogród (1 grządka), Wieża 1–10, towarzysz-zbieracz | 3 pioruny | Zbieracz Mgły |
| 4 | Założenie Fundamentu 築基 | Jakość fundamentu, 1. gałąź ścieżki, formacje, odosobnienie 8 h, kolejka alchemii | 6 piorunów | Kamień Węgielny |
| 5 | Złoty Rdzeń 金丹 | Kolor rdzenia, druga ścieżka, 1. uczeń, wyprawy, kompresja Qi | 9 piorunów + demon serca | Nosiciel Rdzenia |
| 6 | Narodziny Duszy 元嬰 | Dusza na wyprawach, odosobnienie 24 h, towarzysz w walce, kolejka kuźni | Powódź Qi | Dwa Serca |
| 7 | Transformacja Ducha 化神 | Dao, wpływ na pogodę w regionie (raz dziennie), 3. uczeń | Próba ognia | Widzący Prawa |
| 8 | Oczyszczenie Pustki 煉虛 | Teleport do odwiedzonych regionów, pierścień przestrzenny | Niewidzialny przeciwnik | Wędrowiec Pustki |
| 9 | Integracja Ciała 合體 | Qi z terytoriów sekty, automatyczny przełom, 2. gałąź, 4. uczeń | 3 demony serca naraz | Jedno z Niebem |
| 10 | Wielki Pojazd 大乘 | Nauczanie (sekta +10% szybciej), odłam sekty, 5. uczeń | 18 piorunów | Mistrz Pojazdu |
| 11 | Przejście Próby 渡劫 | Wielka Próba 9×9 jako wydarzenie serwera (inni widzą i wspierają) | 81 piorunów | Ten, Który Przetrwał Niebo |

### ERA II — Nieśmiertelna (świat: Niebiańskie Wyspy nad mapą)
| # | Królestwo | Co zmienia w grze | Próba | Tytuł |
|---|---|---|---|---|
| 12 | Nieśmiertelny Ziemski 地仙 | Qi Nieśmiertelne (1:1000), niebiańskie pola | Zrzucenie śmiertelnego ciała | Ziemski Nieśmiertelny |
| 13 | Prawdziwy Nieśmiertelny 真仙 | Odrodzenie raz na walkę (30% HP) | Wyrok niebios | Prawdziwy |
| 14 | Nieśmiertelny Tajemnic 玄仙 | Wieczne formacje | Labirynt formacji | Tkacz Szyków |
| 15 | Złoty Nieśmiertelny 金仙 | Odporność na jeden wybrany status | Złoty ogień | Złote Ciało |
| 16 | Nieśmiertelny Taiyi 太乙 | Klon: 2 łowy równolegle | Walka z klonem | Dwoisty |
| 17 | Nieśmiertelny Daluo 大羅 | Zbiór offline do 24 h | Pętla czasu | Poza Czasem |
| 18 | Król Nieśmiertelnych 仙王 | Domena z pawilonami produkcji | Najazd na domenę | Król Domeny |
| 19 | Cesarz Nieśmiertelnych 仙帝 | Domena wzmacnia sektę w okolicy | Bunt wasali | Cesarz Wysp |
| 20 | Nieśmiertelny Źródła 源仙 | Studnia Qi w domenie (10 poziomów) | Wysychające źródło | Strażnik Źródła |
| 21 | Pół-Święty 半聖 | Pierwsza Zasada walki (1 z 3, np. pierwsza tura zawsze twoja) | Sprzeczność zasad | Pół-Święty |
| 22 | Szczyt Nieśmiertelnych 仙極 | Zamknięcie ery | Walka z echem siebie z ery I | Pan Nieba |

### ERA III — Święta (świat: Ziemie Święte, rajdy kooperacyjne)
| # | Królestwo | Co zmienia w grze | Próba | Tytuł |
|---|---|---|---|---|
| 23 | Pierwszy Krok Świętego 聖人 | Święta Wola: jedno zdanie-prawo dające premię sekcie | Próba słowa | Głos Prawa |
| 24 | Święte Ciało 聖體 | Regeneracja 2% HP na turę | Krew nieba | Święte Ciało |
| 25 | Święta Dusza 聖魂 | Dusza walczy obok (pełny ruch co 2 tury) | Rozdarcie duszy | Podwójny Wojownik |
| 26 | Święty Prawa 法聖 | Druga Zasada walki | Zderzenie praw | Prawodawca |
| 27 | Wielki Święty 大聖 | Rajdy 5-osobowe na Święte Bestie | Bestia Granic | Pogromca Bestii |
| 28 | Święty Pierwotny 元聖 | Tworzenie własnych metod kultywacji | Pustka bez metody | Twórca Sutry |
| 29 | Święty Mędrzec 聖賢 | Uczniowie mogą osiągnąć Złoty Rdzeń | Próba ucznia | Mędrzec |
| 30 | Święty Cesarz 聖帝 | Bitwy sekt o Święte Góry (kontrola tygodniowa) | Oblężenie góry | Cesarz Gór |
| 31 | Święty Niebios 天聖 | Głosowanie nad Bossem Świata tygodnia | Gniew niebios | Wybraniec Niebios |
| 32 | Wieczny Święty 永聖 | Trzecia Zasada walki | Erozja wieczności | Wieczny |
| 33 | Doskonały Święty 至聖 | Odblokowana Reinkarnacja; Próba Serca Świata (wybory fabularne zmieniają przydomek) | Serce Świata | Święty |

### ERA IV — Boska (świat: Panteon i gwiazdozbiory)
| # | Królestwo | Co zmienia w grze | Próba | Tytuł |
|---|---|---|---|---|
| 34 | Iskra Boskości 神火 | Wyznawcy: gracze niższych królestw modlą się do ciebie (ty: Wiara, oni: błogosławieństwo) | Pierwsza modlitwa | Iskra |
| 35 | Bóstwo Pomniejsze 小神 | Świątynia w realnym punkcie mapy; odwiedzający dają Wiarę | Pusta świątynia | Opiekun Miejsca |
| 36 | Bóstwo Żywiołu 元素神 | Domena Boża: wybrany żywioł +30% | Bunt żywiołów | Bóg Żywiołu |
| 37 | Prawdziwy Bóg 真神 | Pierwsza boska technika (ultimate) | Zstąpienie | Prawdziwy Bóg |
| 38 | Bóg Urzędu 神職 | Wybór urzędu: Wojna, Los, Żniwa, Śmierć, Wiedza — unikalna rola na serwerze | Sąd urzędu | Bóg (Urząd) |
| 39 | Bóg Prawa 法神 | Czwarta Zasada | Kodeks niebios | Strażnik Kodeksu |
| 40 | Bóg Gwiazd 星神 | Gwiazdozbiór: drzewko 36 gwiazd pasywnych | Spadająca gwiazda | Gwiezdny |
| 41 | Bóg Czasu 時神 | Codziennie 4 h kultywacji natychmiast | Zatrzymany czas | Pan Godzin |
| 42 | Starszy Bóg 古神 | Panteon: gildia bogów ponad sektami | Rada bogów | Starszy |
| 43 | Bóg nad Bogami 神上神 | Druga boska technika | Bóg-uzurpator | Nad Bogami |
| 44 | Doskonałe Bóstwo 至神 | Zmierzch Bogów — wydarzenie serwera | Zmierzch | Bóg |

### ERA V — Pierwotna (świat: Morze Chaosu, wyprawy proceduralne)
| # | Królestwo | Co zmienia w grze | Próba | Tytuł |
|---|---|---|---|---|
| 45 | Dziecię Chaosu 混沌子 | Wyprawy w Morze Chaosu (roguelike, losowe piętra) | Pierwszy sztorm | Dziecię Chaosu |
| 46 | Kowal Chaosu 混沌匠 | Artefakty Pierwotne: rosną razem z tobą | Kuźnia bez ognia | Kowal Pierwotny |
| 47 | Władca Żywiołów 五行主 | Wszystkie 5 żywiołów naraz | Cykl niszczenia | Pięć w Jednym |
| 48 | Pożeracz Gwiazd 吞星 | Pochłanianie kryształów chaosu na stałe statystyki | Gwiazda-pułapka | Pożeracz |
| 49 | Pierwotna Forma 原形 | Ostateczna forma ścieżki (Awatar Miecza, Ciało Wadżry, Prawdziwy Feniks, Król Trucizn) | Walka z formą | (nazwa formy) |
| 50 | Stwórca Małego Świata 小世界 | Własny mały świat: zasiedlasz uczniami, produkuje | Narodziny świata | Stwórca |
| 51 | Władca Losu 命主 | Raz na walkę przerzut wyniku | Nić losu | Tkacz Losu |
| 52 | Pierwotny Mędrzec 原賢 | Piąta Zasada | Pytanie bez odpowiedzi | Pierwotny Mędrzec |
| 53 | Pierwotny Cesarz 原帝 | Wojny Chaosu między sektami | Wojna światów | Cesarz Chaosu |
| 54 | Przedwieczny 太古 | Zbiór offline do 72 h | Zapomnienie | Przedwieczny |
| 55 | Doskonały Chaos 至混沌 | Rajd serwera na Pierwotną Bestię | Pierwotna Bestia | Pierwotny |

### ERA VI — Dao (świat: Źródło)
| # | Królestwo | Co zmienia w grze | Próba | Tytuł |
|---|---|---|---|---|
| 56 | Dotyk Dao 觸道 | Dao bez limitu poziomów | Cisza | Dotknięty |
| 57 | Słowo Dao 道言 | Nadajesz nazwę jednemu regionowi na serwerze | Imię | Nazywający |
| 58 | Ścieżka Dao 道途 | Scalenie ścieżek: premie wszystkich 4 | Rozdroże | Wszechścieżkowy |
| 59 | Serce Dao 道心 | Odporność na demony serca na zawsze | Lustro serca | Niezłomne Serce |
| 60 | Oko Dao 道眼 | Widzisz ukryte miejsca na mapie | Ślepota | Widzący |
| 61 | Ręka Dao 道手 | Szósta Zasada | Ręka losu | Ręka Dao |
| 62 | Ciało Dao 道體 | Raz dziennie przeżywasz śmiertelny cios | Śmierć | Nieumierający |
| 63 | Dusza Dao 道魂 | Twoi uczniowie stają się NPC na mapie dla innych | Rozproszenie | Nauczyciel Światów |
| 64 | Pan Dao 道主 | Twoja technika trafia do gry jako legendarny zwój | Ostatni uczeń | Pan Dao |
| 65 | Jedność z Dao 合道 | Posąg postaci na mapie w miejscu pierwszego przełomu | Zatarcie | Legenda |
| 66 | Dao Bez Imienia 無名之道 | Wielkie Wniebowstąpienie: Reinkarnacja z wpisem do Kroniki Wieczności | Bezimienne | Bezimienny |

Wygląd: każda era ma własną paletę aury (I: barwa ścieżki, II: złoto-biała, III: jadeit z runami, IV: gwiezdna, V: tęczowy chaos, VI: przejrzyste światło), a co 11 królestw nowy strój bohatera.

---

## 6. Ścieżki

### 6.1 Gałęzie
Przy królestwach 4, 9, 22 (i dalej co erę) wybór gałęzi: 3 gałęzie na ścieżkę, 9 wyborów na całą grę.

| Ścieżka | Gałąź A | Gałąź B | Gałąź C |
|---|---|---|---|
| Qi | Miecz (obrażenia, Intencja Miecza) | Formacje (pasywne Qi, tarcze) | Dusza (kontrola, kradzież Qi) |
| Ciało | Żelazna Skóra (garda, odbicie) | Gigant (HP, ogłuszenia) | Wilk (szybkość, krwawienie, regeneracja) |
| Płomień | Feniks (odrodzenie, leczenie) | Słońce (obrażenia za dnia, przebicie) | Popiół (oparzenia, osłabienie) |
| Trucizna | Jad (siła trucizn) | Cień (uniki, pierwsze uderzenie) | Lalkarz (Gu w walce) |

### 6.2 Techniki ścieżki
6–8 technik na ścieżkę odblokowywanych z etapami (zamiast jednej sygnaturowej). Druga ścieżka od Złotego Rdzenia z karą −20% do premii obu. Ostateczna forma ścieżki w królestwie 49.

### 6.3 Żywioły
Pięć elementów w cyklu: Drewno → Ogień → Ziemia → Metal → Woda → Drewno (przewaga +20% obrażeń, słabość −20%). Techniki, przeciwnicy, regiony, płomienie i jady mają żywioł.

---

## 7. Kolekcje ścieżek

### 7.1 Płomień — Niebiańskie Płomienie
**Przebieg:** odnalezienie → ujarzmienie (walka z Duchem Płomienia) → wchłonięcie (odosobnienie 1–48 h, szansa zależna od królestwa i pigułek chłodzących; porażka = oparzenie meridianów −30% Qi na 24 h) → karmienie (poziomy 1–10, kryształy ognia, rdzenie bestii, rośnie samo w odosobnieniu) → fuzja (1 główny + 2 poboczne; dwa płomienie poz. 10 = płomień złożony).

**Każdy płomień daje:** pasyw, technikę płomienia (dodatkowy specjał), ogień alchemii (szansa i jakość pigułek), ogień kuźni. Inne ścieżki mogą trzymać 1 płomień tylko do kotła (30% mocy).

| Ranga | Płomień | Gdzie | Pasyw | Technika | Alchemia |
|---|---|---|---|---|---|
| 18 | Płomień Świecy Dusz | Miejsca kultu nocą | +5% Qi z medytacji | Iskra Duszy (kradnie 5 Qi) | +3% |
| 17 | Ogień Bambusowego Dymu | Lasy, parki | +5% zbiorów ziół | Zasłona Dymu | +4% |
| 16 | Czerwony Płomień Żelaza | Strefy przemysłowe | +8% obrażeń Siły | Rozżarzona Pięść | kuźnia +5% |
| 15 | Płomień Lisiego Ognia | Ognisty Lis | +10% szansy efektów | Lisie Ognie (3 trafienia) | +5% |
| 14 | Słony Płomień Morza | Brzegi wód | Odporność na truciznę | Gorąca Fala | +5% |
| 13 | Płomień Gromowego Pióra | Gromowy Orzeł | +inicjatywa | Piorunujący Ogień (ogłuszenie) | +6% |
| 12 | Popielny Płomień Kości | Cmentarze, ruiny | Leczenie po zabójstwie | Ogień Popiołu (osłabia) | +6% |
| 11 | Płomień Krwawego Księżyca | Tylko przy pełni | Nocą +20% obrażeń | Krwawy Księżyc (krwawienie) | +7% |
| 10 | Lodowy Płomień Pustki | Wieża 25 | Odporność na oparzenia | Zimny Ogień (spowalnia) | +8% |
| 9 | Płomień Smoczego Oddechu | Smok Jiao | +12% HP | Oddech Smoka | +9% |
| 8 | Złoty Płomień Słońca | Południe, otwarty teren | Za dnia +15% Qi | Słoneczny Promień (przebicie gardy) | +10% |
| 7 | Płomień Czarnego Lotosu | Demoniczny Kultywator | Demony serca −50% | Czarny Lotos (wybuch po 2 turach) | +11% |
| 6 | Płomień Burzowego Qilina | Boss Świata | Pioruny próby słabsze | Ogień Burzy (łańcuch) | +12% |
| 5 | Płomień Fioletowego Nieba | Wędrujący, rzadki | +1 slot fuzji | Niebiański Deszcz Ognia | +14% |
| 4 | Płomień Feniksa | Wieża 50 | Odrodzenie raz na walkę | Odrodzenie Feniksa+ | +16% |
| 3 | Płomień Bez Koloru | Wydarzenie serwera | Oparzenia ignorują leczenie | Biały Ogień | +18% |
| 2 | Płomień Chaosu | Era Pierwotna | Spala statusy wroga | Chaos Ognia | +22% |
| 1 | Płomień Pierwszego Świtu | Jeden na serwer — odbierany w pojedynku | +25% do wszystkiego | Świt Stworzenia | +30% |

**Księga Płomieni:** 3 → „Zbieracz Iskier”, alchemia +5%; 6 → „Władca Ognisk”, +1 slot; 9 → „Serce Pożogi”, ogień +15%; 12 → „Cesarz Płomieni”, wieczna aura, Qi +10%; 15 → fuzja trzech; 18 → „Ten, W Którym Płonie Niebo”. Tajne fuzje (np. Feniks + Słońce = Płomień Wiecznego Południa) to tajne osiągnięcia.

### 7.2 Trucizna — Księga Jadów i hodowla Gu
**Zbieranie jadów:** bestie (żmije, pająki, skorpiony, ropuchy), rośliny w terenie (mokradła i parki nocą, rzadkie przed świtem), miejsca skażone, wydarzenia. Jakość: surowy → oczyszczony → esencja → królewski (Kocioł Jadów, kolejka idle 30 min–8 h).

**Ciało Trucizn** (wchłanianie jadów; ryzyko zatrucia bez antidotum):

| Poziom | Premia |
|---|---|
| 1 | Odporność na krwawienie +20% |
| 3 | Trucizny trwają +1 turę |
| 5 | Kto cię uderzy, może się zatruć |
| 7 | Efekt Paraliż (traci co 2. akcję) |
| 9 | Efekt Korozja (gardy wroga −50%) |
| 12 | Efekt Spalanie Qi |
| 15 | Odporność na wszystkie trucizny |
| 20 | Efekt Zjadacz Serca (obrażenia rosną co turę) |

**Mieszanie:** 2–3 jady → powłoki na broń (5 walk), mgły bojowe (specjał), towary na Targ. Nowa kombinacja = wpis do Księgi + tajne osiągnięcie.

**Gniazdo Gu (automat):** 3–9 słoików, Gu rosną godzinami–dniami. Rodzaje: Złodziej (kradnie Qi), Strażnik (osłona), Szpieg (pokazuje ukryte miejsca), Uzdrowiciel, Bitewny (walczy obok). Dwa Gu poz. max → wyższa ranga; mutacje → legendarne (Gu Tysiąca Żyć). Gałąź Lalkarz steruje Gu w walce.

**Kolekcja:** 10 jadów „Truciciel”, 25 „Mistrz Kotła” (+1 slot), 50 „Król Stu Jadów” (aura mgły), wszystkie (~70) „Ten, Którego Krew Zabija”; pełny Gniazdownik „Matka Gu”.

### 7.3 Ciało — Hartowanie +X
**Wyświetlanie:** przy imieniu wszędzie: „Lin +27”. Kolor: +1–9 szary, +10–29 brąz, +30–49 srebro, +50–69 złoto, +70–89 świecący jadeit, +90–98 tęcza, +99 animowany złoty płomień.

**Źródła:** kroki z telefonu, przyjęte ciosy w walce, kąpiele ziołowe (idle 1–8 h), pioruny prób (dobrowolne dodatkowe pioruny), trening na mapie (schody, wzgórza, stadiony).

**Próba +1:** szansa sukcesu jak przy ulepszaniu; porażka nie cofa, zużywa materiały i daje zmęczenie (1 h). Co 10 poziomów próg: Pigułka Przekroczenia Ciała + walka-próba.

| +X | Etap | Za każde +1 | Premia etapu |
|---|---|---|---|
| 1–9 | Skóra | +1% HP | Odporność na krwawienie +10% |
| 10–19 | Mięśnie | +1% HP, +0,5% obrażeń Siły | Pięści mogą ogłuszyć |
| 20–29 | Ścięgna | +1% HP, +inicjatywa | Pierwszy cios w walce +30% |
| 30–39 | Kości | +1,5% HP | Raz na walkę ignorujesz ogłuszenie |
| 40–49 | Szpik | +1,5% HP | Regeneracja 2% HP na turę |
| 50–59 | Krew | +2% HP | Wampiryzm 5% |
| 60–69 | Organy | +2% HP | Trucizny o połowę krócej |
| 70–79 | Złote Ciało | +2% HP, +1% gardy | Garda nigdy poniżej 20% |
| 80–89 | Diamentowe Ciało | +2,5% HP | Odbijasz 10% obrażeń |
| 90–98 | Ciało Wadżry | +3% HP | Przeżywasz śmiertelny cios raz na walkę |
| 99 | Ciało Nieśmiertelne | — | „Niezniszczalny”, złota skóra, +20% do wszystkiego |

### 7.4 Qi — Intencja Miecza
Echa Mieczy z miejsc pamięci (muzea, pomniki, ruiny) i od pokonanych szermierzy podnoszą stopień Intencji (przy imieniu: „Lin ⚔4”). Miecze duchowe: do 9 latających mieczy w szyku, każdy z pasywem; pełny szyk = technika Deszcz Dziewięciu Mieczy.

---

## 8. Tytuły i osiągnięcia

### 8.1 Tytuł nad postacią
Wybrany tytuł widać nad postacią w walce, na mapie (gracze w okolicy), w rankingach, w profilu i na czacie sekty. Kolor = rzadkość (zwykły, rzadki, epicki, legendarny z animowaną poświatą, mityczny z cząsteczkami). Premie tytułów: wszystkie zdobyte działają jak dotąd (sumowane), wybrany dodaje +10% swojej premii.

### 8.2 Kategorie (~60 osiągnięć)
Walka, Kultywacja, Świat, Rzemiosło, Społeczność, Ścieżka, Tajne (15).

### 8.3 Tajne osiągnięcia (pokazane jako „???” z zagadką)
| Tytuł | Warunek | Zagadka |
|---|---|---|
| Uparty Osioł | 100 porażek z manekinem | „Drewno też uczy pokory.” |
| Nocny Mnich | Medytacja rozpoczęta o 3:33 | „Gdy miasto śpi, niebo mówi.” |
| Na Ostrzu Noża | Zwycięstwo nad bossem z 1 HP | „Jeden oddech od końca.” |
| Pacyfista | 7 dni bez walki przy codziennej grze | „Najsilniejszy cios to ten niezadany.” |
| Wędrowiec Jianghu | 50 różnych miejsc | „Droga jest celem.” |
| Pierwszy Świt | Przełom o wschodzie słońca | „Słońce wita odważnych.” |
| Burzowe Dziecko | Przełom podczas burzy (pogoda) | „Niebo grzmi dla ciebie.” |
| Uczeń Bez Mistrza | Złoty Rdzeń bez sekty | „Samotna sosna też rośnie.” |
| Złoty Kocioł | 10 niebiańskich pigułek z rzędu | „Ogień słucha cierpliwych.” |
| Wieczne Południe | Tajna fuzja Feniks + Słońce | „Dwa ognie, jedno niebo.” |
| Matka Gu | Wszystkie rodzaje Gu | „Małe stworzenia, wielka władza.” |
| Niezniszczalny | Hartowanie +99 | „Ciało, którego nie złamie niebo.” |
| Dziewięć Mieczy | Pełny szyk mieczy | „Dziewięć ostrzy, jedna wola.” |
| Ten, Który Wrócił | Reinkarnacja | „Koniec jest początkiem.” |
| Bezimienny | Królestwo 66 | „Dao, które da się nazwać…” |

### 8.4 Tytuły rankingowe (sezonowe)
Pan Wieży, Pogromca Qilina, Mistrz Areny, Pierwszy w Królestwie X na serwerze, Cesarz Płomieni (najwięcej płomieni), Król Stu Jadów, Niezniszczalny (najwyższe hartowanie).

### 8.5 Kolekcje
Bestiariusz, Atlas regionów, Księga technik, Zielnik, Księga Płomieni, Księga Jadów, Gniazdownik, Kolekcja tytułów — każda z premiami za progi i komplet.

---

## 9. Świat i regiony
- **Regiony** z siatki geograficznej (nazwy: Dolina Mgieł, Szczyty Pioruna…): żywioł, przeciwnicy, surowce, boss regionu, kontrola sekt, wpis do Atlasu.
- **Teren z map** (park → las duchowy, woda → jezioro Qi, centrum → miasto, wzgórza → góry) decyduje o przeciwnikach, ziołach, jadach i płomieniach.
- **Pora dnia i pogoda:** noc wzmacnia Truciznę i demony, burza Piorun, upał Płomień, deszcz Wodę; pełnia księżyca otwiera rzadkie miejsca.
- **Ukryte miejsca:** ruiny, groty, ołtarze z zagadkami i jednorazowym łupem (Oko Dao je pokazuje).
- **Warstwy er:** Niebiańskie Wyspy (II), Ziemie Święte (III), Panteon (IV), Morze Chaosu (V), Źródło (VI) — nakładki na mapę z własnymi przeciwnikami.

---

## 10. Pozostałe systemy
- **Arena rankingowa:** asynchroniczne walki z kopiami innych graczy, ligi Brąz → Nieśmiertelny, sezony, nagrody tygodniowe.
- **Towarzysz w walce:** ruch co 3 tury, ewolucja, drzewko umiejętności.
- **Ekwipunek:** losowe premie, zestawy z premią za komplet, rozbieranie na materiały, grawerowanie run.
- **Sekty:** budynki (pawilon alchemii, biblioteka technik, arena treningowa, ogród sekty), misje sekty, boss sekty, turniej sekt.
- **Społeczność:** przyjaciele, prezenty, polowanie w parze, powiadomienia push.
- **Wydarzenia:** weekendowe bonusy, inwazja demonów, Wielka Próba gracza jako wydarzenie, darmowa przepustka sezonowa.
- **Fabuła:** kampania z rozdziałem na każde królestwo ery I, postacie w świecie (mistrz, rywal, kupiec), zadania powiązane z mapą.

---

## 11. Plan wydań
| Wersja | Zawartość |
|---|---|
| 1.5 Idle i Kultywacja | Pasywne Qi, medytacja ×5, odosobnienie, naczynie, kompresja, podsumowanie offline, metody kultywacji, 66×12 (pełne mechaniki ery I, dalsze ery widoczne jako zamglone), próby jako walki, jakość fundamentu, demony serca, oświecenie |
| 1.6 Ścieżki i kolekcje | Płomienie, Jady + Gu, Hartowanie +X, Intencja Miecza, gałęzie ścieżek, żywioły |
| 1.7 Tytuły i automaty | ~60 osiągnięć z tajnymi, tytuł nad postacią, kolekcje; ogród, uczniowie, kolejki alchemii/kuźni, wyprawy, automatyczny przełom |
| 1.8 Świat i rywalizacja | Regiony, teren, pogoda, ukryte miejsca, arena z ligami, towarzysz w walce, budynki sekty, wydarzenia |
| 2.0 | Era II (Niebiańskie Wyspy), domena, klon; dalej kolejne ery |
