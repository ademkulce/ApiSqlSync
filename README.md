# ApiSqlSync

[![Build and Test](https://github.com/ademkulce/ApiSqlSync/actions/workflows/ci.yml/badge.svg)](https://github.com/ademkulce/ApiSqlSync/actions/workflows/ci.yml)

Incremental synchronization from a PostgREST-compatible API to SQL Server,
with persistent checkpoints, execution history, a background worker,
and an ASP.NET Core dashboard.

## Features

- Incremental pagination using an `(updated_at, id)` cursor.
- Insert and update records stored as JSON in SQL Server.
- Commit each page and its checkpoint in the same transaction.
- Resume from the last committed checkpoint.
- Track successful, failed, and cancelled synchronization runs.
- Run a single synchronization through the CLI.
- Run periodically through the background worker.
- View job summaries and execution history in the web dashboard.
- Initialize the database using embedded, tracked SQL scripts.
- Try the application using the built-in development demo API.

## Projects

| Project | Responsibility |
| --- | --- |
| ApiSqlSync.Core | Contracts, cursor models, synchronization engine and run tracking |
| ApiSqlSync.PostgRest | HTTP requests, cursor queries and JSON parsing |
| ApiSqlSync.SqlServer | Persistence, dashboard queries and database initialization |
| ApiSqlSync.Cli | Database initialization and single synchronization runs |
| ApiSqlSync.Worker | Periodic synchronization |
| ApiSqlSync.Web | Dashboard and development demo API |

Source projects are under `src/`; test projects are under `tests/`.

## Requirements

- .NET 10 SDK.
- SQL Server with JSON support, or SQL Server Express LocalDB on Windows.
- Visual Studio with .NET 10 support, or another compatible editor.

The example configuration uses Windows LocalDB.
For another SQL Server instance, update the connection strings.

## Quick start on Windows

Run the following commands in PowerShell from the repository root.

### 1. Create local configuration files

```powershell
Copy-Item src/ApiSqlSync.Cli/appsettings.Local.example.json src/ApiSqlSync.Cli/appsettings.Local.json
Copy-Item src/ApiSqlSync.Worker/appsettings.Local.example.json src/ApiSqlSync.Worker/appsettings.Local.json
Copy-Item src/ApiSqlSync.Web/appsettings.Local.example.json src/ApiSqlSync.Web/appsettings.Local.json
```

Check the copied files:

- Use the same SQL Server database in all three projects.
- For the demo, set the CLI and Worker API base URL to
  `https://localhost:7190/demo-api/`.
- Set the API resource to `records`.
- The demo API does not require a token.

Local configuration files are excluded from Git.

### 2. Restore and build

```powershell
dotnet restore ApiSqlSync.slnx
dotnet build ApiSqlSync.slnx
```

### 3. Initialize the database

```powershell
dotnet run --project src/ApiSqlSync.Cli -- --init-db
```

This creates the configured database if it does not exist and applies
pending schema scripts.

Running the command again skips scripts already recorded in
`dbo.SchemaMigrations`.

The database account needs database creation permission when the
configured database does not exist.

### 4. Start the web application

Trust the development HTTPS certificate:

```powershell
dotnet dev-certs https --trust
```

Start the application in Development mode:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/ApiSqlSync.Web --no-launch-profile -- --urls "https://localhost:7190"
```

The environment setting above applies to the current PowerShell session.

Keep this terminal open.

- Dashboard: https://localhost:7190/SyncJobs
- Run history: https://localhost:7190/SyncRuns
- Demo API: https://localhost:7190/demo-api/records

The demo API is available only in Development mode.

### 5. Run one synchronization

Open a second terminal at the repository root:

```powershell
dotnet run --project src/ApiSqlSync.Cli
```

Running it again without newer source records should process zero records.

### 6. Run periodic synchronization

After the CLI run finishes, start the Worker:

```powershell
dotnet run --project src/ApiSqlSync.Worker
```

The Worker runs immediately, then waits for `Sync:IntervalSeconds`
after each completed or failed round.

Press Ctrl+C to stop it.

## Configuration

Configuration is loaded in this order:

1. Default application configuration.
2. Optional `appsettings.Local.json`.
3. Environment variables prefixed with `APISQLSYNC_`.

Later values override earlier values.

The CLI and Worker use these settings:

| Setting | Purpose |
| --- | --- |
| Sync:JobId | Identifier used for records, checkpoints and run history |
| Sync:PageSize | Maximum number of records requested per page |
| PostgRest:BaseUrl | API base address |
| PostgRest:Resource | Resource appended to the base address |
| PostgRest:TimestampField | Numeric timestamp field |
| PostgRest:IdField | Numeric record ID field |
| PostgRest:Token | Optional bearer token |
| SqlServer:ConnectionString | Target SQL Server database |
| SqlServer:CommandTimeoutSeconds | SQL command timeout |

The Worker also uses `Sync:IntervalSeconds`.
The dashboard requires the SQL Server settings.

Do not commit real credentials or tokens in example configuration files.

## Source API expectations

The API must support the ordering and filtering used by PostgREST:

- Ascending order by the configured timestamp and ID fields.
- Filtering records after an `(updated_at, id)` cursor.
- A page limit.
- A JSON array response.
- Integer values fitting into a signed 64-bit integer for timestamps and IDs.

Record updates must move their cursor beyond the saved checkpoint to be
picked up by a later synchronization.

## Database schema

| Table | Purpose |
| --- | --- |
| dbo.SyncRecords | Latest stored JSON payload for each job and record ID |
| dbo.SyncCheckpoints | Last committed cursor for each job |
| dbo.SyncRuns | Synchronization execution history |
| dbo.SchemaMigrations | Applied script names, hashes and timestamps |

Each page and its checkpoint are committed atomically.
Execution history is recorded separately from page transactions.

Applied migration scripts must not be edited.
Add a new numbered script and register it in
`SqlServerDatabaseInitializer` for future schema changes.

## Tests

Run tests that do not require SQL Server:

```powershell
dotnet test tests/ApiSqlSync.Core.Tests
dotnet test tests/ApiSqlSync.PostgRest.Tests
```

For integration tests, use a separate test database that has already
been initialized with the application schema:

```powershell
$env:APISQLSYNC_TEST_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Database=ApiSqlSync_Test;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
dotnet test tests/ApiSqlSync.IntegrationTests
```

The store test uses the configured database and cleans up its own records.
The initializer test creates and removes a separate temporary database
on the same server.

GitHub Actions builds the solution and runs the Core and PostgREST tests.
SQL integration tests currently run separately.

## Current scope

- Source deletions are not synchronized.
- Late updates whose cursor is behind the checkpoint are not revisited.
- SQL payloads are stored as JSON; automatic relational column mapping
  is not implemented.
- Do not run multiple synchronizers concurrently for the same job.
  A distributed job lease is not implemented.
- Reusing a JobId for a different source reuses its existing checkpoint.
  Use a different JobId when changing sources.
- The dashboard currently has no authentication.
  Use it locally until access control is added.
  
  ## License

Licensed under the [MIT License](LICENSE).
Third-party dependencies retain their respective licenses.