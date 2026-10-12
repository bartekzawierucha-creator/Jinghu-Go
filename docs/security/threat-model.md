# Model zagrożeń (STRIDE, skrót)

Status: ✅ zaimplementowane i przetestowane · 🟡 częściowo · ⏳ zaplanowane (etap)

## Konta i sesje
| ID | Zagrożenie | Kontrola | Status |
|---|---|---|---|
| T-ACC-1 | Brute force / credential stuffing | Rate limit na IP dla `/auth/*` (10/min), blokada konta po 5 błędach na 15 min, jednolity błąd `invalid_credentials`, wyrównanie czasu przy nieistniejącym koncie | ✅ (`SecurityTests`, `IdentityTests`) |
| T-ACC-2 | Kradzież tokena dostępu | TTL 15 min, rotacja przy refresh, wylogowanie unieważnia rodzinę | ✅ |
| T-ACC-3 | Kradzież refresh tokena | Wykrywanie ponownego użycia → unieważnienie całej rodziny | ✅ |
| T-ACC-4 | Wyciek tokena z urządzenia | Klient: `ITokenStore`; obecnie PlayerPrefs (DEV). Docelowo Android Keystore / iOS Keychain | ⏳ przed wydaniem |
| T-ACC-5 | Wyciek bazy | Tylko hashe tokenów (SHA-256) i haseł (PBKDF2-SHA512) | ✅ |
| T-ACC-6 | Enumeracja kont | Ten sam błąd dla złego hasła i braku konta; rejestracja ujawnia zajętość e-maila (świadomy kompromis UX → rozważyć potwierdzenie e-mailem) | 🟡 |

## Zasoby i autoryzacja
| ID | Zagrożenie | Kontrola | Status |
|---|---|---|---|
| T-RES-1 | Odczyt/zmiana cudzej postaci/szkicu (IDOR) | Sprawdzenie właściciela w serwisie; cudze zasoby → 404 | ✅ (test akceptacyjny #1) |
| T-RES-2 | Zapomniane `RequireAuthorization` | Test enumeruje wszystkie endpointy i porównuje z listą publiczną | ✅ |
| T-RES-3 | Wstrzyknięcie wyników przez klienta (statystyki, obrażenia) | Klient wysyła intencje; nieznane pola JSON → 400 | ✅ (test #6) |

## Ekonomia
| ID | Zagrożenie | Kontrola | Status |
|---|---|---|---|
| T-ECO-1 | Podwójne naliczenie przy ponowieniu | `Idempotency-Key` + zapis w tej samej transakcji | ✅ (test #2) |
| T-ECO-2 | Wyścig równoległych operacji | `expectedVersion`; in-memory serializowalny; SQL: blokada wiersza salda w triggerze rejestru | ✅ |
| T-ECO-3 | Ujemne saldo / manipulacja saldem | CHECK ≥ 0; saldo zmieniane wyłącznie przez trigger rejestru; wpisy niezmienne; widok rekonsyliacji | ✅ (`database/tests`) |
| T-ECO-4 | Duplikacja przedmiotów przy równoległym zakupie | Wersja oferty + SERIALIZABLE; indeks: jedna aktywna oferta na przedmiot; wersja przedmiotu podnoszona o 1 (trigger) | ✅ (test #3 `Concurrent_buyers_of_a_unique_item_cannot_both_win`, 4 kupujących × 3 rundy) |
| T-ECO-5 | Ujemne/zerowe ilości i ceny | Walidacja w API i w `InventoryLedger` (każdy moduł), CHECK `delta <> 0`, `quantity_after >= 0`, `price >= 1` | ✅ (test #4, `database/tests/005_economy.sql`) |
| T-ECO-6 | Częściowe pobranie przy błędzie (np. brak miejsca po zabraniu składników) | Cała operacja w jednej transakcji; błąd biznesowy = brak commitu | ✅ (test #5: rzemiosło, targ, wystawienie) |
| T-ECO-7 | Pranie waluty między własnymi postaciami przez targ | Zakaz zakupu oferty z tego samego konta (serwis + CHECK). Między różnymi kontami: opłata + podatek i audyt; wykrywanie sieci kont — moduł AntiAbuse (Etap 8) | 🟡 |
| T-ECO-8 | Przewidywanie łupu przed walką | Ziarno łupu = f(id wiadomości outboxa), nieznane klientowi; nagradzane tylko zwycięstwa w dziennym limicie | ✅ |
| T-ECO-9 | DoS przez kosztowną rekonsyliację | `/health/economy` buforuje wynik (skan najwyżej co 5 min); docelowo tylko sieć wewnętrzna | ✅ / ⏳ ingress |

## Laboratorium Dao, sekty, terytoria (Etapy 6+–7)
| ID | Zagrożenie | Kontrola | Status |
|---|---|---|---|
| T-DAO-1 | Technika „zbyt silna” (ominięcie balansu) | Twarde limity + budżet mocy liczone na serwerze; klient wysyła tylko komponenty; CHECK `allowance >= points` w bazie | ✅ (`DaoLabTests`, test kalibracji) |
| T-DAO-2 | Użycie cudzej techniki bez nauki | Zestaw walki akceptuje tylko techniki znane postaci (`known_techniques`) | ✅ |
| T-DAO-3 | Podmiana definicji po fakcie | Definicje niezmienne (trigger), zamrażane w migawce walki | ✅ |
| T-SECT-1 | Eskalacja uprawnień w sekcie | `SectPolicy` sprawdzana na serwerze przy każdej akcji; usunięcie tylko niższej rangi; zmiana ról tylko przez przywódcę | ✅ (`Roles_decide_what_members_may_do`, testy jednostkowe macierzy) |
| T-SECT-2 | Działanie cudzą postacią w sekcie | Każde żądanie wskazuje postać, której własność sprawdza `ICharacterDirectory` | ✅ |
| T-SECT-3 | Wyścigi członkostwa (dwie sekty naraz, przepełnienie) | PK `members(character_id)`, wersja sekty, limit członków sprawdzany w tej samej transakcji | ✅ |
| T-SECT-4 | Siedziba ujawnia dokładną lokalizację gracza | Zaokrąglenie do ~100 m, brak współrzędnych w historii i audycie; siedziba jest celowo publiczna | ✅ |
| T-TER-1 | Zajęcie terytorium zdalnie (fałszywy klucz) | Klucz wyłącznie z pozycji zweryfikowanej przez Geolocation | ✅ |
| T-TER-2 | Nabijanie punktów wojny powtórzonym zdarzeniem walki | `war_battles (war_id, battle_id)`; punkty tylko w oknie wojny; wyniki tylko rosną (trigger) | ✅ |
| T-TER-3 | „Farmienie” punktów ustawionymi walkami między multikontami | Brak automatycznej detekcji (wymaga AntiAbuse: korelacja kont/urządzeń) | ⏳ |

## Kultywacja i zdarzenia
| ID | Zagrożenie | Kontrola | Status |
|---|---|---|---|
| T-CUL-1 | Manipulacja czasem (zegar urządzenia) dla medytacji / nocy Trucizn | Start i koniec sesji tylko z zegara serwera; strefa czasowa świata po stronie serwera | ✅ |
| T-CUL-2 | Wielokrotne odebranie Qi z jednej sesji | Idempotency-Key + zakończenie sesji w tej samej transakcji; równoległe żądania → jedno przyznanie | ✅ (test #2) |
| T-CUL-3 | „Przelosowywanie” nieudanego przełomu | Wynik zapisany pod kluczem idempotencji (replay, nie nowy rzut); wymagany `expectedVersion`; odchylenie Qi blokuje kolejne próby | ✅ |
| T-CUL-4 | Fałszywe kroki (emulator, potrząsanie) | Limit kroków/min od ostatniego raportu i dzienny; przycinanie + sygnał w audycie; brak automatycznych kar | 🟡 heurystyka; korelacja z GPS w Etapie 5 |
| T-CUL-5 | Wyłudzenie RNG | Seed z CSPRNG serwera, rzut deterministyczny, zapis seed/rzut/szansa; CHECK `success = roll < chance` w bazie | ✅ |
| T-EVT-1 | Utrata zdarzenia przy awarii | Outbox w tej samej transakcji; przetwarzanie jedna wiadomość = jedna transakcja | ✅ (test #10, prawdziwy proces) |
| T-EVT-2 | Podwójny efekt przy ponownym dostarczeniu | Idempotentne handlery (`UNIQUE(account_id, source_message_id)`) | ✅ |

## Walka, GPS, komunikacja
| ID | Zagrożenie | Kontrola | Status |
|---|---|---|---|
| T-CMB-1 | Klient ustawia obrażenia/wynik | Tylko intencja; nieznane pola → 400; rozstrzyganie wyłącznie na serwerze | ✅ (test #6) |
| T-CMB-2 | Zła kolejność tur / akcja w cudzej walce | Numer tury musi się zgadzać; strona walki ustalana z tokena; cudze walki → 404 | ✅ (test #7) |
| T-CMB-3 | Powtórzona / podwójna akcja | Idempotency-Key, PK `(battle, turn, side)`, tura rozstrzyga się raz | ✅ (test #8) |
| T-CMB-4 | Manipulacja RNG / „ustawiona” walka przez serwer | Seed z CSPRNG, commit-reveal, pełny log tur — walkę odtworzy każdy | ✅ |
| T-CMB-5 | Wymuszanie walkowerów / spam wyzwań | Wyzwania nie blokują postaci, maks. 3 oczekujące, wygasają po 10 min; walkower dopiero po 3 przegapionych turach | 🟡 |
| T-CMB-6 | Farmienie Qi na NPC / win-trading PvP | Limit 20 nagradzanych zwycięstw PvE / 24 h; PvP bez nagród w Qi | ✅ |
| T-GPS-1 | Teleportacja / spoofing pozycji | Prędkość z odjęciem promieni błędu, okres przenosin 5 min, oznaczenie sesji po 5 anomaliach, audyt | ✅ (test #9) — fałszywy GPS poruszający się „realistycznie” nie jest wykrywalny po stronie serwera (⏳ atestacja urządzenia: Play Integrity / App Attest) |
| T-GPS-2 | Manipulacja czasem próbek | Okno ±30 s / 120 s wobec zegara serwera, ścisła kolejność, limit tempa | ✅ |
| T-GPS-3 | Farmienie punktów | Raz na postać na punkt, 60 interakcji/h, punkty znikają co 6 h | ✅ |
| T-GPS-4 | Fałszowanie / przewidywanie punktów | Punkt odtwarzany po stronie serwera z tajnego seeda; nieznany klucz → 404 | ✅ |
| T-GPS-5 | Wyciek historii lokalizacji | Tylko ostatnia pozycja, kasowana przy końcu sesji (CHECK w bazie); brak współrzędnych w logach/audycie; retencja zebrań 7 dni | ✅ (retencja: zadanie czyszczące ⏳) |
| T-COM-1 | Podsłuch | Klient release wymusza HTTPS (`UnityWebRequestTransport`); TLS terminowany na ingressie | 🟡 (ingress ⏳) |
| T-COM-2 | Duże/zniekształcone żądania | Limit body 64 KiB, walidacja JSON → 400 | ✅ |
| T-OPS-1 | Sekrety w repo | `.gitignore` dla `.env`; gitleaks w CI | 🟡 (CI nieuruchomione tutaj) |
| T-OPS-2 | Wyciek danych w logach | Logujemy tylko metodę, ścieżkę, status, czas; log frameworka z query stringiem wyciszony | ✅ |
