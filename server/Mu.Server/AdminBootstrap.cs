using Microsoft.AspNetCore.Identity;
using Mu.Server.Data;

namespace Mu.Server;

public static class AdminBootstrap
{
    public static async Task<bool> RunAdminCommandAsync(this WebApplication app, string[] args)
    {
        var index = Array.IndexOf(args, "--create-admin");
        if (index < 0) return false;
        if (index + 1 >= args.Length || !System.Net.Mail.MailAddress.TryCreate(args[index + 1], out var email))
            throw new InvalidOperationException("Usage: --create-admin admin@example.com. Supply the password on stdin.");
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await users.FindByEmailAsync(email.Address) is not null)
            throw new InvalidOperationException("This email already exists. Administrator creation never promotes an existing account.");
        Console.Error.WriteLine("New administrator password (at least 12 characters; stdin, never command arguments):");
        var password = ReadPassword();
        if (password.Length is < 12 or > 128)
            throw new InvalidOperationException("Administrator passwords must contain 12 to 128 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!await roles.RoleExistsAsync(AdminSecurity.Role))
            RequireSuccess(await roles.CreateAsync(new IdentityRole(AdminSecurity.Role)));
        var user = new ApplicationUser
        {
            Email = email.Address,
            UserName = email.Address,
            EmailConfirmed = true,
            LockoutEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TermsVersion = "admin-bootstrap",
            TermsAcceptedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        RequireSuccess(await users.CreateAsync(user, password));
        RequireSuccess(await users.AddToRoleAsync(user, AdminSecurity.Role));
        db.AuditEntries.Add(new AuditEntry
        {
            Actor = "server-cli", UserId = user.Id, Action = "admin.created", Reason = "服务器初始化管理员",
            BeforeJson = "{}", AfterJson = "{\"role\":\"Admin\",\"twoFactorEnabled\":false}",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        Console.WriteLine("Administrator created. Sign in at /admin/login and enroll an authenticator before accessing management.");
        return true;
    }

    private static string ReadPassword()
    {
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.Error.WriteLine(); return password.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password.Length--; }
            else if (!char.IsControl(key.KeyChar)) password.Append(key.KeyChar);
        }
    }

    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
