# Moduły i dozwolone zależności

| Projekt | Może zależeć od |
|---|---|
| `JianghuGo.Domain` | — (tylko BCL) |
| `JianghuGo.Application` | Domain |
| `JianghuGo.Infrastructure` | Application, Domain |
| `JianghuGo.Modules.Identity` | Infrastructure, Application, Domain |
| `JianghuGo.Modules.Characters` | Identity (wyłącznie publiczne API: `JianghuClaims`), Infrastructure, Application, Domain |
| `JianghuGo.Modules.Combat` | Characters (**tylko** `Characters.Contracts`), Identity (claims), Infrastructure, Application, Domain |
| `JianghuGo.Modules.Cultivation` | Characters.Contracts, Combat.Contracts (zdarzenie nagrody), Identity (claims), Infrastructure, Application, Domain |
| `JianghuGo.Modules.Notifications` | zdarzenia publikowane przez Characters i Cultivation (konsumowane z outboxa), Identity (claims), Infrastructure, Application, Domain |
| `JianghuGo.Composition` | wszystkie moduły — wspólna rejestracja usług dla hostów |
| `JianghuGo.Api`, `JianghuGo.Worker` | Composition (hosty) |

Reguły: brak cykli; moduł nie czyta tabel innego modułu; współpraca synchroniczna tylko przez kontrakty (`Characters.Contracts.ICharacterDirectory`), asynchroniczna przez zdarzenia z transactional outbox (ADR 0006). Test `Cross_module_usage_goes_through_published_contracts` pilnuje, by moduły używały wyłącznie przestrzeni `*.Contracts` innych modułów.
Wewnątrz modułu: `Domain/` (czyste reguły), `Application/` (przypadki użycia, porty), `Infrastructure/` (adaptery), `Api/` (endpointy).

## Status 14 modułów domenowych

| # | Moduł | Status |
|---|---|---|
| 1 | Identity | ✅ Etap 2 |
| 2 | Characters | ✅ Etap 2 (tworzenie postaci; rozwój postaci w kolejnych etapach) |
| 3 | Cultivation | ✅ Etap 3 (Płomień: punkty GPS w Etapie 5) |
| 4 | Combat | ✅ Etap 4 (Laboratorium Dao i zwoje — Etap 6) |
| 5 | Inventory | Etap 6 |
| 6 | Crafting | Etap 6 |
| 7 | Geolocation | Etap 5 |
| 8 | World | Etap 5 |
| 9 | Sects | Etap 7 |
| 10 | Territory | Etap 7 |
| 11 | Market | Etap 6 |
| 12 | Social | po Etapie 7 |
| 13 | Notifications | ✅ Etap 3 (powiadomienia w grze; push na urządzenia później) |
| 14 | AntiAbuse | częściowo: rate limiting, blokada konta, idempotencja; reguły GPS w Etapie 5 |

Moduły nieistniejące nie mają pustych projektów — powstają w etapie, w którym dostają działający kod i testy.
