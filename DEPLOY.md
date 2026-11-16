# Putting Subfuse on the internet

This is the checklist for hosting. `HOW-TO-RUN.md` covers running it on your own computer.

## What the server needs

| Need | Why |
|---|---|
| A place to run a container | The `Dockerfile` in this folder builds the whole site. |
| A permanent disk mounted at `/data` | Holds the database (`subfuse.db`) and the encryption keys (`keys/`). |
| HTTPS in front of it | Sign-in cookies are only sent over HTTPS in production. |

The container listens on port **8080**. A health check is available at `/healthz`.

## Settings

Set these as environment variables on the host. The first two already have these values inside the container.

| Variable | Value | Meaning |
|---|---|---|
| `ConnectionStrings__Default` | `Data Source=/data/subfuse.db` | Where the database file lives. |
| `DataProtection__KeysPath` | `/data/keys` | Where the encryption keys live. |
| `Proxy__TrustForwardedHeaders` | `true` | Required behind Cloudflare or a platform's load balancer, so the site sees each visitor's real address. Without it the sign-in limiter treats all visitors as one. |
| `Proxy__ForwardLimit` | number of proxies in front (for example `2` for Cloudflare + the platform) | How many forwarded addresses to trust. Too high lets visitors fake their address; too low limits everyone together. |
| `RateLimits__AuthPerMinute` | `10` (default) | Sign-in and other sensitive attempts allowed per address per minute. |

Only turn on `Proxy__TrustForwardedHeaders` when the server cannot be reached directly from the internet, bypassing the proxy.

## Back up `/data`

- **`subfuse.db`** is every account and subscription.
- **`keys/`** encrypts sign-in cookies and authenticator secrets. If it is lost, everyone is signed out and
  authenticator codes stop working (recovery codes still work). Back it up together with the database, and
  keep the backup private: anyone holding both files can read the authenticator secrets.

## Before launch

1. Fill in the bracketed parts of `server/wwwroot/privacy.html` and `terms.html`, have them reviewed, and remove the "Draft" notice.
2. Turn on "Always use HTTPS" at the proxy or platform.
3. Create an account on the live site, turn on two-factor sign-in, sign out and back in.
4. Restore a backup once on a test copy, to know it works.

## Not built yet

- **Email**: password reset, email verification and renewal reminders all need an email-sending service.
- **Live currency rates**: totals use fixed sample rates.
- **A larger database**: SQLite on one disk suits a single server. Running several servers at once needs PostgreSQL.
