# Retencja danych

| Dane | Cel | Retencja | Dostęp |
|---|---|---|---|
| `identity.accounts` | logowanie | do usunięcia konta | serwer |
| `identity.sessions` | uwierzytelnianie, wykrywanie kradzieży | 30 dni po wygaśnięciu refresh tokena | serwer |
| `platform.idempotency_records` | ochrona przed ponowieniami | 7 dni | serwer |
| `platform.audit_log` | audyt bezpieczeństwa i ekonomii | 2 lata (do potwierdzenia prawnie) | administracja, append-only |
| rejestr Żetonów Losu | ekonomia | bez limitu (księgowość) | serwer |
| lokalizacja (Etap 5) | walidacja eksploracji | **minimalna**: surowe próbki ≤ 24 h, potem tylko agregaty sesji | serwer |

Zadania czyszczące powstaną razem z Workerem (Etap 3). Usunięcie konta z postaciami: blokowane przez FK (`ON DELETE RESTRICT`) — wymaga procedury anonimizacji (do zaprojektowania przed wydaniem).
