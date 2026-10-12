# ADR 0011 — Sekty i terytoria: uprawnienia w kodzie, pozycja jako dowód obecności, leniwe rozstrzyganie wojen

**Status:** przyjęta (Etap 7)

**Decyzje.**
1. **Macierz uprawnień w kodzie** (`SectPolicy`), testowana jednostkowo i integracyjnie dla każdej akcji. Konfiguracja zawiera liczby (opłaty, limity), nie reguły dostępu.
2. **Wersja sekty jako punkt serializacji.** Każda zmiana członkostwa, ról, wkładów i siedziby podbija `sects.version`; równoległe zmiany jednej sekty kolidują na jednym wierszu (SERIALIZABLE + warunek wersji). Indeksy bazy dodatkowo gwarantują: jedna sekta na postać, jeden przywódca, jedna oczekująca prośba na parę.
3. **Skarbiec jako rejestr** z triggerem i widokiem rekonsyliacji, tak jak portfele (ADR 0009).
4. **Terytorium = funkcja zweryfikowanej pozycji.** Endpointy `claim`/`challenge` nie przyjmują klucza; serwer liczy go z pozycji z modułu Geolocation (`IPositionVerifier`). Fałszywa pozycja podlega tym samym zabezpieczeniom co nagrody świata (ADR 0008).
5. **Wojna punktowana zdarzeniami walki.** Territory konsumuje `combat.battle_finished` (PvP) z outboxa; `territory.war_battles (war_id, battle_id)` daje jednokrotność. Nie ma osobnego trybu walki „wojennej” — gracze używają zwykłych wyzwań PvP.
6. **Leniwe rozstrzyganie** przy najbliższym odczycie/działaniu po oknie. Brak zadania w tle oznacza brak ryzyka, że zatrzymany proces „zamrozi” wojnę; koszt: odczyt może zapisywać (rozstrzygnięcie w tej samej transakcji, idempotentne przez wersję wojny).

**Konsekwencje.** Członkostwo w chwili przetwarzania zdarzenia (nie w chwili walki) decyduje o punkcie — różnica sekund, akceptowalna. Rozwiązanie sekty nie kasuje danych (historia, wojny, własność zostają jako zapis).
