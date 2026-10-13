# Raport gotowości do wydania (Etap 8)

Data: 2026-10-12. **Werdykt: NIE jest gotowe do publicznego wydania produkcyjnego.** Serwer jest gotowy na zamknięte testy (alfa wewnętrzna) na PostgreSQL; klient Unity i infrastruktura produkcyjna nie zostały zweryfikowane. Uzasadnienie i lista blokad poniżej — każda pozycja ma warunek zamknięcia.

## Co jest zweryfikowane (uruchomione, zielone)
- 15 modułów serwera (13 z 14 domenowych; Social poza zakresem v1), 16 migracji, 78 testów jednostkowych, 140 integracyjnych na PostgreSQL (138 + 2 NOT RUN na in-memory), 7 plików testów integralności SQL, ćwiczenie backup/restore, przepływ end-to-end, przegląd odporności wszystkich endpointów.
- Wszystkie 13 obowiązkowych testów akceptacyjnych (§14) — patrz `verification-report.md`.
- Ekonomia: rejestry niezmienne z triggerami, rekonsyliacja (`/health/economy`), atomowe zakupy i rzemiosło, idempotencja nagród.

## Wyniki testu obciążeniowego
Środowisko: **2 vCPU współdzielone** przez API (z Workerem w procesie), PostgreSQL 16 i generator obciążenia — wyniki są dolną granicą, nie prognozą produkcji. Narzędzie: `tools/load-test` (50 wirtualnych graczy, 60 s, każdy: rejestracja → postać → pętla: odczyty, medytacja, targ, powiadomienia, walka PvE z manekinem).

| Operacja | realistyczny p50 / p95 (ms) | stres p50 / p95 (ms) |
|---|---|---|
| odczyt postaci / kultywacji / sakwy / targu / powiadomień | 52–68 / 111–138 | 86–116 / 176–283 |
| medytacja start / koniec | 69 / 140 · 97 / 271 | 111 / 233 · 139 / 820 |
| start walki PvE | 288 / 2 371 | 498 / 3 761 |
| akcja w walce | 251 / 3 362 | 422 / 3 979 |
| rejestracja / logowanie (PBKDF2 100k, celowo kosztowne) | 2 952 / 6 655 · 1 645 / 4 644 | 6 254 / 9 789 · 1 820 / 4 468 |

Przepustowość 101 req/s (realistyczny) i 70 req/s (stres), **0 % błędów**, 78/62 odpowiedzi `503 busy_retry` skutecznie ponowionych, rekonsyliacja ekonomii czysta po teście.

**Budżet p95 < 500 ms dla rozgrywki: spełniony dla odczytów i medytacji, NIESPEŁNIONY dla walki.** Przyczyny: nasycenie CPU (hashowanie haseł, silnik walki, baza na tych samych 2 rdzeniach) oraz konflikty SERIALIZABLE w ścieżce walki (każda tura zapisuje bitwę, turę, idempotencję, audyt i — na końcu — outbox, mistrzostwo technik; handlery nagród dotykają kultywacji i portfela tego samego gracza).

## Blokady wydania
| # | Blokada | Warunek zamknięcia |
|---|---|---|
| B1 | (Częściowo zamknięte w MVP 1.0.0: natywny klient Android zbudowany i przechodzi grę na emulatorze w CI — [mvp.md](mvp.md); nadal brak testów na fizycznych urządzeniach.) Klient Unity nigdy nie był skompilowany ani uruchomiony w Unity; brak testów na urządzeniach, brak profilowania (CPU/GPU/pamięć/bateria/GPS), UI to prototyp list bez grafiki | Build Android/iOS z Unity 6 LTS, testy na ≥ 3 urządzeniach, profil Unity Profiler (klatka < 16 ms, brak alokacji w pętli), zgoda lokalizacji na obu platformach |
| B2 | Opóźnienie walki pod obciążeniem (p95 3,4 s na 2 vCPU) | Test na docelowym sprzęcie (osobna baza, ≥ 4 rdzenie API) z budżetem p95 < 500 ms; jeśli nie: tura walki w READ COMMITTED z jawną blokadą wiersza bitwy (`SELECT … FOR UPDATE`) zamiast SSI, partycjonowanie tabel walk |
| B3 | Obrazy Docker i compose niezbudowane w tym środowisku (brak rejestrów) | Build obrazów w CI, skan obrazów, uruchomienie compose ze smoke testem |
| B4 | Token odświeżania w `PlayerPrefs` (tylko DEV) | Android Keystore / iOS Keychain |
| B5 | Rate limiting w pamięci procesu | Rozproszony limiter (Redis) przy > 1 instancji API |
| B6 | Fałszywy GPS poruszający się „realistycznie” jest niewykrywalny | Atestacja urządzenia (Play Integrity / App Attest) wiązana z sesją eksploracji |
| B7 | Brak modułu AntiAbuse: multikonta, pranie waluty na targu, ustawione pojedynki wojenne, farmienie łupów | Korelacja kont/urządzeń, reguły i kolejka przeglądu, panel operatora |
| B8 | Usuwanie konta (RODO art. 17) niezaimplementowane; część retencji „do potwierdzenia” | Decyzja prawna o okresach, anonimizacja zamiast kasowania rejestrów, endpoint i proces usunięcia |
| B9 | Brak powiadomień push, brak modułu Social (czat, znajomi) | Decyzja produktowa: v1 bez nich albo implementacja |
| B10 | Balans ekonomii nieweryfikowany symulacją (walka ma symulator, gospodarka nie) | Symulacja przepływów KD (źródła vs odpływy) dla 30 dni gry |
| B11 | Obserwowalność: brak eksportera OTLP, dashboardów i alertów | OpenTelemetry → system metryk; alerty: `/health/economy`, lag outboxa, 5xx, 503 `busy_retry` |
| B12 | Kopie: ćwiczenie pg_dump/pg_restore działa; PITR (WAL) nieskonfigurowany | Ciągła archiwizacja WAL, ćwiczenie odtworzenia do punktu w czasie, RPO/RTO zapisane |
| B13 | Prawne: polityka prywatności (lokalizacja!), regulamin, klasyfikacja wiekowa | Dokumenty i zgody w kliencie |

## Ryzyka zaakceptowane świadomie (nie blokują alfy)
- Leniwe rozstrzyganie wojen: odczyt może zapisywać (transakcja idempotentna przez wersję).
- Członkostwo liczone w chwili przetworzenia zdarzenia walki, nie w chwili walki (różnica sekund).
- Rozwiązanie sekty: skarbiec przepada, nazwa zostaje zajęta.
- Techniki Dao nie podlegają rebalansowi wstecz.

## Rekomendacja
1. Zamknięta alfa serwera z klientem testowym (rdzeń klienta jest przetestowany na prawdziwym API) — **możliwa teraz**.
2. Przed betą: B1, B2, B3, B4, B6, B8, B11.
3. Przed wydaniem publicznym: wszystkie blokady.
