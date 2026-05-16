using Microsoft.Data.Sqlite;

namespace Collaboard.Api.Hosting;

// Resolves the SQLite connection string to a cwd-independent form. The default
// connection string carries a RELATIVE data source (`Data Source=./data/collaboard.db`),
// which the OS/SQLite resolves against the process working directory. Under hardened
// hosting the working directory is read-only (e.g. systemd `ProtectSystem=strict` with
// the app's cwd outside `ReadWritePaths`), so a relative `./data` resolves to a path the
// process cannot create — a managed IOException at startup, pre-serve (Collaboard #232).
//
// The anchor is `AppContext.BaseDirectory` (the directory containing the app binary), NOT
// `Environment.CurrentDirectory`. For the primary LAN shape the operator `cd`s into the
// app folder and runs `./Collaboard.Api`, so BaseDirectory == cwd == the app folder, and
// `./data/` resolves to exactly the same absolute path it does today — byte-identical. For
// the hosted shape the cwd differs from (and may be read-only relative to) the app folder;
// anchoring on BaseDirectory makes the data directory stable regardless of cwd.
//
// An explicit ABSOLUTE data source (operator or Collabhost setting
// `ConnectionStrings:Board` / env `COLLABOARD__ConnectionStrings__Board` to an absolute
// path) is an intentional override and is returned untouched.
internal static class DataSourceResolver
{
    public static string Resolve(string connectionString)
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = connectionStringBuilder.DataSource;

        // SQLite special data sources (`:memory:`, empty/temp) carry no filesystem path —
        // leave them untouched (the in-memory test database relies on this).
        if (string.IsNullOrEmpty(dataSource) || dataSource == ":memory:")
        {
            return connectionString;
        }

        if (Path.IsPathRooted(dataSource))
        {
            return connectionString;
        }

        connectionStringBuilder.DataSource = Path.GetFullPath(
            dataSource,
            AppContext.BaseDirectory);

        return connectionStringBuilder.ToString();
    }
}
