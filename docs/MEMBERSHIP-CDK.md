# Membership CDK

Administrators with an MFA session can open `/admin/codes` to generate Pro
membership codes (1-3650 membership days, 1-100 codes per batch, 1-3650 days
to redeem). Copy the codes immediately: plaintext is displayed only in the
generation response. The database stores SHA-256 hashes of random 128-bit codes.
Unused codes can be revoked from the same page; the latest 100 records show
redemption status and the redeeming user ID.

Signed-in desktop users enter a code on the account page. The client posts
`{ "code": "..." }` to `POST /api/v1/account/redeem` with its bearer token.
The response is the refreshed account snapshot. Codes are case-insensitive;
outer whitespace and hyphens are ignored. Manual payment availability does
not affect CDK redemption.

Each code grants membership exactly once. The new expiry is
`max(current_expiry, now) + duration_days * 86400`. Consumption, expiry update,
membership history and audit are committed in one SQLite transaction. Concurrent
redemptions cannot both claim a code. Invalid, used, expired or revoked codes
return `409/code_unavailable`; malformed codes return `400/invalid_code`.
The endpoint requires client authentication and uses the existing auth rate limit.

Startup adds the `MembershipCodes` table and indexes to existing account databases.
Back up the account database using the existing operational procedure before
deploying the server. Deploy the server and an updated Windows client to expose
the complete flow. This change does not deploy or publish either component.
