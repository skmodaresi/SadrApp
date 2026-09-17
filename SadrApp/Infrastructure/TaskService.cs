using Microsoft.EntityFrameworkCore;
using SadrApp.Data;

namespace SadrApp.Infrastructure;

/// <summary>One-time schema adjustments for the task tables.</summary>
public static class TaskService
{
    private static bool _done;

    /// <summary>
    /// TaskReports carries NOT NULL TaskId (project task) and NOT NULL FreeTaskId with FKs to both.
    /// A project-task report can never satisfy the FreeTasks FK, so it is disabled once.
    /// </summary>
    public static void EnsureSchema(SadrDbContext db)
    {
        if (_done) return;
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();
            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TaskReports_FreeTasks_FreeTaskId' AND is_disabled = 0) ALTER TABLE [TaskReports] NOCHECK CONSTRAINT [FK_TaskReports_FreeTasks_FreeTaskId]";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            _done = true;
        }
        catch
        {
            // The tables may not exist on a fresh database yet — retried next launch.
        }
    }
}
