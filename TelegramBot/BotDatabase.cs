﻿using System.Data.Common;
using System.Data.SQLite;
using System.Globalization;

namespace TelegramBot;

public class BotDatabase : IDisposable
{
    private const string DatabaseTimeFormat = "yyyy-MM-dd HH:mm:ss";
    private readonly SQLiteConnection _connection;
    private bool _disposed;

    public BotDatabase(string databasePath)
    {
        string connectionString = $"Data Source={databasePath};Version=3;";

        _connection = new SQLiteConnection(connectionString);
    }

    void IDisposable.Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async Task<bool> InitializeAsync()
    {
        await _connection.OpenAsync();
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            return false;
        }

        await using var cmd = new SQLiteCommand("PRAGMA foreign_keys = ON;", _connection);
        await cmd.ExecuteNonQueryAsync();

        int oldVersion = await GetDatabaseVersion();
        int newVersion = await MigrateDatabaseToLatestVersion(oldVersion);

        if (oldVersion != newVersion)
        {
            await SetDatabaseVersion(newVersion);
        }

        return true;
    }

    public async Task<double?> GetMaxKcalAsync(long userId)
    {
        const string sql = """
                           SELECT max_kcal
                           FROM users
                           WHERE id = @id
                           """;
        await using var cmd = new SQLiteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@id", userId);
        return await ExecuteDoubleAsync(cmd);
    }

    public async Task<bool> SetMaxKcalAsync(long userId, double? maxKcal)
    {
        const string sql = """
                           UPDATE users
                           SET max_kcal = @max_kcal
                           WHERE id = @id
                           """;
        await using var cmd = new SQLiteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.Parameters.AddWithValue("@max_kcal", maxKcal);
        return await cmd.ExecuteNonQueryAsync() != 0;
    }

    public async Task<double> GetConsumedCalAsync(DateTime? optionalBegin, DateTime? optionalEnd,
        long userId)
    {
        if (optionalBegin is { } begin && optionalEnd is { } end)
        {
            const string sql = """
                               SELECT SUM(kcal)
                               FROM consumed
                               WHERE user_id = @id AND date BETWEEN @begin AND @end
                               """;
            await using var cmd = new SQLiteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("begin", ToDatabaseTimeFormat(begin));
            cmd.Parameters.AddWithValue("end", ToDatabaseTimeFormat(end));
            cmd.Parameters.AddWithValue("id", userId);
            return await ExecuteDoubleAsync(cmd, 0);
        }

        if (optionalBegin is { } singleBegin)
        {
            const string sql = """
                               SELECT SUM(kcal)
                               FROM consumed
                               WHERE user_id = @id AND date >= @begin
                               """;
            await using var cmd = new SQLiteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("begin", ToDatabaseTimeFormat(singleBegin));
            cmd.Parameters.AddWithValue("id", userId);
            return await ExecuteDoubleAsync(cmd, 0);
        }

        if (optionalEnd is { } singleEnd)
        {
            const string sql = """
                               SELECT SUM(kcal)
                               FROM consumed
                               WHERE user_id = @id AND date <= @end
                               """;
            await using var cmd = new SQLiteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("end", ToDatabaseTimeFormat(singleEnd));
            cmd.Parameters.AddWithValue("id", userId);
            return await ExecuteDoubleAsync(cmd, 0);
        }

        const string everythingSql = """
                                     SELECT SUM(kcal)
                                     FROM consumed
                                     WHERE user_id = @id
                                     """;
        await using var everythingCmd = new SQLiteCommand(everythingSql, _connection);
        everythingCmd.Parameters.AddWithValue("id", userId);
        return await ExecuteDoubleAsync(everythingCmd, 0);
    }

    public async Task<ConsumedRowInfo?> AddConsumedAsync(long userId, string name, double kcal, DateTime date)
    {
        string sql = """
                     INSERT INTO consumed (user_id, date, text, kcal)
                     VALUES (@user_id, @date, @text, @kcal)
                     RETURNING *;
                     """;
        await using var cmd = new SQLiteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("date", ToDatabaseTimeFormat(date));
        cmd.Parameters.AddWithValue("text", name);
        cmd.Parameters.AddWithValue("kcal", kcal);

        return await ExecuteConsumedAndGetOneAsync(cmd);
    }

    public async Task<ConsumedRowInfo?> RemoveConsumedAsync(long id, long? userId)
    {
        string userIdString = userId == null ? "" : "user_id = @user_id AND";
        string sql = $"""
                      DELETE
                      FROM consumed
                      WHERE
                          {userIdString}
                          id = @id
                      RETURNING *;
                      """;
        await using var cmd = new SQLiteCommand(sql, _connection);
        if (userId != null)
        {
            cmd.Parameters.AddWithValue("user_id", userId);
        }

        cmd.Parameters.AddWithValue("id", id);

        return await ExecuteConsumedAndGetOneAsync(cmd);
    }

    public async Task<List<ConsumedRowInfo>> GetStatAsync(DateTime? optionalBegin, DateTime? optionalEnd,
        long? userId)
    {
        if (optionalBegin is { } begin && optionalEnd is { } end)
        {
            string userIdString = userId == null ? "" : "user_id = @id AND";
            string sql = $"""
                          SELECT *
                          FROM consumed
                          WHERE
                              {userIdString}
                              date BETWEEN @begin AND @end
                          ORDER BY date;
                          """;
            await using var cmd = new SQLiteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("begin", ToDatabaseTimeFormat(begin));
            cmd.Parameters.AddWithValue("end", ToDatabaseTimeFormat(end));
            if (userId != null)
            {
                cmd.Parameters.AddWithValue("id", userId);
            }

            return await ExecuteConsumedAndGetAllAsync(cmd);
        }

        if (optionalBegin is { } singleBegin)
        {
            string userIdString = userId == null ? "" : "user_id = @id AND";
            string sql = $"""
                          SELECT *
                          FROM consumed
                          WHERE
                              {userIdString}
                              date >= @begin
                          ORDER BY date;
                          """;
            await using var cmd = new SQLiteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("begin", ToDatabaseTimeFormat(singleBegin));
            if (userId != null)
            {
                cmd.Parameters.AddWithValue("id", userId);
            }

            return await ExecuteConsumedAndGetAllAsync(cmd);
        }

        if (optionalEnd is { } singleEnd)
        {
            string userIdString = userId == null ? "" : "user_id = @id AND";
            string sql = $"""
                          SELECT *
                          FROM consumed
                          WHERE
                              {userIdString}
                              date <= @end
                          ORDER BY date;
                          """;
            await using var cmd = new SQLiteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("end", ToDatabaseTimeFormat(singleEnd));
            if (userId != null)
            {
                cmd.Parameters.AddWithValue("id", userId);
            }

            return await ExecuteConsumedAndGetAllAsync(cmd);
        }

        string everythingEserIdString = userId == null ? "" : "WHERE user_id = @id";
        string everythingSql = $"""
                                SELECT *
                                FROM consumed
                                {everythingEserIdString}
                                ORDER BY date;
                                """;
        await using var everythingCmd = new SQLiteCommand(everythingSql, _connection);
        if (userId != null)
        {
            everythingCmd.Parameters.AddWithValue("id", userId);
        }

        return await ExecuteConsumedAndGetAllAsync(everythingCmd);
    }

    private async Task<ConsumedRowInfo?> ExecuteConsumedAndGetOneAsync(SQLiteCommand cmd)
    {
        try
        {
            await using DbDataReader reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return ReadConsumedRowInfo(reader);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<List<ConsumedRowInfo>> ExecuteConsumedAndGetAllAsync(SQLiteCommand cmd)
    {
        try
        {
            List<ConsumedRowInfo> rows = [];

            await using DbDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ConsumedRowInfo? info = ReadConsumedRowInfo(reader);
                if (info is not null)
                {
                    rows.Add(info);
                }
            }

            return rows;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private async Task<double?> ExecuteDoubleAsync(SQLiteCommand cmd)
    {
        try
        {
            object? result = await cmd.ExecuteScalarAsync();
            return result switch
            {
                null => null,
                DBNull => null,
                _ => Convert.ToDouble(result)
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<double> ExecuteDoubleAsync(SQLiteCommand cmd, double defaultValue)
    {
        return await ExecuteDoubleAsync(cmd) ?? defaultValue;
    }

    private static ConsumedRowInfo? ReadConsumedRowInfo(DbDataReader reader)
    {
        try
        {
            if (
                reader["id"] is not { } idObject || idObject is DBNull ||
                reader["user_id"] is not { } userIdObject || userIdObject is DBNull ||
                reader["date"] is not { } dateObject || dateObject is DBNull ||
                reader["text"] is not { } textObject || textObject is DBNull ||
                reader["kcal"] is not { } kcalObject || kcalObject is DBNull ||
                reader["date"].ToString() is not { } dateString ||
                reader["text"].ToString() is not { } text ||
                string.IsNullOrEmpty(dateString))
            {
                return null;
            }

            long id = Convert.ToInt64(idObject);
            long userId = Convert.ToInt64(userIdObject);
            DateTime date = FromDatabaseTimeFormat(dateString);
            double kcal = Convert.ToDouble(kcalObject);

            return new ConsumedRowInfo(id, userId, date, text, kcal);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<bool> HasUserIdAsync(long userId)
    {
        string sql = "SELECT EXISTS(SELECT 1 FROM users WHERE id = @id)";
        await using var cmd = new SQLiteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("id", userId);
        object? result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) == 1;
    }

    public async Task<bool> SetUserTimezoneOffsetAsync(long userId, int timezoneOffset)
    {
        string sql = "UPDATE users SET timezone = @timezone WHERE id = @id";
        await using var cmd = new SQLiteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("timezone", timezoneOffset);
        return await cmd.ExecuteNonQueryAsync() != 0;
    }

    public async Task<int> GetUserTimezoneOffsetAsync(long userId)
    {
        string sql = "SELECT timezone FROM users WHERE id = @id";
        await using var cmd = new SQLiteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("id", userId);
        object? result = await cmd.ExecuteScalarAsync();
        try
        {
            return Convert.ToInt32(result);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public async Task<bool> RegisterUserIdAsync(long userId, DateTime date)
    {
        string sql = """
                     INSERT INTO users (id, register_date)
                     VALUES (@id, @date);
                     """;
        await using var cmd = new SQLiteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("date", ToDatabaseTimeFormat(date));
        return await cmd.ExecuteNonQueryAsync() != 0;
    }


    private async Task<int> MigrateDatabaseToLatestVersion(int oldVersion)
    {
        int newVersion = oldVersion;

        if (newVersion < 1)
        {
            await MigrateDatabaseToVersion1();
            newVersion = 1;
        }

        return newVersion;
    }

    private async Task MigrateDatabaseToVersion1()
    {
        {
            await using var cmd = new SQLiteCommand("""
                                                    CREATE TABLE users
                                                    (
                                                        id            INTEGER PRIMARY KEY,
                                                        register_date TEXT NOT NULL,
                                                        timezone      INTEGER NOT NULL DEFAULT 0,
                                                        min_kcal      REAL,
                                                        max_kcal      REAL
                                                    );
                                                    """, _connection);
            await cmd.ExecuteNonQueryAsync();
        }
        {
            await using var cmd = new SQLiteCommand("""
                                                    CREATE TABLE consumed
                                                    (
                                                        id      INTEGER PRIMARY KEY AUTOINCREMENT,
                                                        user_id INTEGER NOT NULL,
                                                        date    TEXT    NOT NULL,
                                                        text    TEXT    NOT NULL,
                                                        kcal    REAL,
                                                        FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
                                                    );
                                                    """, _connection);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<int> GetDatabaseVersion()
    {
        await using var cmd = new SQLiteCommand("PRAGMA user_version;", _connection);
        object? ret = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(ret);
    }

    private async Task SetDatabaseVersion(int version)
    {
        await using var cmd = new SQLiteCommand($"PRAGMA user_version = {version};", _connection);
        await cmd.ExecuteNonQueryAsync();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _connection.Dispose();
        }

        _disposed = true;
    }


    private static string ToDatabaseTimeFormat(DateTime dateTime)
    {
        return dateTime.ToString(DatabaseTimeFormat, CultureInfo.InvariantCulture);
    }

    private static DateTime FromDatabaseTimeFormat(string dateTime)
    {
        return DateTime.ParseExact(dateTime, DatabaseTimeFormat, CultureInfo.InvariantCulture);
    }
}