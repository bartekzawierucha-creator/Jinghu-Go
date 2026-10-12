# Laboratorium Dao i zwoje technik

Konfiguracja: `content/game-config/dao-lab.json` (`dao-1`). Kod reguł: `Modules/Combat/Domain/DaoLab.cs`.

## Projektowanie
Gracz składa technikę z **zatwierdzonych komponentów**: wybiera slot (atak, obrona, ruch, kontra), koszt Qi, odnowienie, szybkość i dokładnie **jeden komponent** pasujący do slotu (atak: moc, statystyka skalująca, zasięg, celność, opcjonalny efekt krwawienia/ogłuszenia/osłabienia; obrona: blok; ruch: unik; kontra: odbicie i kradzież Qi). Serwer sprawdza dwie warstwy:

1. **Twarde limity** każdego komponentu (np. moc 1–30, Qi 0–20, odnowienie 0–4, zasięg w granicach dystansu walki 1–3, szansa efektu 5–70%). Ogłuszenie: najwyżej 30%, 1 tura i odnowienie ≥ 2 — nie da się zbudować „zamka” z ogłuszeń.
2. **Budżet mocy.** To, co technika robi, kosztuje punkty (moc × 10, rozpiętość i zasięg, celność, szybkość, wartość efektu ważona szansą, blok/unik/odbicie/kradzież Qi); to, czego wymaga, daje punkty (baza slotu + 12 za każdy punkt Qi + 40 za turę odnowienia). Projekt jest dozwolony, gdy punkty ≤ budżet. Wagi skalibrowano tak, że **każda oficjalna technika bez ścieżki mieści się w budżecie** (test jednostkowy), a techniki-sygnatury ścieżek mogą go przekraczać — autorska technika nigdy nie przebije sygnatury.

`POST /dao-lab/preview` zwraca punkty, budżet i listę problemów bez zapisu (podgląd w edytorze). Stworzenie techniki kosztuje 150 KD; postać może być autorem najwyżej 6 technik.

## Niezmienność
Zapisana technika (`combat.custom_techniques`) nigdy się nie zmienia ani nie znika (trigger). Gdy gracz zakłada ją w zestawie i zaczyna walkę, **pełna definicja jest zamrażana w migawce walki** — walkę można odtworzyć bit w bit z ziarna, nawet jeśli reguły Laboratorium się zmienią. Identyfikator: `dao_` + 16 liter a–p (64 losowe bity).

## Zwoje
Autor może spisać zwój swojej techniki (50 KD + 1 jadeit). Zwój to unikalny przedmiot (`technique_scroll`) z niezmiennym odwołaniem do techniki — można go sprzedać na targu (oferta pokazuje, której techniki uczy; `GET /dao-lab/techniques/{id}` jest publiczne). Przeczytanie zwoju uczy techniki i zużywa zwój; jeśli postać już ją zna, zwój zostaje. Zwój nie da się założyć ani ulepszyć.
