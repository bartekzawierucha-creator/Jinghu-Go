# Kopie zapasowe i odtwarzanie

**Status: NIEPRZETESTOWANE.** Serwer używa PostgreSQL od Etapu 3, ale procedura backup/restore nie została jeszcze przećwiczona.

Plan na PostgreSQL:
- Ciągła archiwizacja WAL + codzienny base backup (np. pgBackRest / zarządzana usługa z PITR).
- Proponowane cele: **RPO ≤ 5 min, RTO ≤ 1 h** — do potwierdzenia po wyborze infrastruktury i kosztów; nie są spełnione, dopóki nie przejdzie test odtwarzania.
- Test odtwarzania (Etap 8): odtworzenie do nowej instancji, `database/migrate.sh` (musi być no-op), uruchomienie `database/tests` w trybie tylko-odczyt + widok rekonsyliacji rejestru = pusty.

Migracje: `JianghuGo.Api migrate` lub `database/migrate.sh` stosuje pliki w osobnych transakcjach i odmawia działania, jeśli zastosowany plik zmieniono (suma SHA-256).
