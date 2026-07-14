# To-fix — deferred code-review findings (PLAT-747 PAT expiry notifications)

Deferred from the code review.

## Resolved (2026-07-09)

### 1. `PatExpiryEmailBuilder.Build` dereferences `token.User` without a null guard — FIXED
- `Build` now throws `ArgumentException` when `token.User` is null or `token.User.Email` is null/empty.
- Tests: `PatExpiryEmailBuilderTests.ThrowsWhenTokenHasNoUser`, `ThrowsWhenTokenOwnerHasNoEmail`.

### 2. Entra `email` derived from `preferred_username` — FIXED
- `EntraRoleSyncService` now derives the stored mailbox as `ClaimTypes.Email ?? mail ?? ClaimTypes.Upn ?? upn`; `preferred_username` is no longer used as the mailbox.
- Tests: `EntraRoleSyncServiceTests.StoresUpnAsEmailWhenNoMailClaimPresent`, `DoesNotUsePreferredUsernameAsEmail`.

### 3. Test coverage gaps — FIXED
- `SeedToken`'s `alreadyNotified` param is now exercised by `DoesNotResendWhenAlreadyNotifiedAtANearerThreshold` (directly tests the `already <= due` skip).
- Already-expired token: was already closed by `SkipsTokensThatHaveAlreadyExpired`.
- `WebBaseUrl` no-trailing-slash: `IncludesTokenPageLinkWhenBaseUrlHasNoTrailingSlash`.
- `SmtpEmailSender` direct tests: `BuildMimeMessage` extracted (`internal`) and covered by `SmtpEmailSenderTests` (sender, recipients, subject, HTML vs text body).

## Still open

### 4. `.gitignore` bundles an unrelated DataProtection rule into this changeset
- File: `.gitignore` (`# keyring` / `DataProtection`, lines 284-285)
- Belongs to the already-committed data-protection key-storage feature, not PAT-expiry email. Mixing features complicates review and a clean revert.
- Fix idea: move that ignore rule into its own commit with the data-protection change.
