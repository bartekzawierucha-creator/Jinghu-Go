# Baza danych

- `migrations/Vnnnn__opis.sql` — stosowane rosnąco przez `migrate.sh`, każda w osobnej transakcji, z sumą SHA-256 w `public.schema_migrations`. Zastosowanego pliku **nie edytujemy** (migrator przerwie pracę).
- `tests/*.sql` — testy integralności; `test.sh` tworzy tymczasową bazę, stosuje migracje, sprawdza idempotencję migratora i wykrywanie zmian, uruchamia testy, usuwa bazę.

```bash
DATABASE_URL=postgres://jianghu:…@localhost:5432/jianghu ./database/migrate.sh
ADMIN_URL=postgres://jianghu:…@localhost:5432/postgres   ./database/test.sh
```

`CREATE EXTENSION postgis` wymaga roli z odpowiednimi uprawnieniami (w obrazie `postgis/postgis` — użytkownik główny). Na produkcji rozszerzenie zakłada DBA, a migracja używa `IF NOT EXISTS`.
