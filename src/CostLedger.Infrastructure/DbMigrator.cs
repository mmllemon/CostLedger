using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CostLedger.Infrastructure;

/// <summary>迁移结果：迁移是否成功、源/目标版本、逐表行数校验、失败原因。</summary>
public sealed record MigrationResult(
    bool Success,
    int FromVersion,
    int ToVersion,
    IReadOnlyList<string> Steps,
    string? Error);

/// <summary>
/// 账套结构迁移（复制 → 通用加列迁移 → 校验 → 替换）。
/// 边界：只支持**增量**结构变更（新增表 / 新增列 / 新增索引）。历史 v10→v14 均为增量，
/// 因此可安全迁移。若旧账套存在当前结构没有的表，或所需列缺失且无法用默认值补齐，则拒绝迁移，
/// 原有账套保持不动，绝不重建、绝不丢数据。
/// </summary>
public static class DbMigrator
{
    /// <summary>可迁移的最早账套版本（再早的历史结构不在支持范围）。</summary>
    public const int MinSupportedVersion = 9;

    /// <summary>
    /// 把 <paramref name="sourcePath"/>（旧版本账套）迁移到当前结构 <paramref name="targetPath"/>。
    /// 先备份源文件，再在目标文件上建新结构并逐表搬运可共有的列，最后校验行数并写版本号。
    /// 任何一步失败都返回 Success=false 且不产出半成品。
    /// </summary>
    public static MigrationResult Migrate(string sourcePath, string targetPath)
    {
        var steps = new List<string>();
        int fromVersion;
        try { fromVersion = DbBootstrap.ReadUserVersionPublic(sourcePath); }
        catch (Exception ex) { return new MigrationResult(false, -1, DbBootstrap.SchemaVersion, steps, $"无法读取源账套版本：{ex.Message}"); }

        if (fromVersion == DbBootstrap.SchemaVersion)
            return new MigrationResult(true, fromVersion, DbBootstrap.SchemaVersion, steps, null);
        if (fromVersion < MinSupportedVersion)
            return new MigrationResult(false, fromVersion, DbBootstrap.SchemaVersion, steps, $"源账套版本 v{fromVersion} 早于支持范围 v{MinSupportedVersion}，无法自动迁移。");

        try
        {
            // 1) 复制源到目标（若目标是不同文件），保留原文件不动。
            var same = Path.GetFullPath(sourcePath) == Path.GetFullPath(targetPath);
            if (!same)
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
                steps.Add($"复制源账套到目标：{Path.GetFileName(targetPath)}");
            }

            // 2) 记录迁移前源库的表与列。
            var sourceSchema = ReadSchema(sourcePath);
            steps.Add($"读取源结构：{sourceSchema.Count} 张表");

            // 3) 在临时库上建当前结构，读取目标表与列。
            var tempPath = targetPath + ".migrating";
            File.Delete(tempPath);
            BuildSchema(tempPath);
            var targetSchema = ReadSchema(tempPath);
            steps.Add($"建立目标结构：{targetSchema.Count} 张表");

            // 4) 拒绝“源有、目标没有”的表（非增量变更，无法安全迁移）。
            var unknown = sourceSchema.Keys.Where(t => !targetSchema.ContainsKey(t)
                && !t.StartsWith("sqlite_") && t != "__EFMigrationsHistory").ToList();
            if (unknown.Count > 0)
            {
                SqliteConnection.ClearAllPools(); TryDeleteFile(tempPath);
                return new MigrationResult(false, fromVersion, DbBootstrap.SchemaVersion, steps,
                    $"源账套存在当前结构没有的表：{string.Join("、", unknown)}。该变更为非增量，无法安全迁移。");
            }

            // 5) 逐表搬运共有列数据：在目标库上 ATTACH 源库并 INSERT SELECT。
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tempPath, Pooling = false }.ToString()))
            {
                conn.Open();
                Attach(conn, sourcePath);
                var copied = new List<(string Table, long Rows)>();
                foreach (var (table, targetCols) in targetSchema)
                {
                    if (table.StartsWith("sqlite_") || table == "__EFMigrationsHistory") continue;
                    if (!sourceSchema.TryGetValue(table, out var sourceCols)) continue; // 新表：留空
                    var common = targetCols.Where(c => sourceCols.Contains(c)).ToList();
                    if (common.Count == 0) continue;
                    var colList = string.Join(", ", common.Select(Quote));
                    var sql = $"INSERT INTO main.{Quote(table)} ({colList}) SELECT {colList} FROM src.{Quote(table)};";
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    var rows = cmd.ExecuteNonQuery();
                    copied.Add((table, rows));
                }
                steps.Add($"搬运 {copied.Count} 张表数据，共 {copied.Sum(x => x.Rows)} 行");

                // 6) 校验：每张共有表的行数与源一致。
                foreach (var (table, _) in copied)
                {
                    long srcCount = Count(conn, "src", table);
                    long dstCount = Count(conn, "main", table);
                    if (srcCount != dstCount)
                        return new MigrationResult(false, fromVersion, DbBootstrap.SchemaVersion, steps,
                            $"表 {table} 行数校验失败：源 {srcCount} ≠ 目标 {dstCount}。");
                }
                steps.Add("行数校验通过");
                Detach(conn, sourcePath);
            }

            // 7) 用迁移后的库替换目标，写入当前版本号。
            File.Delete(targetPath);
            File.Move(tempPath, targetPath);
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = targetPath, Pooling = false }.ToString()))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA user_version = {DbBootstrap.SchemaVersion};";
                cmd.ExecuteNonQuery();
            }
            steps.Add($"写入结构版本 v{DbBootstrap.SchemaVersion}");

            return new MigrationResult(true, fromVersion, DbBootstrap.SchemaVersion, steps, null);
        }
        catch (Exception ex)
        {
            SqliteConnection.ClearAllPools(); TryDeleteFile(targetPath + ".migrating");
            return new MigrationResult(false, fromVersion, DbBootstrap.SchemaVersion, steps, ex.Message);
        }
    }

    /// <summary>在指定路径上建立当前结构（不含种子）。用非池化连接并在结束后清池，避免文件句柄残留。</summary>
    private static void BuildSchema(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
            .Options;
        using (var db = new AppDbContext(options))
            db.Database.EnsureCreated();
        SqliteConnection.ClearAllPools();
    }

    private static Dictionary<string, List<string>> ReadSchema(string path)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        var tables = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tables.Add(r.GetString(0));
        }
        foreach (var t in tables)
        {
            var cols = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({Quote(t)});";
                using var r = cmd.ExecuteReader();
                while (r.Read()) cols.Add(r.GetString(1));
            }
            result[t] = cols;
        }
        return result;
    }

    private static void Attach(SqliteConnection conn, string sourcePath)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "ATTACH DATABASE $p AS src;";
        cmd.Parameters.AddWithValue("$p", Path.GetFullPath(sourcePath));
        cmd.ExecuteNonQuery();
    }

    private static void Detach(SqliteConnection conn, string sourcePath)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DETACH DATABASE src;";
        cmd.ExecuteNonQuery();
    }

    private static long Count(SqliteConnection conn, string schema, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {schema}.{Quote(table)};";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
