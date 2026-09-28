# Compatibility public query proxy

The website serves `/compatibility` from `public/compatibility.html`. Browser requests use these same-origin, read-only endpoints:

- `GET /api/compatibility/games?q=<game>&gpu=<exact GPU name>`: shared compatibility search DTO, `{ items: [...] }`.
- `GET /api/compatibility/games/<id>?gpu=<exact GPU name>`: shared compatibility detail DTO.
- `GET /api/compatibility/gpus`: `{ items: ["GPU model", ...] }` from submitted records.

The page provides public browsing. Creating test reports stays in the authenticated Windows client. The proxy does not provide login, submission, upload, or other write operations. `HEAD` and all other methods return `405` with `Allow: GET`.

## Configuration

`COMPATIBILITY_API_BASE` defaults to `https://mu-api.claude-api.cn/api/v1/compatibility/public/`. It is a server-controlled HTTP(S) base URL without credentials, query parameters, or fragments. A trailing slash is added if omitted. Set it to the account service's public compatibility endpoint when using another deployment; no database credentials are needed by the website. Local tests use an isolated HTTP server and do not access the production database.

The service must expose the public routes and enable its independent `compatibility.public.read` feature. A disabled public feature returns `403`; this is shown as unavailable rather than as an empty database. Client authorization and submission controls remain independent.

The proxy never forwards browser Cookie, Authorization, Origin, forwarded IP, or other request headers. It sends only its own Accept and User-Agent headers and never follows upstream redirects. URL path segments come from the three supported routes, and query parameters are validated and encoded. Game search is at most 200 characters, GPU name 160, and game ID 64 ASCII letters/digits/underscore/hyphen. Duplicate, unknown, control-character, or overlong parameters are rejected.

The independent limiter permits 120 requests per minute per socket IP, including cache hits. It keeps at most 5,000 IP buckets, removes expired buckets opportunistically, and refuses new buckets when full instead of evicting active limits. It does not trust user-supplied proxy headers by default. Deployments whose network ingress **guarantees a genuine Cloudflare connecting IP header** may explicitly set `COMPATIBILITY_TRUST_CF_IP=1`; do not enable this on an endpoint accessible directly by untrusted clients. This option affects the local limiter only and never forwards the IP upstream. Without trusted ingress configuration, users sharing a reverse proxy connection may share the local rate limit.

Successful responses are cached internally for 15 seconds with at most 64 keys and 8 MiB of serialized JSON. Same-key requests share one upstream fetch; at most 32 distinct upstream requests can be pending. Errors are never cached. This reduces load from repeated public queries without spoofing client IP addresses to the upstream service. Browser responses use `Cache-Control: no-store`.

## Failure behavior

Each upstream fetch, including its streamed body, has an 8-second deadline and a 2 MiB size limit. JSON data is validated against the public DTO, counts/statuses are checked, and only known fields are returned. Extra account identifiers or backend fields are omitted. Testers must use the service's anonymous tester identifier format.

Errors have `{ code, message, retryAfterSeconds? }`:

- `400 invalid_compatibility_query`: invalid parameters.
- `403`: public reading disabled or forbidden; retain the service's structured code.
- `404`: game or route not found; retain `game_not_found` from the service.
- `405 compatibility_read_only`: use the client to submit a report.
- `429`: local or upstream rate limit, with bounded `Retry-After` (1 to 3,600 seconds for upstream errors).
- `502 compatibility_bad_response`: invalid JSON/content type/schema, oversized payload, or unexpected response.
- `503 compatibility_unavailable`: network failure, timeout, valid upstream server error, or pending-request capacity reached.

An unavailable or malformed upstream is never translated into `{ items: [] }` or zero test counts. Upstream error messages, cookies, and internal response headers are not exposed.

## Verification

Run `node --test website/tests/compatibility.test.mjs` with the existing Node 22 runtime. Tests inject fetch for failure/limit scenarios and also start two isolated local HTTP servers (the website and a fixture upstream) to verify routes, filters, headers, CSP, error statuses, redirects, and absence of anonymous writes. These tests do not validate a deployed public service or Windows client behavior.
