using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;

namespace AUBDigitalAcademy.Data;

public sealed record TestDatabaseGuardResult(
    bool IsValid,
    string? DataSource,
    string? DatabaseName,
    string DiagnosticMessage);

public static class TestDatabaseGuard
{
    public const string RequiredTestDatabaseName = "AUBDigitalAcademy_Test";
    public const string ForbiddenDevelopmentDatabaseName = "AUBDigitalAcademy";

    /// <summary>
    /// Validates that test CLI execution targets only the isolated AUBDigitalAcademy_Test database
    /// in a non-Production environment and verifies the live SQL Server database identity via SELECT DB_NAME().
    /// </summary>
    public static TestDatabaseGuardResult EnsureSafeTestConnection(
        string? connectionString,
        IHostEnvironment environment)
    {
        if (environment.IsProduction())
        {
            return new TestDatabaseGuardResult(
                false,
                null,
                null,
                "Test CLI execution is blocked in the Production environment.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new TestDatabaseGuardResult(
                false,
                null,
                null,
                "Missing test database connection string. Configure 'ConnectionStrings:TestConnection' or 'TEST_DB_CONNECTION'. Fallback to DefaultConnection is disabled.");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            return new TestDatabaseGuardResult(
                false,
                null,
                null,
                $"Malformed test database connection string ({ex.GetType().Name}).");
        }

        var dataSource = string.IsNullOrWhiteSpace(builder.DataSource) ? "<unspecified>" : builder.DataSource;
        var initialCatalog = builder.InitialCatalog?.Trim() ?? string.Empty;

        if (string.Equals(initialCatalog, ForbiddenDevelopmentDatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            return new TestDatabaseGuardResult(
                false,
                dataSource,
                initialCatalog,
                $"Unsafe test database target '{ForbiddenDevelopmentDatabaseName}' rejected. Development database cannot be used for test execution.");
        }

        if (!string.Equals(initialCatalog, RequiredTestDatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            var reportedDb = string.IsNullOrEmpty(initialCatalog) ? "<unspecified>" : initialCatalog;
            return new TestDatabaseGuardResult(
                false,
                dataSource,
                reportedDb,
                $"Test database must be '{RequiredTestDatabaseName}', but connection string specified '{reportedDb}'.");
        }

        try
        {
            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DB_NAME()";
            var actualDatabase = (command.ExecuteScalar() as string)?.Trim() ?? string.Empty;

            if (string.Equals(actualDatabase, ForbiddenDevelopmentDatabaseName, StringComparison.OrdinalIgnoreCase))
            {
                return new TestDatabaseGuardResult(
                    false,
                    dataSource,
                    actualDatabase,
                    $"Live SQL Server check connected to '{ForbiddenDevelopmentDatabaseName}'. Aborting to protect Development data.");
            }

            if (!string.Equals(actualDatabase, RequiredTestDatabaseName, StringComparison.OrdinalIgnoreCase))
            {
                var reportedActual = string.IsNullOrEmpty(actualDatabase) ? "<empty>" : actualDatabase;
                return new TestDatabaseGuardResult(
                    false,
                    dataSource,
                    reportedActual,
                    $"Live SQL Server check returned '{reportedActual}' instead of '{RequiredTestDatabaseName}'.");
            }

            return new TestDatabaseGuardResult(
                true,
                dataSource,
                actualDatabase,
                $"Verified isolated test database '{actualDatabase}' on '{dataSource}'.");
        }
        catch (Exception ex)
        {
            return new TestDatabaseGuardResult(
                false,
                dataSource,
                initialCatalog,
                $"Failed to verify live SQL Server database identity on '{dataSource}' for '{initialCatalog}': {ex.GetType().Name} - {ex.Message}");
        }
    }
}
