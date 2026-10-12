# ADR 0006 — Transactional outbox i osobny Worker

**Status:** przyjęta (Etap 3)

**Decyzja.**
- Moduł publikuje zdarzenie integracyjne (`characters.character_created`, `cultivation.breakthrough_*`) wpisem do `platform.outbox_messages` **w tej samej transakcji** co zmiana stanu.
- `OutboxDispatcher` przetwarza **jedną wiadomość na transakcję**: `FOR UPDATE SKIP LOCKED` → handlery → oznaczenie `processed` → commit. Awaria w dowolnym momencie przed commitem = pełny rollback; wiadomość wraca do kolejki.
- Gwarancja: dostarczenie *co najmniej raz*; skutki *dokładnie raz* dzięki idempotentnym handlerom (np. `UNIQUE(account_id, source_message_id)` w powiadomieniach). Nie obiecujemy globalnego „exactly once”.
- Błędy handlera: wykładniczy backoff (2^n s, maks. 10 min), po 8 próbach status `dead` (dead letter) → readiness `Degraded`.
- Hosty: `JianghuGo.Worker` (osobny proces, wymaga PostgreSQL) w produkcji; `Worker:RunInProcess=true` w API dla developmentu in-memory. Kilka workerów może działać równolegle (SKIP LOCKED).

**Weryfikacja.** Testy: crash handlera (rollback i ponowne dostarczenie), redelivery bez duplikatu, dead letter, pełny restart procesu API na tej samej bazie, **prawdziwy proces Workera** zabijany `kill` i uruchamiany ponownie (`WorkerProcessTests`).
