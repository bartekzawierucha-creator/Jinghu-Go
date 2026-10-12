# ADR 0012 — Retencja danych: polityki należą do modułów, wykonuje je Worker

**Status:** przyjęta (Etap 8)

**Decyzje.** Każdy moduł rejestruje `RetentionPolicy(name, SQL usuwający jedną partię)` dla swoich tabel (tylko PostgreSQL). `RetentionRunner` (Infrastructure, uruchamiany w procesie Worker co `Retention:IntervalMinutes`, domyślnie 60) wykonuje partie po 1000 wierszy w krótkich transakcjach, aż partia wróci niepełna; metryka `jianghu.retention.purged{policy}`. Tabele rejestrów, audytu i historii są append-only (triggery) — polityka nie może ich dotknąć nawet przez pomyłkę.

**Konsekwencje.** Nowa tabela z danymi tymczasowymi = nowa polityka w module + wiersz w `docs/operations/data-retention.md` + test. In-memory nie ma retencji (nie jest trwały).
