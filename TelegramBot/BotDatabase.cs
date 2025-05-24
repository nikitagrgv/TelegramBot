using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace TelegramBot;

public class BotDatabase : IDisposable, IBotDatabase
{
    private const string DatabaseTimeFormat = "yyyy-MM-dd HH:mm:ss";
    private readonly AppDbContext _dbContext;
    private bool _disposed;

    public BotDatabase(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    void IDisposable.Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    async Task<double?> IBotDatabase.GetMaxKcalAsync(long userId)
    {
        var entity = await _dbContext
            .Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);
        return entity?.MaxKcal ?? null;
    }

    async Task<bool> IBotDatabase.SetMaxKcalAsync(long userId, double? maxKcal)
    {
        var entity = await _dbContext
            .Users
            .FindAsync(userId);

        if (entity == null)
        {
            return false;
        }

        entity.MaxKcal = maxKcal;

        await _dbContext.SaveChangesAsync();
        return true;
    }

    private static Expression<Func<ConsumedRow, bool>> BuildFilterFunction(DateTime? begin, DateTime? end)
    {
        Expression<Func<ConsumedRow, bool>> filterFunc;

        if (begin != null && end != null)
        {
            filterFunc = c =>
                c.Date > begin &&
                c.Date < end;
        }
        else if (begin != null)
        {
            filterFunc = c => c.Date > begin;
        }
        else if (end != null)
        {
            filterFunc = c => c.Date < end;
        }
        else
        {
            filterFunc = c => true;
        }

        return filterFunc;
    }

    async Task<double> IBotDatabase.GetConsumedKcalAsync(DateTime? begin, DateTime? end, long userId)
    {
        Expression<Func<ConsumedRow, bool>> filterDateFunction = BuildFilterFunction(begin, end);

        double value = await _dbContext
            .Consumed
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Where(filterDateFunction)
            .SumAsync(c => c.Kcal ?? 0);
        return value;
    }

    async Task<ConsumedRow?> IBotDatabase.AddConsumedAsync(long userId, string name, double? kcal, DateTime date)
    {
        var newConsumedRow = new ConsumedRow()
        {
            UserId = userId,
            Date = date,
            Text = name,
            Kcal = kcal
        };

        await _dbContext.Consumed.AddAsync(newConsumedRow);

        await _dbContext.SaveChangesAsync();

        return newConsumedRow;
    }

    async Task<ConsumedRow?> IBotDatabase.RemoveConsumedAsync(long id, long? userId)
    {
        ConsumedRow? entity = await _dbContext
            .Consumed
            .FindAsync(id);

        if (entity == null)
        {
            return null;
        }

        _dbContext.Consumed.Remove(entity);

        await _dbContext.SaveChangesAsync();

        return entity;
    }

    async Task<List<ConsumedRow>> IBotDatabase.GetStatAsync(DateTime? optionalBegin, DateTime? optionalEnd,
        long? userId)
    {
        return new List<ConsumedRow>();
//         if (optionalBegin is { } begin && optionalEnd is { } end)
//         {
//             string userIdString = userId == null ? "" : "user_id = @id AND";
//             string sql = $"""
//                           SELECT *
//                           FROM consumed
//                           WHERE
//                               {userIdString}
//                               date BETWEEN @begin AND @end
//                           ORDER BY date;
//                           """;
//             await using var cmd = new SQLiteCommand(sql, _connection);
//             cmd.Parameters.AddWithValue("begin", ToDatabaseTimeFormat(begin));
//             cmd.Parameters.AddWithValue("end", ToDatabaseTimeFormat(end));
//             if (userId != null)
//             {
//                 cmd.Parameters.AddWithValue("id", userId);
//             }
//
//             return await ExecuteConsumedAndGetAllAsync(cmd);
//         }
//
//         if (optionalBegin is { } singleBegin)
//         {
//             string userIdString = userId == null ? "" : "user_id = @id AND";
//             string sql = $"""
//                           SELECT *
//                           FROM consumed
//                           WHERE
//                               {userIdString}
//                               date >= @begin
//                           ORDER BY date;
//                           """;
//             await using var cmd = new SQLiteCommand(sql, _connection);
//             cmd.Parameters.AddWithValue("begin", ToDatabaseTimeFormat(singleBegin));
//             if (userId != null)
//             {
//                 cmd.Parameters.AddWithValue("id", userId);
//             }
//
//             return await ExecuteConsumedAndGetAllAsync(cmd);
//         }
//
//         if (optionalEnd is { } singleEnd)
//         {
//             string userIdString = userId == null ? "" : "user_id = @id AND";
//             string sql = $"""
//                           SELECT *
//                           FROM consumed
//                           WHERE
//                               {userIdString}
//                               date <= @end
//                           ORDER BY date;
//                           """;
//             await using var cmd = new SQLiteCommand(sql, _connection);
//             cmd.Parameters.AddWithValue("end", ToDatabaseTimeFormat(singleEnd));
//             if (userId != null)
//             {
//                 cmd.Parameters.AddWithValue("id", userId);
//             }
//
//             return await ExecuteConsumedAndGetAllAsync(cmd);
//         }
//
//         string everythingEserIdString = userId == null ? "" : "WHERE user_id = @id";
//         string everythingSql = $"""
//                                 SELECT *
//                                 FROM consumed
//                                 {everythingEserIdString}
//                                 ORDER BY date;
//                                 """;
//         await using var everythingCmd = new SQLiteCommand(everythingSql, _connection);
//         if (userId != null)
//         {
//             everythingCmd.Parameters.AddWithValue("id", userId);
//         }
//
//         return await ExecuteConsumedAndGetAllAsync(everythingCmd);
    }

    // private async Task<ConsumedRowInfo?> ExecuteConsumedAndGetOneAsync(SQLiteCommand cmd)
    // {
    //     try
    //     {
    //         await using DbDataReader reader = await cmd.ExecuteReaderAsync();
    //         if (!await reader.ReadAsync())
    //         {
    //             return null;
    //         }
    //
    //         return ReadConsumedRowInfo(reader);
    //     }
    //     catch (Exception)
    //     {
    //         return null;
    //     }
    // }
    //
    // private async Task<List<ConsumedRowInfo>> ExecuteConsumedAndGetAllAsync(SQLiteCommand cmd)
    // {
    //     try
    //     {
    //         List<ConsumedRowInfo> rows = [];
    //
    //         await using DbDataReader reader = await cmd.ExecuteReaderAsync();
    //         while (await reader.ReadAsync())
    //         {
    //             ConsumedRowInfo? info = ReadConsumedRowInfo(reader);
    //             if (info is not null)
    //             {
    //                 rows.Add(info);
    //             }
    //         }
    //
    //         return rows;
    //     }
    //     catch (Exception)
    //     {
    //         return [];
    //     }
    // }
    //
    // private async Task<double?> ExecuteDoubleAsync(SQLiteCommand cmd)
    // {
    //     try
    //     {
    //         object? result = await cmd.ExecuteScalarAsync();
    //         return result switch
    //         {
    //             null => null,
    //             DBNull => null,
    //             _ => Convert.ToDouble(result)
    //         };
    //     }
    //     catch (Exception)
    //     {
    //         return null;
    //     }
    // }
    //
    // private async Task<double> ExecuteDoubleAsync(SQLiteCommand cmd, double defaultValue)
    // {
    //     return await ExecuteDoubleAsync(cmd) ?? defaultValue;
    // }
    //
    // private static ConsumedRowInfo? ReadConsumedRowInfo(DbDataReader reader)
    // {
    //     try
    //     {
    //         if (
    //             reader["id"] is not { } idObject || idObject is DBNull ||
    //             reader["user_id"] is not { } userIdObject || userIdObject is DBNull ||
    //             reader["date"] is not { } dateObject || dateObject is DBNull ||
    //             reader["text"] is not { } textObject || textObject is DBNull ||
    //             reader["kcal"] is not { } kcalObject || kcalObject is DBNull ||
    //             reader["date"].ToString() is not { } dateString ||
    //             reader["text"].ToString() is not { } text ||
    //             string.IsNullOrEmpty(dateString))
    //         {
    //             return null;
    //         }
    //
    //         long id = Convert.ToInt64(idObject);
    //         long userId = Convert.ToInt64(userIdObject);
    //         DateTime date = FromDatabaseTimeFormat(dateString);
    //         double kcal = Convert.ToDouble(kcalObject);
    //
    //         return new ConsumedRowInfo(id, userId, date, text, kcal);
    //     }
    //     catch (Exception)
    //     {
    //         return null;
    //     }
    // }

    async Task<bool> IBotDatabase.HasUserIdAsync(long userId)
    {
        return await _dbContext
            .Users
            .AsNoTracking()
            .Where(c => c.Id == userId)
            .AnyAsync();
    }

    async Task<bool> IBotDatabase.SetUserTimezoneOffsetAsync(long userId, int timezoneOffset)
    {
        return true;
        // string sql = "UPDATE users SET timezone = @timezone WHERE id = @id";
        // await using var cmd = new SQLiteCommand(sql, _connection);
        // cmd.Parameters.AddWithValue("id", userId);
        // cmd.Parameters.AddWithValue("timezone", timezoneOffset);
        // return await cmd.ExecuteNonQueryAsync() != 0;
    }

    async Task<int> IBotDatabase.GetUserTimezoneOffsetAsync(long userId)
    {
        return 0;
        // string sql = "SELECT timezone FROM users WHERE id = @id";
        // await using var cmd = new SQLiteCommand(sql, _connection);
        // cmd.Parameters.AddWithValue("id", userId);
        // object? result = await cmd.ExecuteScalarAsync();
        // try
        // {
        //     return Convert.ToInt32(result);
        // }
        // catch (Exception)
        // {
        //     return 0;
        // }
    }

    async Task<bool> IBotDatabase.RegisterUserIdAsync(long userId, DateTime date)
    {
        var newUser = new UserRow()
        {
            Id = userId,
            RegisterDate = date
        };
        await _dbContext.Users.AddAsync(newUser);

        await _dbContext.SaveChangesAsync();

        return true;

        return true;
        // string sql = """
        //              INSERT INTO users (id, register_date)
        //              VALUES (@id, @date);
        //              """;
        // await using var cmd = new SQLiteCommand(sql, _connection);
        // cmd.Parameters.AddWithValue("id", userId);
        // cmd.Parameters.AddWithValue("date", ToDatabaseTimeFormat(date));
        // return await cmd.ExecuteNonQueryAsync() != 0;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
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