# ADR 0004 — Własny minimalny runner testów

**Status:** przyjęta tymczasowo

**Kontekst.** xUnit/NUnit/MSTest niedostępne (brak NuGet). Testy integracyjne potrzebują prawdziwego pipeline'u HTTP — `Microsoft.AspNetCore.TestHost` też niedostępny.

**Decyzja.** `server/tests/TestKit`: atrybut `[Fact]`, `IAsyncLifetime`, `Assert.*` o nazwach jak w xUnit; projekty testowe to aplikacje konsolowe (`dotnet run --project …`), kod wyjścia ≠ 0 przy jakimkolwiek błędzie **oraz gdy nie uruchomiono żadnego testu**. Testy HTTP startują prawdziwy Kestrel na `127.0.0.1:0`.

Testy wymagające PostgreSQL rzucają `SkipException` → raportowane jako **NOT RUN**, nigdy jako zaliczone; przebieg, w którym nic nie zostało uruchomione, kończy się błędem.

**Migracja.** Gdy NuGet będzie dostępny, zamiana na xUnit jest mechaniczna (`using Xunit;`, usunięcie `Program.cs`, `IAsyncLifetime` ma tę samą sygnaturę). Do tego czasu `dotnet test` nie jest używany.
