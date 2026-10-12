# Moduły i dozwolone zależności

| Projekt | Może zależeć od |
|---|---|
| `JianghuGo.Domain` | — (tylko BCL) |
| `JianghuGo.Application` | Domain |
| `JianghuGo.Infrastructure` | Application, Domain |
| `JianghuGo.Modules.Identity` | Infrastructure, Application, Domain |
| `JianghuGo.Modules.Characters` | Identity (wyłącznie publiczne API: `JianghuClaims`), Infrastructure, Application, Domain |
| `JianghuGo.Api` | wszystkie moduły (composition root) |

Reguły: brak cykli; moduł nie czyta tabel innego modułu; współpraca przez publiczne typy `…Api`/`…Application` lub (od Etapu 3) zdarzenia przez transactional outbox.
Wewnątrz modułu: `Domain/` (czyste reguły), `Application/` (przypadki użycia, porty), `Infrastructure/` (adaptery), `Api/` (endpointy).

## Status 14 modułów domenowych

| # | Moduł | Status |
|---|---|---|
| 1 | Identity | ✅ Etap 2 |
| 2 | Characters | ✅ Etap 2 (tworzenie postaci; rozwój postaci w kolejnych etapach) |
| 3 | Cultivation | Etap 3 |
| 4 | Combat | Etap 4 |
| 5 | Inventory | Etap 6 |
| 6 | Crafting | Etap 6 |
| 7 | Geolocation | Etap 5 |
| 8 | World | Etap 5 |
| 9 | Sects | Etap 7 |
| 10 | Territory | Etap 7 |
| 11 | Market | Etap 6 |
| 12 | Social | po Etapie 7 |
| 13 | Notifications | Etap 3 (razem z outboxem) |
| 14 | AntiAbuse | częściowo: rate limiting, blokada konta, idempotencja; reguły GPS w Etapie 5 |

Moduły nieistniejące nie mają pustych projektów — powstają w etapie, w którym dostają działający kod i testy.
