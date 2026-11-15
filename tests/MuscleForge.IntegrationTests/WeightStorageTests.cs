using Microsoft.EntityFrameworkCore;
using MuscleForge.Core.Measurements;
using Xunit;

namespace MuscleForge.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class WeightStorageTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task DecimalKilogramsRoundTripThroughNumeric103()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await postgres.CreateDatabaseAsync(cancellationToken);
        await using var context = new WeightContext(new DbContextOptionsBuilder<WeightContext>()
            .UseNpgsql(database.ConnectionString).Options);
        await context.Database.OpenConnectionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE \"WeightFixture\" (\"Id\" integer PRIMARY KEY, \"Kilograms\" numeric(10,3) NOT NULL)", cancellationToken);
        context.Weights.Add(new WeightRow { Id = 1, Kilograms = ExternalWeight.ToKilograms(100m, WeightUnit.Pounds) });
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        Assert.Equal(45.359m, (await context.Weights.SingleAsync(cancellationToken)).Kilograms);
    }

    private sealed class WeightContext(DbContextOptions<WeightContext> options) : DbContext(options)
    {
        public DbSet<WeightRow> Weights => Set<WeightRow>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<WeightRow>().ToTable("WeightFixture");
            modelBuilder.Entity<WeightRow>().HasKey(x => x.Id);
            modelBuilder.Entity<WeightRow>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<WeightRow>().Property(x => x.Kilograms).HasPrecision(10, 3);
        }
    }

    private sealed class WeightRow
    {
        public int Id { get; set; }
        public decimal Kilograms { get; set; }
    }
}
