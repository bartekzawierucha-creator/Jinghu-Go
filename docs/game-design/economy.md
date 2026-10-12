# Ekonomia: przedmioty, rzemiosło, targ (Etap 6)

Konfiguracja: `content/game-config/items.json` (`items-1`), `crafting.json` (`crafting-1`), `market.json` (`market-1`), materiały ze świata w `world.json` (`world-2`, pole `yields`). Każdy plik jest walidowany przy starcie serwera (błędna konfiguracja = serwer nie wstaje); odwołania między plikami (łupy → NPC, materiały świata → przedmioty, receptury → przedmioty) pilnują testy jednostkowe.

## Waluta i sakwa
- **Kamienie Duchowe (KD)** — jedyna waluta. Nowa postać dostaje 100 KD (prawdziwy wpis `initial_grant` w rejestrze przy pierwszej operacji portfela).
- **Sakwa**: 60 miejsc. Miejsce zajmuje każdy niepusty stos (maks. 999 sztuk) i każdy unikalny przedmiot w sakwie. Przedmioty założone i wystawione na targu nie zajmują miejsca. Pierścień Przestrzeni (założony) daje +20 miejsc.
- Nagrody (łupy, zbieranie) **nigdy nie przepadają przez pełną sakwę** — gracz je zdobył. Stos jest przycinany tylko do limitu 999, a nadwyżka trafia do audytu. Pełna sakwa blokuje za to działania gracza tworzące nowe miejsce (rzemiosło, zakup, zdjęcie przedmiotu).

## Przedmioty
| Typ | Przykłady | Zachowanie |
|---|---|---|
| materiał (stos) | duchowe zioło, ruda żelaza, kieł wilka, jadeit, kamień rafinacji | składniki receptur, handel |
| konsumpcyjny (stos) | Pigułka Qi (60 Qi), Wielka Pigułka Qi (250 Qi) | `use`: 1–10 sztuk, Qi przyznaje moduł Kultywacji przez outbox (dokładnie raz) |
| ekwipunek (unikalny) | miecz, sztylet, szata, talizman, pierścień, skarb | 5 slotów: broń, szata, talizman, pierścień, skarb; ulepszenia +1…+15 |

Premia ekwipunku = statystyka bazowa × (100% + 10% × poziom ulepszenia), zaokrąglona w dół. Premie są **zamrażane w migawce walki** w chwili jej rozpoczęcia (zmiana ekwipunku w trakcie walki nic nie daje).

## Skąd się biorą przedmioty
- **Świat**: duchowe zioło daje 1–3 zioła (zawsze), źródło Qi z 40% szansą 1–2 rudy. Ilości losuje serwer (CSPRNG) przy zebraniu; materiały trafiają do sakwy przez outbox (`world.items_gathered`).
- **Łupy**: tylko za **nagradzane** zwycięstwa PvE (te same, które dają Qi — obowiązuje dzienny limit). Wilk: 15 KD + 60% na 1–2 kły; bandyta: 30 KD + ruda (70%), jadeit (25%), Pióro Feniksa (0,5%). Ziarno łupu pochodzi z identyfikatora wiadomości outboxa — niejawnego dla gracza i stałego przy ponowieniach.
- **Rzemiosło** (niżej) i **targ**.

## Rzemiosło
- **Kucie** nigdy nie zawodzi (walidator wymusza 100%). **Alchemia**: szansa bazowa + `alchemyInsightBonusPermille` (10‰ = 1 punkt procentowy) za każdy punkt Wglądu, maks. 100%. Porażka zużywa składniki i kamienie.
- Jedno żądanie = 1–10 powtórzeń (przedmioty unikalne: 1). Składniki, kamienie i wynik w **jednej transakcji**: brak składnika, kamieni lub miejsca → nic nie zostaje zużyte.
- **Ulepszanie** o +1: 1 Kamień Rafinacji + 20 × docelowy poziom KD. Szanse: +1…+5 100%, potem 90, 80, 70, 60, 50, 40, 35, 30, 25, 20%. Porażka **zachowuje** poziom (bez cofania, bez niszczenia). Przedmiot wystawiony na targu nie może być ulepszany.
- Każde żądanie trafia do niezmiennego dziennika `crafting.log` z ziarnem — wynik można odtworzyć (`CraftingRules.Rolls(seed, n)`).

## Targ
- Wystawia się **całą partię**: stos (przedmiot × ilość) albo jeden przedmiot unikalny, za cenę 1 … 10⁹ KD.
- **Depozyt**: przy wystawieniu towar znika z sakwy (stos — wpis `market_escrow` w rejestrze; przedmiot unikalny — stan `listed`).
- **Opłata za wystawienie**: max(1, 1% ceny), bezzwrotna — hamuje spam ofert i wyprowadza kamienie z gry.
- **Podatek od sprzedaży**: 5% (zaokrąglony w dół); sprzedawca zawsze dostaje co najmniej 1 KD. Kolejny „odpływ” waluty.
- **Zakup** = jedna transakcja: kupujący płaci cenę, sprzedawca dostaje cenę − podatek, towar opuszcza depozyt, oferta się zamyka. Równoczesnych kupujących rozstrzyga wersja oferty: wygrywa dokładnie jeden, reszta dostaje `409 listing_unavailable` (test akceptacyjny #3). Nie można kupić własnej oferty (też z innej postaci tego samego konta).
- Oferta żyje 48 h. **Wygasła oferta** przestaje być widoczna i kupowalna; towar czeka w depozycie, aż sprzedawca go odbierze (`cancel`). Nie ma więc zadania w tle, które mogłoby zawieść. Limit: 20 aktywnych ofert na postać (wygasłe nieodebrane też się liczą).
- Sprzedawca dostaje powiadomienie `market_sold` z kwotą netto.

## Przepływy waluty
| Źródło (do gry) | Odpływ (z gry) |
|---|---|
| startowe 100 KD, łupy PvE (ograniczone dziennym limitem nagradzanych walk) | rzemiosło, ulepszenia, opłata za wystawienie, podatek od sprzedaży |

Strojenie: wszystkie liczby są w plikach konfiguracji; zmiana reguł = nowa `rulesVersion`.
