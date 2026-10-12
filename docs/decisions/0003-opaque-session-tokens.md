# ADR 0003 — Nieprzezroczyste tokeny sesji zamiast JWT

**Status:** przyjęta (Etap 2)

**Kontekst.** Wymagania: krótkotrwały token dostępu, bezpieczne odświeżanie, natychmiastowe unieważnienie (wylogowanie, kradzież). Pakiet `Microsoft.AspNetCore.Authentication.JwtBearer` był niedostępny.

**Decyzja.** Tokeny losowe 256-bit z CSPRNG, prefiksy `jga_` (access, 15 min) i `jgr_` (refresh, 30 dni). W bazie tylko SHA-256. Każde odświeżenie **rotuje** parę; użycie zrotowanego refresh tokena = podejrzenie kradzieży → unieważnienie całej rodziny sesji. Hasła: `PasswordHasher` (PBKDF2-HMAC-SHA512, format Identity v3).

**Konsekwencje.**
- (+) Natychmiastowe unieważnienie; brak kluczy podpisujących do ochrony i rotacji; prefiksy wykrywalne przez skanery sekretów.
- (−) Każde żądanie = odczyt sesji. Przy skali: cache Redis z krótkim TTL i unieważnianiem przy revoke (Etap 8, test obciążeniowy zdecyduje).
