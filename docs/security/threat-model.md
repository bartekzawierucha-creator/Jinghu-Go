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
| T-ECO-4 | Duplikacja przedmiotów, rynek | Etap 6 | ⏳ |

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
| T-CMB-* | Kolejność tur, powtórzone akcje, RNG klienta | Etap 4 (wzorce: version + idempotencja + `DeterministicRandom` z seedem serwera) | ⏳ |
| T-GPS-* | Spoofing lokalizacji, teleportacja, farmienie | Etap 5: walidacja prędkości/dokładności/kolejności, scoring zamiast automatycznych kar, minimalizacja przechowywania | ⏳ |
| T-COM-1 | Podsłuch | Klient release wymusza HTTPS (`UnityWebRequestTransport`); TLS terminowany na ingressie | 🟡 (ingress ⏳) |
| T-COM-2 | Duże/zniekształcone żądania | Limit body 64 KiB, walidacja JSON → 400 | ✅ |
| T-OPS-1 | Sekrety w repo | `.gitignore` dla `.env`; gitleaks w CI | 🟡 (CI nieuruchomione tutaj) |
| T-OPS-2 | Wyciek danych w logach | Logujemy tylko metodę, ścieżkę, status, czas; log frameworka z query stringiem wyciszony | ✅ |
