using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TelegramBot;

public class BotDbContext : DbContext
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

            entity.Property(u => u.BanDate)
                .HasColumnName("ban_date")
                .HasConversion(dateConverter)
                .HasColumnType("TEXT");

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