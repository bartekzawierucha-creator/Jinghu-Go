# Retencja danych

| Dane | Cel | Retencja | Dostęp |
|---|---|---|---|
| `identity.accounts` | logowanie | do usunięcia konta | serwer |
| `identity.sessions` | uwierzytelnianie, wykrywanie kradzieży | 30 dni po wygaśnięciu refresh tokena | serwer |
| `platform.idempotency_records` | ochrona przed ponowieniami | 7 dni | serwer |
| `platform.audit_log` | audyt bezpieczeństwa i ekonomii | 2 lata (do potwierdzenia prawnie) | administracja, append-only |
| rejestr Żetonów Losu | ekonomia | bez limitu (księgowość) | serwer |
| `platform.outbox_messages` | dostarczanie zdarzeń | przetworzone: 7 dni; `dead`: do ręcznej analizy | serwer, operator |
| `cultivation.breakthroughs` | audyt i odtwarzanie rzutów | bez limitu (mały wolumen) | serwer |
| `notifications.notifications` | powiadomienia w grze | 90 dni (do potwierdzenia) | właściciel konta |
| lokalizacja (Etap 5) | walidacja eksploracji | **minimalna**: surowe próbki ≤ 24 h, potem tylko agregaty sesji | serwer |

Zadania czyszczące: do dodania jako okresowe zadania Workera (Etap 8, razem z testami obciążeniowymi); indeksy pod nie już istnieją. Usunięcie konta z postaciami: blokowane przez FK (`ON DELETE RESTRICT`) — wymaga procedury anonimizacji (do zaprojektowania przed wydaniem).
