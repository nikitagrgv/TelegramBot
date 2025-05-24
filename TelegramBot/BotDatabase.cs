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

    async Task<double> IBotDatabase.GetConsumedKcalAsync(DateTime? begin, DateTime? end, long userId)
    {
        Expression<Func<ConsumedRow, bool>> filterDateFunction = BuildFilterDateFunction(begin, end);

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

        if (userId != null && entity.UserId != userId)
        {
            return null;
        }

        _dbContext.Consumed.Remove(entity);

        await _dbContext.SaveChangesAsync();

        return entity;
    }

    async Task<List<ConsumedRow>> IBotDatabase.GetStatAsync(DateTime? begin, DateTime? end, long? userId)
    {
        Expression<Func<ConsumedRow, bool>> filterDateFunction = BuildFilterDateFunction(begin, end);
        Expression<Func<ConsumedRow, bool>> filterUserIdFunction = BuildFilterUserIdFunction(userId);

        List<ConsumedRow> entities = await _dbContext
            .Consumed
            .AsNoTracking()
            .Where(filterUserIdFunction)
            .Where(filterDateFunction)
            .ToListAsync();
        return entities;
    }

    async Task<bool> IBotDatabase.HasUserIdAsync(long userId)
    {
        return await _dbContext
            .Users
            .AsNoTracking()
            .Where(c => c.Id == userId)
            .AnyAsync();
    }

    async Task<bool> IBotDatabase.SetUserTimezoneOffsetAsync(long userId, int dateTimeOffset)
    {
        UserRow? entity = await _dbContext
            .Users
            .FindAsync(userId);

        if (entity == null)
        {
            return false;
        }

        entity.DateTimeOffset = dateTimeOffset;
        
        await _dbContext.SaveChangesAsync();
        
        return true;
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

    private static Expression<Func<ConsumedRow, bool>> BuildFilterDateFunction(DateTime? begin, DateTime? end)
    {
        if (begin != null && end != null)
        {
            return c =>
                c.Date > begin &&
                c.Date < end;
        }

        if (begin != null)
        {
            return c => c.Date > begin;
        }

        if (end != null)
        {
            return c => c.Date < end;
        }

        return c => true;
    }

    private static Expression<Func<ConsumedRow, bool>> BuildFilterUserIdFunction(long? userId)
    {
        if (userId == null)
        {
            return c => true;
        }

        return c => c.UserId == userId;
    }
}