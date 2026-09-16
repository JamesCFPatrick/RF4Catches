using Microsoft.EntityFrameworkCore;
using RF4Catches.Models;

namespace RF4Catches.Data;

public static class SeedData
{
    /// <summary>
    /// Generates fake fishing sessions and catches for testing the analytics
    /// and history views. Deterministic (seed 42) so the shape is stable
    /// across runs. Pass force:true to seed on top of existing data.
    /// </summary>
    public static async Task SeedFakeSessionsAsync(
        AppDbContext db,
        int count = 50,
        bool force = false)
    {
        if (!force && await db.FishingSessions.AnyAsync()) return;

        var random = new Random(42);
        var now = DateTimeOffset.UtcNow;

        var species = new (string Name, decimal MinKg, decimal MaxKg, decimal SilverPerKg)[]
        {
            ("Common Roach",    0.10m, 0.40m, 45m),
            ("Bream",           0.30m, 1.20m, 55m),
            ("Pike",            2.00m, 8.00m, 35m),
            ("Chub",            0.50m, 3.00m, 40m),
            ("Bluegill",        0.05m, 0.20m, 60m),
            ("Channel catfish", 1.50m, 6.00m, 30m),
            ("Largemouth bass", 0.80m, 3.00m, 50m),
            ("White crappie",   0.20m, 0.90m, 45m),
            ("Smallmouth bass", 0.60m, 2.50m, 50m),
            ("Chinese Sleeper", 0.10m, 0.50m, 40m),
        };

        var maps = new[]
        {
            "Mosquito Lake", "Old Burg", "Winding Rivulet",
            "Belaya River", "Ladoga Lake", "Volkhov River"
        };

        var baits = new[]
        {
            "Worm", "Bread", "Spinner", "Spoon",
            "Boilie", "Maggot", "Caster", "Sweet Corn"
        };

        var methods = new[] { "float", "feeder", "spinning" };

        // Weighted rarity pool — same shape as the real game.
        var rarityPool = new (string? Tag, int Weight)[]
        {
            (null,               60),
            ("Valuable",         30),
            ("Trophy",            5),
            ("Rare",              3),
            ("Rare Trophy",       1),
            ("Valuable, Trophy",  1),
        };

        for (var i = 0; i < count; i++)
        {
            var startedAt = now.AddDays(-random.Next(1, 30))
                                .AddHours(-random.Next(0, 24))
                                .AddMinutes(-random.Next(0, 60));
            var duration = TimeSpan.FromMinutes(random.Next(45, 240));
            var endedAt = startedAt + duration;

            var session = new FishingSession
            {
                StartedAtUtc = startedAt,
                EndedAtUtc = endedAt,
                FishingMethod = methods[random.Next(methods.Length)],
                Baits = baits[random.Next(baits.Length)],
                LineClip = $"{random.Next(10, 40)} m",
                HookDepthCm = random.Next(50, 250),
                MapName = maps[random.Next(maps.Length)],
                MapCoordinates = $"{random.Next(1, 60)}:{random.Next(1, 60)}",
            };

            var catchCount = random.Next(3, 30);
            var sessionSilver = 0m;

            for (var j = 0; j < catchCount; j++)
            {
                var (name, minKg, maxKg, silverPerKg) = species[random.Next(species.Length)];
                var weightKg = minKg + (decimal)random.NextDouble() * (maxKg - minKg);
                var lengthCm = weightKg * 25m + random.Next(-5, 10);

                // Weighted rarity pick.
                var roll = random.Next(100);
                var cumulative = 0;
                string? chosenRarity = null;
                foreach (var (tag, weight) in rarityPool)
                {
                    cumulative += weight;
                    if (roll < cumulative) { chosenRarity = tag; break; }
                }

                // Rarity multiplier for sale value.
                var multiplier = chosenRarity switch
                {
                    "Valuable" => 1.5m,
                    "Trophy" => 3m,
                    "Rare" => 5m,
                    "Rare Trophy" => 10m,
                    "Valuable, Trophy" => 4m,
                    _ => 1m
                };

                var saleValue = weightKg * silverPerKg * multiplier;
                sessionSilver += saleValue;

                // Caught at a random point during the session.
                var minutesIn = random.Next(0, Math.Max(1, (int)duration.TotalMinutes));
                var caughtAt = startedAt + TimeSpan.FromMinutes(minutesIn);

                db.Catches.Add(new Catch
                {
                    FishingSession = session,
                    Species = name,
                    WeightKg = Math.Round(weightKg, 3),
                    LengthCm = Math.Round(lengthCm, 1),
                    Rarity = chosenRarity,
                    CaughtAtUtc = caughtAt,
                    SaleValue = Math.Round(saleValue, 2),
                });
            }

            // ~70% of sessions have silver recorded.
            if (random.NextDouble() < 0.7)
            {
                var cafeShare = 0.4m + (decimal)random.NextDouble() * 0.3m;
                session.CafeSilver = Math.Round(sessionSilver * cafeShare, 2);
                session.MarketSilver = Math.Round(sessionSilver * (1 - cafeShare), 2);
            }

            db.FishingSessions.Add(session);
        }

        await db.SaveChangesAsync();
    }
}