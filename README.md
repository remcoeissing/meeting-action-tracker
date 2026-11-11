# Meeting Action Tracker

A local committee workspace built from [the project brief](projectbrief.md).
Prepare agendas and reading documents, capture meeting summaries, and track follow-up actions with owners and due dates.

## Stack

- .NET 10 REST API and EF Core SQLite
- React, TypeScript, and Vite
- API-first OpenAPI contract with generated frontend types
- SQLite and private local document storage; no cloud services required

## Prerequisites

- A .NET 10+ host installation for `global.json` SDK discovery
- .NET SDK **10.0.404** (or a later patch in the same feature band)
- Node.js **22.12+** and npm (Node 26 was used during development)

The project prefers the ignored `.dotnet` directory, then the system SDK. If the required SDK is missing, install it locally from the repository root in PowerShell:

```powershell
$installer = Join-Path $env:TEMP "dotnet-install-$([guid]::NewGuid()).ps1"
try {
    Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $installer
    & $installer -Version 10.0.404 -InstallDir .dotnet -NoPath
} finally {
    if (Test-Path -LiteralPath $installer) {
        Remove-Item -LiteralPath $installer -Force
    }
}
dotnet --version
```

This leaves shared/system SDK installations unchanged. Use `.\.dotnet\dotnet.exe` instead of `dotnet` if your installed host is older than .NET 10 and cannot resolve the `paths` setting.

## Run locally

From the repository root, start the backend:

```powershell
dotnet run --project src\MeetingActionTracker.Api
```

In another terminal:

```powershell
Set-Location src\meeting-action-tracker-web
npm ci
npm run dev
```

Open **http://127.0.0.1:5173**. The API listens at **http://127.0.0.1:5080**.
Vite proxies `/api` requests to the backend. Both processes must be running; do not open the HTML file directly.
Ports are fixed to prevent accidentally connecting to a different local service.

The database is created and migrations are applied on API startup. A new workspace starts empty:

1. Create a committee.
2. Create members and add them to the committee.
3. Schedule a meeting.
4. Add and order agenda items; upload preparation documents.
5. Write the meeting summary.
6. Create follow-up actions, assign committee members, and set due dates.
7. Use the dashboard to filter actions; open a meeting to update its actions.

## API-first development

The authoritative contract is [api/openapi.yaml](api/openapi.yaml), served at
**http://127.0.0.1:5080/api/openapi.yaml**. Resource endpoints use `/api/v1`.

Change the contract before changing backend DTOs/endpoints or dependent UI workflows.
Regenerate frontend types after contract edits:

```powershell
Set-Location src\meeting-action-tracker-web
npm run api:generate
```

The generated [schema types](src/meeting-action-tracker-web/src/api/schema.d.ts) are checked in.
Production frontend builds regenerate them automatically.
Keep backend validation and DTOs aligned with the contract; backend endpoints are handwritten.
Failures use HTTP status codes and Problem Details rather than success-shaped error responses.

## Build and validation

```powershell
# Repository root
dotnet build MeetingActionTracker.slnx

# Frontend
Set-Location src\meeting-action-tracker-web
npm run typecheck
npm run lint
npm run build
```

`npm run preview` serves the production frontend build on port 5173 with the same API proxy; stop the development frontend first.

There are deliberately **no automated test cases or test runners** in this release.
For manual verification, exercise the workflow above and check:

- Editing committee/member names, meeting details, agendas, summaries, and actions persists.
- Agenda move-up/down controls maintain order, including after a restart.
- Documents download with their original filename and identical contents.
- Exactly 20 MiB is accepted; larger files and unsupported/mismatched content are rejected.
- Non-member assignments, invalid dates/statuses, and blank names are rejected with readable errors.
- Actions due today are not overdue; completed actions are never overdue.
- Dashboard committee, assignee, and status filters work.
- Referenced members/memberships cannot be removed; committees and meetings have no delete operation.
- Removing an agenda item with documents is blocked until the documents are removed.
- Stopping the API produces a visible error; restart it and use Retry connection.
- Data and documents survive API restarts; check desktop/mobile and light/dark appearance.

## Persistence and migrations

By default, the API stores `tracker.db` and `documents` inside
`src\MeetingActionTracker.Api\data`. Data and uploads are ignored by Git.
Override the location with an absolute path, for example:

```powershell
$env:DataDirectory = 'C:\local-data\meeting-action-tracker'
dotnet run --project src\MeetingActionTracker.Api
```

Use an otherwise empty directory for a separate workspace.
Back up the **entire data directory together while the API is stopped**; copying only the database loses document contents.
Do not delete the data directory unless you intend to permanently remove that workspace.

EF tooling is repository-local:

```powershell
dotnet tool restore
dotnet ef migrations add YourMigrationName --project src\MeetingActionTracker.Api
```

Migrations are checked in. Do not replace migration-based initialization with `EnsureCreated`.

## Workflow rules and limitations

- Statuses: **Open**, **In progress**, **Done**.
- Every action has one meeting, one committee-member assignee, and a required date-only due date.
- An action is overdue when its due date is before the browser's local calendar date and it is not Done.
- The dashboard refreshes its calendar date automatically; `/actions` queries require a `today=YYYY-MM-DD` parameter for consistent classification.
- Meeting timestamps are stored in UTC and displayed/edited in the browser's timezone.
- Committee and meeting deletion is not exposed. Membership removal is blocked if any action in that committee references the member, including completed actions.
- Documents: PDF, DOCX, XLSX, PPTX, and UTF-8 TXT, maximum **20 MiB / 20,971,520 bytes** each. Extension and contents are checked. OpenXML files must contain their expected main part and matching content type; uploads are not extracted.
- Documents are stored under generated names outside the web root and downloaded as attachments.
- Appearance follows your system by default. The appearance button stores the selection in the URL's `scoutTheme` parameter.
- Summaries are plain text and manually written.

**Local prototype only:** there is no authentication or authorization. The API permits loopback connections only, validates local request origins, and does not enable cross-origin access. Do not expose it through a network listener, tunnel, or reverse proxy for shared use.

There is no malware scanning, multi-user concurrency/version-conflict UX, automatic backup/retention, notifications, calendar integration, AI summarization, or cloud deployment.
Document validation is not a substitute for malware scanning. Use trusted preparation documents only.
Filesystem and database updates use compensating operations but are not one crash-atomic transaction; after an unexpected interruption, check the API logs and recover documents from a complete backup if needed.
