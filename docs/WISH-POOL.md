# MU client wish pool

The existing WinForms header now includes **许愿池** (page 8). The page uses the supplied reference's dark surfaces, starry pool artwork, green/yellow submit button, status chips and voting rows. The current client shell retains its top navigation.

## Behavior

- Signed-in accounts can read, submit, vote/unvote and comment without a Pro membership.
- Wishes contain 5-500 characters and an optional image. The client compresses PNG/JPEG input to JPEG (960 px maximum side, at most 250 KB); the API checks PNG/JPEG headers, rejects invalid or excessive dimensions (4 megapixels maximum), and limits stored image data. Images appear in the detail dialog.
- Filters: all, popular (50+ votes), planned, developing, completed and mine; search, newest/most-supported sort and 20-item pagination.
- Submission IDs survive retries; voting uses an idempotent PUT and a composite database key. A real user's vote is independent of demo vote totals.
- Real wishes begin in `pending`. Daily submission limit: 10 per account. Comments: 1-300 characters, 30 per account per hour; details return the latest 100 comments.
- Authors use pseudonyms derived from account IDs. The list never returns email addresses or image bodies.
- Network failures preserve the submission draft and expose a refresh action. Logout clears list data and the draft; stale list requests are cancelled.

## Database and sample content

`Wishes`, `WishVotes` and `WishComments` live in the account SQLite database. Startup creates these additive module tables and their indexes for fresh and existing supported databases; the account schema version and existing migrations are preserved. Before production activation, back up the database using the existing server deployment procedure.

Sample content is **opt-in** and clearly labelled `IsDemo`. Eight sample wishes use fixed IDs and separate `DemoVotes`; no fake user accounts or comments are created. Repeating the seed command preserves existing entries and real submissions.

```sh
Database__Path=/absolute/path/accounts.sqlite \
DataProtection__Path=/absolute/path/keys \
dotnet server/Mu.Server/bin/Release/net10.0/Mu.Server.dll --seed-wishes
```

Local seeded database for this delivery: `artifacts/wishes-demo/accounts.sqlite` (ignored by Git). The production service was subsequently activated with eight labelled demo wishes; see `docs/WISH-POOL-DELIVERY.md` for the verified deployment baseline.

## Administration

`/admin/wishes` uses the existing administrator role, MFA and CSRF protections. Administrators can search/read wishes, view attached images and discussions, and set pending/planned/developing/completed progress with an audit reason. Status changes write `wish.status` audit entries in the same transaction. Stale forms cannot overwrite a newer status.

## API

All routes require the existing client Bearer session:

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/v1/wishes?filter=all&q=&sort=newest&page=1` | List and global filter counts |
| POST | `/api/v1/wishes` | Submit `{ submissionId, content, imageData? }` |
| GET | `/api/v1/wishes/{id}` | Detail, image, comments |
| PUT | `/api/v1/wishes/{id}/vote` | Set `{ voted: true/false }` |
| POST | `/api/v1/wishes/{id}/comments` | Comment `{ content }` |

Only the wish submission path receives a 384 KiB request limit; other account API routes keep their existing limit. The client uses its existing refresh/retry transport.

## Artwork and visual review

- Asset: `assets/mu-wish-pool-hero.png`, embedded as `AmdNrAssistant.mu-wish-pool-hero.png`.
- Created with the built-in imagegen tool. Prompt: panoramic midnight wishing pool, luminous golden five-pointed star over cyan water in a violet stone pool, navy/indigo sky and tiny sparkles, polished 3D game illustration, subject on the right and dark empty left for UI copy; no lettering, logo or interface.
- `docs/wish-pool-preview.html` is an interactive **layout preview**, with search/filter/sort on labelled sample content. It is not a screenshot of a running Windows client and does not submit or vote.

## Validation and Windows acceptance

```sh
dotnet build src/AmdNrAssistant.csproj -c Release
dotnet run --project server/Mu.Server.HttpTests -c Release -- --wishes
dotnet run --project server/Mu.Server.Tests -c Release
dotnet run --project server/Mu.Server.HttpTests -c Release
dotnet run --project server/Mu.Server.AdminTests -c Release
```

On Windows, verify the actual client at 1200x740 and 1440x910, at 100%, 125%, 150% and 200% DPI: all six navigation entries, hero crop/text contrast, composer native textbox paint/focus, scrolling and empty state, attachment add/remove/detail preview, submission failure/retry, repeated vote/unvote, comments and counts, switching accounts, light/dark themes, and administrator status updates after refresh. macOS cross-compilation and browser layout review do not establish Windows rendering acceptance.

Verified on 2026-10-08: desktop and server compile successfully; wish HTTP suite 62 assertions (including concurrent submission/vote retries and JPEG/PNG storage), full HTTP regression 409 assertions, administrator/backup regression 114 assertions, account service 156 assertions, compatibility service 86 assertions and metadata 29 assertions. Browser preview checked at 1200x740 and 1440x1000, including filtering/search and horizontal overflow. Preview captures are under `output/playwright/wish-pool-preview-*.png` (ignored by Git).

This implementation does not change public website files or release metadata. The corrected Windows test package and server activation are recorded in `docs/WISH-POOL-DELIVERY.md`.
