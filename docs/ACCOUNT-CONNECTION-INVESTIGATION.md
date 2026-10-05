# Account connection investigation

Investigated on 2026-10-05. User-facing times below use Asia/Shanghai (UTC+8).
The customer screenshot shows the client's generic `HttpRequestException`
message. It does not include a request identifier or the failure time.

## Confirmed evidence

- Cloudflared recorded five interrupted `/api/v1/auth/login` requests at
  2026-10-03 23:59:00 and 2026-10-04 00:05:28, 00:07:04, 00:08:17, 00:09:02.
- Five `/api/v1/auth/refresh` requests were also interrupted during that period.
- Those records report `stream ... canceled by remote with error code 0`,
  `connIndex=1`, and edge address `198.41.192.77`. That address belongs to the
  tunnel's remote endpoint; it is not an identified customer's address.
- An additional QUIC connection timeout occurred on 2026-10-05 at 03:11:56.
  That connection registered again at 03:12:14. Other tunnel connections existed;
  this does not prove an 18-second outage of the entire account service.
- At inspection the account container had not restarted since
  2026-09-28 01:33:15. No application failure was found in the recent error logs.
  Container uptime and a healthy probe do not prove customer connectivity.

## Request logging enabled

The old configuration suppresses normal ASP.NET Core HTTP access records.
At 2026-10-05 14:34:05, the active release configuration was updated to enable
`Microsoft.AspNetCore.Hosting.Diagnostics=Information`. Configuration reload
was verified by actual request-start and request-finish messages; the account
container was not restarted. No request bodies or authorization headers are
enabled by this category.

- Active release: `/home/xrw/amd-dlss-mu-account/releases/20260928-hardware-data`.
- Original configuration backup:
  `/home/xrw/amd-dlss-mu-account/backups/request-logging-20261005T063405Z.json`.
- Repeatable activation: `tools/enable-account-request-logging.sh`.
- Repository `server/Mu.Server/appsettings.json` carries the same logging setting.

Between 14:34:07 and 14:38:09, 184 completed requests were observed: 183 returned
200, and one returned 400. The 400 was our deliberately invalid login probe,
not a customer's failed login. Five real session refreshes returned 200.
Maximum observed origin processing time was 40.69 ms; this excludes network
time between the client and the origin. No real new login appeared in that sample.

## Remaining attribution gap

The tunnel records prove interrupted account requests occurred, but remote stream
cancellation alone does not identify the component that caused cancellation.
The historical application logs do not contain per-request status/duration or
customer correlation. The screenshot's failure time must be matched before
claiming that these historical interruptions explain that customer's report.
No customer-side diagnostic script or change is required for this investigation.

## Customer timestamp supplied: 2026-10-05 around 14:04

The user subsequently supplied this timestamp. The Cloudflared journal and
syslog were checked from 13:54 to 14:14. No account-route interruption or tunnel
connection failure appears in that interval. The sole matching tunnel error
is a canceled website download at 13:57:38 on the separate port-8088 origin.
The older login cancellations above must not be attributed to this report.

The database contains two session issuances during 14:04, both belonging to
existing session families. No new session family attributable to password login
was created in the 13:54-14:14 interval; the three new families in that interval
coincide with new user registrations at 13:55:51, 14:00:01 and 14:03:03.
These are aggregate facts, not attribution to the customer in the screenshot.

HTTP status/duration logging was enabled only at 14:34, so the historical
14:04 login attempt cannot be reconstructed from access records. During the
investigation, a separate SMTP cutover replaced the account container at
14:41:54 with release `20261005-smtp-prepared`. The new container retains
the request logging setting. Its stdout must not be mistaken for the old
container's historical logs.

The user reports at least four affected customers. Investigation therefore
focuses on the shared connection path rather than requiring one customer's
email. The current evidence does not establish the transport failure cause.

## Cloudflare edge analytics checked

The authenticated zone dashboard was inspected for POST requests received
between 2026-10-05 14:03 and 14:06 (dashboard timezone GMT+8).

- The dashboard reports 93 requests and presents 93 sampled-log rows. All ten
  pages were inspected. It labels the underlying dataset as adaptive sampling,
  so this is not a guarantee of complete raw packet or request capture.
- 91 rows target `mu-api.claude-api.cn`: 82 `/api/v1/account/authorize`, eight
  `/api/v1/auth/refresh`, and one `/api/v1/auth/register` at 14:03:03.
- No `/api/v1/auth/login` row appears in that displayed dataset.
- The other two rows target the separate mail service. The dashboard groups
  91 requests as served by origin and two as served by Cloudflare, with no
  mitigated requests in this window. Served by origin does not prove a 200
  response or delivery of a response to the client.
- Edge refresh rows at 14:04:04 and 14:04:36 match the aggregate database
  issuance timestamps. They are not password-login attempts.
- The separate Events view shows 14 sampled blocked records in the last
  24 hours, all targeting website or mail hostnames. None targets the account
  API; the newest displayed blocked record is at 07:32:01 on October 5.
  No account WAF block or challenge is established by these records.
- The free dashboard's expanded request detail does not expose HTTP response
  status or TLS error details. Requests failing before Cloudflare receives
  HTTP headers cannot be reconstructed from these HTTP analytics.

The screenshot text maps specifically to `HttpRequestException` in
`src/MainForm.Access.cs`; a wrong-password response follows a different error
path. The client uses one hostname and the Windows system proxy with a
12-second timeout. DNS resolution, connection establishment, TLS negotiation,
proxy transport, and an interrupted response remain possible causes, rather
than confirmed explanations. No client binary fix or connectivity resolution
has been verified, and no customer-side diagnostic operation is required.

## Identified customer follow-up

The user supplied a specific 163.com account (redacted here as
`130***@163.com`). A read-only live database query establishes:

- Registered at 2026-10-05 12:57:11, with confirmed email and a password set.
- `DisabledAt` and `LockoutEnd` are null; `AccessFailedCount` is zero.
- One session family was issued at 12:57:12 and rotated at 13:11:45 and
  13:26:28. No subsequent session issuance exists at inspection.
- The latest session is not revoked; its access token expires at 13:41:28
  and its refresh token at 2026-11-04 12:57:12. Expiry of the access token
  alone does not invalidate this account or explain failure of password login.

Edge records associate one source address with the registration at 12:57:11,
the verification-code request at 12:56:27, and both database refresh timestamps
at 13:11:45 and 13:26:28. This timestamp correlation is strong but source IP
is not an authenticated user identifier and can change or be shared.

Filtering that address between 12:54 and 14:15 shows the last displayed HTTP
request at 13:33:28, an `/api/v1/account/me` heartbeat. The POST-only view
shows registration, refresh, authorization and website events; no password
login is displayed. No request from that address is displayed around 14:04.
The datasets remain adaptively sampled, and a later address change cannot be
excluded. Neither the account state nor the displayed records establish a
wrong-password or disabled-account cause. The failed transport stage remains
unresolved; these findings do not establish recovery of the affected client.

## Login form flashes and clears entered credentials

The user subsequently reports that the login page flashes and requires the
credentials to be entered again. `RunAccountActionAsync` calls `RenderAccount`
in its `finally` block on both success and failure. The old renderer disposes
every account-page control and creates fresh empty textboxes. Startup session
restoration, account navigation and heartbeat completion also call the renderer.
This is a confirmed source-level cause of clearing input after a failed request,
not evidence of an application process crash.

The renderer now keeps the existing input form when its mode and signed-in
state are unchanged and updates only the feedback label. Login, registration,
reset-password and password-change failures preserve the current controls and
entered values. A mode or signed-in-state transition still builds a new form,
so successful authentication and switching forms do not reuse old password
fields. No password persistence has been added.

Validation: `dotnet build tests-ui/Ui.Compile.csproj -c Release --no-restore`
succeeded with zero warnings and zero errors on the macOS host. Windows UI
execution and delivery of an updated client binary have not been performed.
Windows acceptance should verify failed login retains the email/password and
shows the error, a feedback refresh while typing preserves focus/selection,
switching login/register clears the previous form, and successful login
transitions to the signed-in view.

## Heartbeat scope and transient connection behavior

The user requested that the account screen not run global periodic checks.
The timer is now started only on business pages with an existing session;
entering account page 6 stops the timer and cancels an in-flight heartbeat.
The heartbeat handler also checks the page before sending and suppresses
feedback from canceled/late results. Startup restoration and explicit login,
account refresh or reconnection remain available; these are not timer checks.

A separately verified account view now has a two-minute grace window starting
at the first transport failure, timeout or temporary 5xx/408/429 response.
Repeated failures do not extend this window. The underlying `IsOnline` remains
false; every actual operation still requires a successful server authorization.
One heartbeat failure therefore does not clear displayed results, cancel the
active operation, or force the account page. Grace expiry restricts the view
while retaining the refresh token for explicit recovery. Because the account
page has no automatic heartbeat, recovery there uses its reconnect button.
Revocation, account disablement and malformed account responses end grace
immediately. Saved credentials without a prior verified account grant no grace.

Verification: 64 portable account assertions passed, including the exact grace
boundary, non-extension on repeated errors, operation authorization during
grace, recovery, revocation, disabled accounts, malformed responses and caller
cancellation. Windows UI compilation passed. Windows execution of timer/page
switch behavior and release delivery remain unverified. On Windows, wait more
than 60 seconds on login/register/reset forms and verify no periodic
`account/me` request; switch to a business page and verify checks resume, then
switch back during a slow request and verify input and feedback are undisturbed.

## Live recheck: 2026-10-05 15:23-15:26

- The active release is now `20261005-mail-monitor`; the account container
  started at 15:04:04, with restart count zero and no OOM flag. This recheck
  did not deploy or restart it.
- From 15:04 through approximately 15:23:53, 799 completed origin requests
  were logged: 795 HTTP 200, one 302, and three 401. Maximum origin duration
  was 916.175 ms. One 401 is this investigation's nonexistent-account login
  probe; the other two are a refresh at 15:09:25 and account/me at 15:18:56.
  No 5xx or 429 appeared in that sample. Before the probe, no password-login
  completion appeared in the sample; this does not show where failed customer
  connections stopped.
- A nonexistent `example.invalid` account received the expected
  `401 invalid_credentials` response through public HTTPS from this Mac in
  1.898 seconds, from the VPS in 0.392 seconds, and through the loopback origin
  with the correct Host/protocol headers in 0.008 seconds. These probes verify
  login-route reachability and rejection, not successful password login or
  affected-customer connectivity.
- Current resource sampling showed about 205 MiB of the 512 MiB account
  container limit. Privileged tunnel journal access was unavailable in this
  SSH session; no claim about current tunnel health is based on its empty
  unprivileged journal view.

### A separate observed session revocation

A read-only database query found one session revoked without rotation since
15:04, exactly at the refresh-401 timestamp 15:09:25. Its family had:

1. Initial issuance at 14:39:06 and rotation at 14:53:56.
2. Another rotation at 15:08:54; the matching refresh request returned HTTP
   200 at the origin in 9.0913 ms.
3. The newly issued session revoked at 15:09:25, before its 15:23:54 access
   expiry and November 4 refresh expiry.

The checked-in `AccountService.RefreshAsync` revokes a family when an already
rotated/revoked refresh token is submitted. The timeline is consistent with
that branch, including a lost refresh response followed by reuse of the old
token, but the submitted token and initiating client are not captured. It
does not prove a lost response, concurrent client, or attribution to the
reported customer. Origin HTTP 200 does not prove receipt by the client.

The current client has a 12-second HTTP timeout and 30-second heartbeat.
A transport failure sets IsOnline=false and returns the UI to the account
page while retaining the session. A refresh 401 instead clears local session
state. These explain two different paths to the reported account screen;
neither establishes the cause of repeated password-login transport timeouts.
No code fix, connectivity recovery, or Windows-client acceptance was performed
by this recheck.
