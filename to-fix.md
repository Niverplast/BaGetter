# To-fix — deferred code-review findings (PLAT-747 PAT expiry notifications)

Deferred from the code review. Items 1–7 are being fixed now; these are left for later.

## 1. `PatExpiryEmailBuilder.Build` dereferences `token.User` without a null guard
- File: `src/BaGetter.Core/Notifications/PatExpiryEmailBuilder.cs:40`
- `Build` null-checks `token` but then does `new EmailMessage(token.User.Email, …)`. The only current caller (the scanner) guards `token.User` first, so it's not triggerable today, but this is a public reusable service — any future caller passing a token queried without `Include(t => t.User)` gets a `NullReferenceException`.
- Fix idea: null-check `token.User`/`token.User.Email` and throw a clear `ArgumentException`, or take the recipient address as a parameter.

## 2. Entra `email` derived as `ClaimTypes.Email ?? preferred_username` is persisted as the mailbox
- File: `src/BaGetter/Authentication/EntraRoleSyncService.cs:54,67,74`
- `preferred_username` is often a UPN, not a deliverable mailbox. Now that `User.Email` drives notification delivery, tenants where UPN ≠ mailbox will have warnings bounce.
- Fix idea: prefer a verified mail claim (`ClaimTypes.Email` / `mail` / `upn`) and don't fall back to `preferred_username` for the stored email; or validate deliverability.

## 3. Test coverage gaps
- Files: `tests/BaGetter.Core.Tests/Notifications/*`, `tests/BaGetter.Core.Tests/Email/EmailSenderTests.cs`
- `SeedToken`'s `alreadyNotified` param is dead (never passed) → the `already <= due` skip is only tested indirectly.
- No service-level test seeds an already-expired token (a regression in the new lower bound would pass silently).
- `WebBaseUrl` link only tested with a trailing slash; the no-slash form (as shown in appsettings) is untested.
- `SmtpEmailSender` has zero direct tests despite the file name `EmailSenderTests.cs`.

## 4. `.gitignore` bundles an unrelated DataProtection rule into this changeset
- File: `.gitignore` (`# keyring` / `DataProtection`)
- Belongs to the already-committed data-protection key-storage feature, not PAT-expiry email. Mixing features complicates review and a clean revert.
- Fix idea: move that ignore rule into its own commit with the data-protection change.
