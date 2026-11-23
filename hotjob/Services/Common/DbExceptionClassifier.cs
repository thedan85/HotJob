using System.Data.Common;
using MySqlConnector;

namespace hotjob.Services.Common;

/// <summary>Provider-aware classification of database exceptions, so services do not guess from message text.</summary>
public static class DbExceptionClassifier
{
    /// <summary>
    /// True when the exception (or any inner exception) is a unique/primary-key violation.
    /// MySQL (production) is matched by error code 1062. SQLite (used by the test suite) has no
    /// provider-neutral code available here, so its stable "UNIQUE constraint failed" message is used.
    /// </summary>
    public static bool IsUniqueViolation(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlException mySql)
                return mySql.ErrorCode == MySqlErrorCode.DuplicateKeyEntry;

            if (current is DbException dbException &&
                dbException.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
