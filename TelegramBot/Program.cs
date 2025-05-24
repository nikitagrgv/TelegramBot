using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TelegramBot;

public struct Config
{
    public Config()
    {
    }

    public Config(string? botToken, string? adminId)
    {
        BotToken = botToken;
        AdminId = adminId;
    }

    public string? BotToken { get; init; } = null;
    public string? AdminId { get; init; } = null;
}

class Program
{
    private static Config GetConfig()
    {
        const string configPath = "botconf.json";

        if (!File.Exists(configPath))
        {
            Console.WriteLine("No config found");
            return new Config();
        }

        string json = File.ReadAllText(configPath);
        Config? config = JsonSerializer.Deserialize<Config>(json);
        return config ?? new Config();
    }

    private static string? GetToken(Config config)
    {
        string? str = Environment.GetEnvironmentVariable("BOT_TOKEN");
        if (!string.IsNullOrEmpty(str))
        {
            Console.WriteLine("Get token from environment variable 'BOT_TOKEN'");
            return str;
        }

        str = config.BotToken;
        if (str != null)
        {
            Console.WriteLine("Get token from config");
            return str;
        }

        Console.WriteLine("No token found in config");
        return null;
    }

    private static string? GetAdminIdString(Config? config)
    {
        string? str = Environment.GetEnvironmentVariable("ADMIN_ID");
        if (!string.IsNullOrEmpty(str))
        {
            Console.WriteLine("Get admin ID from environment variable 'ADMIN_ID'");
            return str;
        }

        str = config?.AdminId;
        if (str != null)
        {
            Console.WriteLine("Get admin ID from config");
            return str;
        }

        Console.WriteLine("No admin ID found in config");
        return null;
    }

    public class UserRow
    {
        public long Id { get; set; }
        public DateTime RegisterDate { get; set; }
        public int? DateTimeOffset { get; set; }
        public double? MinKcal { get; set; }
        public double? MaxKcal { get; set; }

        public ICollection<ConsumedRow> ConsumedItems { get; set; }
    }

    public class ConsumedRow
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public DateTime Date { get; set; }
        public string Text { get; set; }
        public double? Kcal { get; set; }

        public UserRow User { get; set; }
    }

    public class AppDbContext : DbContext
    {
        public DbSet<UserRow> Users { get; set; }
        public DbSet<ConsumedRow> Consumed { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=ConsumeDatabase.sqlite");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            const string dateFormat = "yyyy-MM-dd HH:mm:ss";

            var dateConverter = new ValueConverter<DateTime, string>(
                dt => dt.ToString(dateFormat),
                str => DateTime.ParseExact(str, dateFormat, CultureInfo.InvariantCulture)
            );

            modelBuilder.Entity<UserRow>(entity =>
            {
                entity.ToTable("users");

                entity.HasKey(u => u.Id);

                entity.Property(u => u.Id)
                    .HasColumnName("user_id")
                    .ValueGeneratedNever();

                entity.Property(u => u.RegisterDate)
                    .HasColumnName("register_date")
                    .HasConversion(dateConverter)
                    .HasColumnType("TEXT")
                    .IsRequired();

                entity.Property(u => u.DateTimeOffset)
                    .HasColumnName("date_time_offset");

                entity.Property(u => u.MinKcal)
                    .HasColumnName("min_kcal")
                    .HasColumnType("REAL");

                entity.Property(u => u.MaxKcal)
                    .HasColumnName("max_kcal")
                    .HasColumnType("REAL");

                entity.HasMany(u => u.ConsumedItems)
                    .WithOne(c => c.User)
                    .HasForeignKey(c => c.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ConsumedRow>(entity =>
            {
                entity.ToTable("consumed");

                entity.HasKey(u => u.Id);

                entity.Property(c => c.Id)
                    .HasColumnName("id")
                    .ValueGeneratedOnAdd();

                entity.Property(c => c.UserId)
                    .HasColumnName("user_id")
                    .IsRequired();

                entity.Property(c => c.Date)
                    .HasColumnName("date")
                    .HasConversion(dateConverter)
                    .HasColumnType("TEXT")
                    .IsRequired();

                entity.Property(c => c.Text)
                    .HasColumnName("text")
                    .HasColumnType("TEXT")
                    .IsRequired();

                entity.Property(c => c.Kcal)
                    .HasColumnName("kcal")
                    .HasColumnType("REAL");

                entity.HasOne(c => c.User)
                    .WithMany(u => u.ConsumedItems)
                    .HasForeignKey(c => c.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }

    static async Task Main()
    {
        Console.WriteLine("TelegramBot");

        Config config = GetConfig();

        string? token = GetToken(config);
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        Console.WriteLine($"Token length is {token.Length}");

        string? adminIdString = GetAdminIdString(config);
        if (string.IsNullOrEmpty(adminIdString))
        {
            return;
        }

        if (!long.TryParse(adminIdString, out long adminId))
        {
            Console.WriteLine($"Admin ID is invalid: {adminIdString}");
            return;
        }

        Console.WriteLine($"Admin ID is {adminId}");

        await using var db = new AppDbContext();
        await db.Database.EnsureCreatedAsync();
        
        await db.Database.MigrateAsync();

        return;

        var database = new BotDatabase("ConsumeDatabase.sqlite");

        if (!await database.InitializeAsync())
        {
            Console.WriteLine("Can't open the database");
            return;
        }

        Console.WriteLine("Database initialized");

        CancellationTokenSource cancelTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            Console.WriteLine("Canceling pressed...");
            e.Cancel = true;
            cancelTokenSource.Cancel();
        };

        var bot = new WeightBot(database, adminId, cancelTokenSource);
        await bot.Run(token);

        Console.WriteLine("Done");
    }
}