using Collaboard.Api.Hosting;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace Collaboard.Api.Tests;

public class DataSourceResolverTests
{
    private static string DataSourceOf(string connectionString) =>
        new SqliteConnectionStringBuilder(connectionString).DataSource;

    [Fact]
    public void Resolve_RelativeDataSource_RebasesOntoAppBaseDirectory()
    {
        var result = DataSourceResolver.Resolve("Data Source=./data/collaboard.db");

        var dataSource = DataSourceOf(result);
        Path.IsPathRooted(dataSource).ShouldBeTrue();
        dataSource.ShouldBe(Path.GetFullPath("./data/collaboard.db", AppContext.BaseDirectory));
    }

    [Fact]
    public void Resolve_RelativeDataSource_LanShapeIsByteIdenticalToCwdWhenCwdIsAppDir()
    {
        // The LAN constraint: operator `cd`s into the app folder and runs `./Collaboard.Api`,
        // so AppContext.BaseDirectory is the same folder the relative `./data` would have
        // resolved against via cwd. The resolved absolute path must equal what the old
        // cwd-relative behavior produced when cwd == app dir.
        var result = DataSourceResolver.Resolve("Data Source=./data/collaboard.db");

        var resolvedFromBaseDir = Path.GetFullPath("./data/collaboard.db", AppContext.BaseDirectory);
        DataSourceOf(result).ShouldBe(resolvedFromBaseDir);
    }

    [Fact]
    public void Resolve_AbsoluteDataSource_ReturnedUnchanged()
    {
        var absolute = OperatingSystem.IsWindows()
            ? @"C:\var\lib\collaboard\collaboard.db"
            : "/var/lib/collaboard/collaboard.db";

        var result = DataSourceResolver.Resolve($"Data Source={absolute}");

        DataSourceOf(result).ShouldBe(absolute);
    }

    [Fact]
    public void Resolve_InMemoryDataSource_ReturnedUnchanged()
    {
        const string connectionString = "Data Source=:memory:";

        var result = DataSourceResolver.Resolve(connectionString);

        DataSourceOf(result).ShouldBe(":memory:");
    }

    [Fact]
    public void Resolve_EmptyDataSource_ReturnedUnchanged()
    {
        var result = DataSourceResolver.Resolve("Data Source=");

        DataSourceOf(result).ShouldBe(string.Empty);
    }

    [Fact]
    public void Resolve_RelativeDataSource_PreservesOtherConnectionStringKeywords()
    {
        var result = DataSourceResolver.Resolve(
            "Data Source=./data/collaboard.db;Cache=Shared;Foreign Keys=True");

        var builder = new SqliteConnectionStringBuilder(result);
        Path.IsPathRooted(builder.DataSource).ShouldBeTrue();
        builder.Cache.ShouldBe(SqliteCacheMode.Shared);
        builder.ForeignKeys.ShouldBe(true);
    }

    [Fact]
    public void Resolve_RelativeDataSource_ResultIsCwdIndependent()
    {
        // The whole point: the resolved path must not change when the process cwd changes.
        var originalCwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = Path.GetTempPath();
            var fromTempCwd = DataSourceOf(
                DataSourceResolver.Resolve("Data Source=./data/collaboard.db"));

            Environment.CurrentDirectory = AppContext.BaseDirectory;
            var fromAppDirCwd = DataSourceOf(
                DataSourceResolver.Resolve("Data Source=./data/collaboard.db"));

            fromTempCwd.ShouldBe(fromAppDirCwd);
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
        }
    }
}
