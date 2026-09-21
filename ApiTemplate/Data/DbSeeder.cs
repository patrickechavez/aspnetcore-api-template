using ApiTemplate.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ApiTemplate.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(nameof(DbSeeder));

        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();

        if (environment.IsDevelopment())
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        if (await context.Users.AnyAsync(u => u.Role == UserRole.Admin, cancellationToken))
        {
            return;
        }

        var email = configuration["Seed:AdminEmail"];
        var password = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "No admin user exists and Seed:AdminEmail or Seed:AdminPassword is not configured. " +
                "Skipping admin seeding.");
            return;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (await context.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken))
        {
            logger.LogError(
                "No administrator exists, but the seed email {Email} is already taken by a non-admin account. " +
                "Promote that account manually; skipping seeding.",
                normalizedEmail);
            return;
        }

        var admin = new User
        {
            Email = normalizedEmail,
            DisplayName = "Administrator",
            Role = UserRole.Admin
        };

        admin.PasswordHash = hasher.HashPassword(admin, password);

        context.Users.Add(admin);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded administrator {Email}", admin.Email);
    }
}
