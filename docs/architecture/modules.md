# Moduły i dozwolone zależności

| Projekt | Może zależeć od |
|---|---|
| `JianghuGo.Domain` | — (tylko BCL) |
| `JianghuGo.Application` | Domain |
| `JianghuGo.Infrastructure` | Application, Domain |
| `JianghuGo.Modules.Identity` | Infrastructure, Application, Domain |
| `JianghuGo.Modules.Characters` | Identity (wyłącznie publiczne API: `JianghuClaims`), Infrastructure, Application, Domain |
| `JianghuGo.Modules.Inventory` | Characters.Contracts, Identity (claims), Infrastructure, Application, Domain. Zdarzenia Combat/World czyta przez własne lustra ładunku (ADR 0009) |
| `JianghuGo.Modules.Crafting` | Inventory.Contracts (`IInventory`, `IItemCatalog`), Characters.Contracts, Identity, Infrastructure, Application, Domain |
| `JianghuGo.Modules.Market` | Inventory.Contracts, Characters.Contracts, Identity, Infrastructure, Application, Domain |
| `JianghuGo.Modules.Combat` | Characters (**tylko** `Characters.Contracts`), Inventory.Contracts (`IEquipmentStats`), Identity (claims), Infrastructure, Application, Domain |
| `JianghuGo.Modules.Geolocation` | Characters.Contracts, Identity (claims), Infrastructure, Application, Domain |
| `JianghuGo.Modules.World` | Geolocation.Contracts (`IPositionVerifier`), Combat.Contracts (`ICombatEncounters`), Characters.Contracts, Identity, Infrastructure, Application, Domain |
| `JianghuGo.Modules.Cultivation` | Characters.Contracts, Combat.Contracts, World.Contracts i Inventory.Contracts (zdarzenia nagród i pigułek), Identity (claims), Infrastructure, Application, Domain |
| `JianghuGo.Modules.Notifications` | zdarzenia publikowane przez Characters, Cultivation, Combat i Market (konsumowane z outboxa), Identity (claims), Infrastructure, Application, Domain |
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
| 4 | Combat | ✅ Etap 4 + premie ekwipunku (Etap 6); Laboratorium Dao i zwoje — kolejny krok |
| 5 | Inventory | ✅ Etap 6 (rejestry KD i stosów, ekwipunek, pigułki, łupy, materiały ze świata) |
| 6 | Crafting | ✅ Etap 6 (kucie, alchemia, ulepszanie +1…+15) |
| 7 | Geolocation | ✅ Etap 5 |
| 8 | World | ✅ Etap 5 + materiały z punktów (Etap 6) |
| 9 | Sects | Etap 7 |
| 10 | Territory | Etap 7 |
| 11 | Market | ✅ Etap 6 (oferty z depozytem, opłata, podatek, atomowy zakup) |
| 12 | Social | po Etapie 7 |
| 13 | Notifications | ✅ Etap 3 (powiadomienia w grze; push na urządzenia później) |
| 14 | AntiAbuse | częściowo (rozproszone): rate limiting, blokada konta, idempotencja, anomalie GPS, przycinanie kroków, audyt sygnałów; osobny moduł z regułami i panelem — Etap 8 |

Moduły nieistniejące nie mają pustych projektów — powstają w etapie, w którym dostają działający kod i testy.
