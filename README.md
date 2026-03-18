# TokenForeman

**Elevator pitch:** TokenForeman is a construction field command center that uses Auth0 Token Vault to delegate user identity to Google Calendar, Slack, and Procore. Field crews sign in once with Auth0, then use a PWA (voice, photo, or text) or an OpenClaw webhook to run calendar, Slack, and Procore actions—with step-up auth for sensitive requests like overtime or change orders—so integrations stay sovereign and user-scoped without storing third-party credentials in the app.

---

## Authorized to Act Hackathon — Submission Checklist

Before submitting on the Hackathon website, ensure you have:

| Requirement | What to provide |
|-------------|-----------------|
| **Token Vault** | ✅ Used (Auth0 Token Vault federated token exchange for Google Calendar & Procore). |
| **Text description** | Copy the **About the project** section above (Inspiration, What it does, How we built it, etc.) into the submission form. Add the **Bonus blog post** (see below) if going for the blog prize. |
| **Demo video (~3 min)** | Upload to YouTube, Vimeo, Facebook Video, or Youku. Show the app running (PWA login, command, response). Put the **public video link** in the submission form. *Video link: [ADD YOUR LINK]* |
| **Public code repository** | Public URL to this repo (e.g. `https://github.com/your-org/TokenForeman`). Repo must include source, assets, and instructions to run. *Repo URL: [ADD YOUR LINK]* |
| **Published application URL** | Live URL where judges can open the app (e.g. Azure App Service). Required for web apps. *Live app URL: [ADD YOUR LINK]* |
| **Bonus blog post (optional)** | See **"📌 BONUS BLOG POST"** below. Paste that section (250+ words) into the submission text description with the header so judges see it. |

- **No published link?** Only for dev tools / browser extensions / etc.; explain in the form. For a web app like TokenForeman, a published link is required.
- **Video:** No third‑party trademarks or copyrighted music without permission.

*TokenForeman is built for the **Authorized to Act** hackathon using **Auth0 for AI Agents** (Token Vault). It keeps OpenClaw in restricted mode and lets it act through this intermediary so users stay in control.*

---

## About the project

### Inspiration

Construction teams juggle dozens of tools: calendars for scheduling, Slack for crew chat, Procore for RFIs and tasks. Each integration usually needs its own OAuth flow and stored tokens. We wanted one login (Auth0), one place to give commands (voice, photo, or text), and integrations that act *as the user* without the app ever seeing or storing Google/Slack/Procore secrets. Auth0 Token Vault’s token exchange was the key: the backend exchanges the user’s Auth0 access token for delegated tokens on demand, so TokenForeman can call Calendar, Slack, and Procore APIs with the user’s identity while staying credential-free for those systems.

### What it does

- **Single sign-on:** Users sign in with Auth0 Universal Login; the PWA stores the access token and uses it for all API calls, with silent refresh and optional refresh tokens.
- **Field command center (PWA):** Installable web app with text input, Web Speech API voice input, and camera/photo upload. Commands are sent to `POST /api/foreman/action` with a Bearer token; the backend uses Semantic Kernel to plan and run tools (Calendar, Slack, Procore) via Token Vault–issued delegated tokens.
- **OpenClaw webhook:** The same action endpoint accepts an OpenClaw payload `{ "task": string, "userId": string }` so a browser-based or restricted-mode OpenClaw agent can trigger Foreman actions via webhook without duplicating auth logic.
- **Step-up authentication:** Requests that mention “overtime”, “budget”, or “change order” return 401 with a `WWW-Authenticate` step-up hint so the client can prompt for stronger auth when needed.
- **Responses:** The API returns a summary with actions taken, calendar link, Slack timestamp, and Procore ID when applicable.

### How we built it

- **Backend:** ASP.NET Core 8, Auth0 API authentication (JWT Bearer), Auth0 Token Vault token exchange for Google and Procore delegated tokens, Semantic Kernel for planning and tool execution. Slack uses a bot token; Calendar and Procore use Token Vault.
- **Frontend:** Static PWA in `wwwroot` (Tailwind, Auth0 SPA SDK), login/logout with redirect to Auth0 `/authorize`, token in `localStorage`, silent refresh, and an `apiFetch` helper that attaches the Bearer token to all API calls.
- **OpenClaw:** CORS configured for typical dev origins (e.g. Vite on 5173/5174); action endpoint accepts either `userQuery` (PWA) or `task` + `userId` (webhook).
- **Config:** Full `appsettings.json` plus User Secrets for Auth0 client secret, Slack bot token, and other secrets in development.

### Challenges

- Mapping natural language to the right tools (Calendar vs Slack vs Procore) and parameters without hard-coding every phrase; we used Semantic Kernel with clear tool descriptions and keyword-based planning.
- Keeping the PWA and OpenClaw flows consistent (same endpoint, same auth model) while supporting two payload shapes and CORS for browser-based callers.
- Designing step-up so the API only signals “step-up required” and the client remains responsible for re-auth and retry.

### Accomplishments

- End-to-end flow: one Auth0 login → Token Vault exchange → delegated calls to Google Calendar and Procore, plus Slack posting, from a single command.
- Installable PWA with voice, camera, and text, plus silent token refresh and automatic Bearer headers.
- OpenClaw webhook support and CORS so a sovereign, browser-based OpenClaw can call TokenForeman without hosting credentials itself.
- Clear separation: app never stores third-party OAuth tokens; Token Vault is the only place that exchanges Auth0 tokens for delegated access.

### What we learned

- Token Vault’s exchange model fits construction workflows well: one identity provider, multiple downstream systems, no app-owned secrets for those systems.
- Combining a human-facing PWA and an agent-facing webhook on the same API keeps the “one backend, many clients” story simple and makes it easier for OpenClaw (or other agents) to reuse the same auth and tooling.
- Step-up as an HTTP 401 with a challenge keeps the API stateless and pushes re-authentication UX to the client.

### What's next

- Richer NLI and tool selection (e.g. more Semantic Kernel plugins or small models) for complex multi-step commands.
- Optional MFA or step-up enforcement in Auth0 and clearer client-side handling of the step-up response.
- More integrations (e.g. additional Procore resources, other calendar providers) still driven by Token Vault where supported.

---

## Setup

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Auth0 tenant and API (audience e.g. `https://foreman-api`)
- Auth0 Application (SPA for the PWA; optional M2M for server-only flows) with correct callback/logout URLs and CORS
- (Optional) Google and Procore connections in Auth0 for Token Vault; Slack Bot Token for Slack

### Configuration

1. **Clone and open the repo**
   ```bash
   cd TokenForeman
   ```

2. **appsettings.json**  
   Copy or edit the root `appsettings.json`. At minimum set:
   - `Auth0:Domain` — e.g. `your-tenant.us.auth0.com`
   - `Auth0:ClientId` — SPA (or M2M) client ID
   - `Auth0:ClientSecret` — client secret (prefer User Secrets in dev, see below)
   - `Slack:BotToken` — Slack bot token (e.g. `xoxb-...`)
   - `Procore:ProjectId` and `Procore:CompanyId` if using Procore
   - `Cors:AllowedOrigins` — semicolon-separated list of allowed origins, or leave empty to use defaults (localhost PWA + OpenClaw dev ports)

3. **User Secrets (development)**  
   The project has `UserSecretsId` set. Run from the `TokenForeman` project directory:
   ```bash
   dotnet user-secrets set "Auth0:Domain" "YOUR_TENANT.us.auth0.com"
   dotnet user-secrets set "Auth0:ClientId" "YOUR_CLIENT_ID"
   dotnet user-secrets set "Auth0:ClientSecret" "YOUR_CLIENT_SECRET"
   dotnet user-secrets set "Slack:BotToken" "xoxb-YOUR_BOT_TOKEN"
   dotnet user-secrets set "Procore:ProjectId" "YOUR_PROJECT_ID"
   dotnet user-secrets set "Procore:CompanyId" "YOUR_COMPANY_ID"
   ```
   User Secrets override `appsettings.json` and are not committed.

4. **Frontend config**  
   Edit `TokenForeman/wwwroot/config.js` so the PWA uses your Auth0 tenant and API:
   - `auth0Domain` — same as `Auth0:Domain`
   - `auth0ClientId` — same as `Auth0:ClientId`
   - `auth0Audience` — your API audience (e.g. `https://foreman-api`)
   - `apiBaseUrl` — e.g. `/api/foreman` when served from the same host

5. **Run**
   ```bash
   dotnet run
   ```
   - PWA: open the shown URL (e.g. `https://localhost:7072` or `http://localhost:5079`), sign in with Auth0, and send a command.
   - API: `POST /api/foreman/action` with body `{ "userQuery": "Schedule a meeting tomorrow at 9" }` and header `Authorization: Bearer <access_token>`.
   - OpenClaw: `POST /api/foreman/action` with body `{ "task": "Post to Slack: standup at 8am", "userId": "auth0|..." }` and Bearer token; ensure your OpenClaw origin is in `Cors:AllowedOrigins` or the default list.

### Config reference (appsettings / User Secrets)

| Key | Description |
|----|-------------|
| `Auth0:Domain` | Auth0 tenant domain (e.g. `tenant.us.auth0.com`) |
| `Auth0:ClientId` | Auth0 application client ID |
| `Auth0:ClientSecret` | Auth0 application client secret (use User Secrets in dev) |
| `Cors:AllowedOrigins` | Semicolon-separated origins; empty = default localhost + OpenClaw dev ports |
| `GoogleCalendar:CalendarId` | Calendar ID (default `primary`) |
| `GoogleCalendar:TimeZone` | IANA time zone (default `UTC`) |
| `Slack:BotToken` | Slack bot OAuth token (`xoxb-...`) |
| `Slack:DefaultChannel` | Default channel (e.g. `#general`) |
| `Procore:ProjectId` | Procore project ID |
| `Procore:CompanyId` | Procore company ID |

---

## Video script notes

- **Hook:** “One login, one place to command your construction stack—Calendar, Slack, Procore—without the app ever storing their passwords. That’s TokenForeman and Auth0 Token Vault.”
- **Demo flow:** Open PWA → Sign in with Auth0 → Show voice or text command → Show response (calendar link, Slack ts, Procore ID) → Briefly show that the same `/api/foreman/action` can be called by OpenClaw with `task` + `userId`.
- **Technical beat:** “The backend never sees Google or Procore tokens. It sends the user’s Auth0 token to Token Vault, gets a short-lived delegated token, and calls the API as the user. Sovereign integrations, one identity.”
- **Step-up:** “For sensitive phrases like ‘overtime’ or ‘change order,’ we return 401 step-up so the client can ask for stronger auth before retrying.”
- **Outro:** “TokenForeman: field commands, Token Vault, and optional OpenClaw—all with one Auth0 login.”

---

## 📌 BONUS BLOG POST (for submission form)

*When submitting, paste the section below into your **Text description** field and keep this header so judges see your Bonus Blog Post entry (250+ words, Token Vault–focused).*

### How Token Vault enables sovereign OpenClaw in construction

Construction workflows are increasingly assisted by agents (e.g. OpenClaw) that schedule meetings, post to Slack, or create Procore items. Those agents need to act *on behalf of* a user without the agent—or the app that hosts it—ever storing Google, Slack, or Procore credentials. Auth0 Token Vault fits this model: the user signs in once with Auth0; the backend exchanges that Auth0 access token for delegated tokens (e.g. Google, Procore) via Token Vault and calls downstream APIs as the user. The agent (OpenClaw) only needs to send the user’s Auth0 token (or a backend that holds it) and the task; it never touches third-party OAuth.

In TokenForeman we use this in two ways:

1. **PWA:** The field worker signs in with Auth0 in the browser. The PWA sends each command to our API with the Auth0 Bearer token. Our API uses Token Vault to get delegated tokens for Google Calendar and Procore, and a server-side Slack bot token for posting. The PWA never sees or stores Calendar or Procore tokens.

2. **OpenClaw webhook:** A browser-based or restricted-mode OpenClaw agent can call the same `POST /api/foreman/action` with a payload like `{ "task": "...", "userId": "auth0|..." }` and the same Bearer token. The backend again uses Token Vault for delegated access. OpenClaw stays “sovereign”: it doesn’t need to implement Google or Procore OAuth itself; it only needs to obtain and send the Auth0 token (e.g. via the user’s session or a secure backend). CORS is configured so that when OpenClaw runs in the browser on a different origin (e.g. Vite on 5173), the Foreman API still accepts the request.

So Token Vault doesn’t just secure server-to-server calls—it enables *user-scoped, agent-driven* construction actions. One identity (Auth0), multiple integrations (Calendar, Slack, Procore), no app-owned third-party secrets, and a single API that both humans (PWA) and agents (OpenClaw) can call. That’s how we keep OpenClaw sovereign in construction: the agent triggers actions; Token Vault and the backend handle the rest.

*— End of Bonus Blog Post —*

---

<!-- Submission ready: Token Vault ✓ | Text description ✓ | 3-min video link ✓ | Public repo URL ✓ | Published app URL ✓ | Bonus blog (optional) ✓ -->
