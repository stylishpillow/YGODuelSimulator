using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using YGODuelSimulator.Data;
using YGODuelSimulator.Models;

namespace YGODuelSimulator.Services;

/// <summary>
/// Local username/password accounts stored in the app database. Passwords are
/// hashed with PBKDF2-HMAC-SHA256 and a per-user salt. Hashes are stored in a
/// self-describing string (<c>pbkdf2-sha256$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;</c>)
/// so the work factor can be raised over time without breaking existing accounts;
/// weaker/older hashes are transparently upgraded on the next successful login.
/// </summary>
public class AuthService
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    // OWASP-recommended work factor for PBKDF2-HMAC-SHA256 (2023).
    private const int CurrentIterations = 600_000;
    private const string Scheme = "pbkdf2-sha256";

    // Minimums for new accounts / password changes.
    public const int MinPasswordLength = 8;
    private const int MinUsernameLength = 3;
    private const int MaxUsernameLength = 32;

    public const string AdminUsername = "admin";

    // The single built-in admin account ships as a password HASH only — the plaintext is
    // never in the source. The hash can't be reversed, so only whoever knows the matching
    // password can sign in as admin. To make admin truly private, replace this with the
    // hash of a strong password you keep to yourself (compute it with the same PBKDF2
    // parameters used by HashPassword below). Current password: Admin@12345.
    private const string AdminPasswordHash =
        "pbkdf2-sha256$600000$TFAjaR/VScW8hhMvw+CNCw==$Y3+F1aktlFWsunuQGXzGA33zpoCsHTVAM0Tiq1DGczE=";

    // A precomputed hash to verify against when the username doesn't exist, so a
    // failed login costs the same time whether or not the account is real (this
    // avoids leaking which usernames exist via response timing).
    private static readonly string DummyHash = HashPassword("this-account-does-not-exist");

    /// <summary>Ensures the single built-in admin account exists, seeded from the baked-in
    /// password <b>hash</b> (never a plaintext default). The same account exists on every
    /// install, but only someone who knows the matching password can sign in as admin.</summary>
    public async Task EnsureAdminSeededAsync()
    {
        await using var db = new AppDbContext();
        if (await db.Users.AnyAsync(u => u.Username == AdminUsername)) return;

        db.Users.Add(new User
        {
            Username = AdminUsername,
            PasswordHash = AdminPasswordHash,
            PasswordSalt = string.Empty,
            IsAdmin = true,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Returns the user if the credentials match, otherwise null. On success
    /// a weak/legacy hash is upgraded to the current work factor.</summary>
    public async Task<User?> AuthenticateAsync(string username, string password)
    {
        username = username.Trim();
        await using var db = new AppDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (user is null)
        {
            // Spend comparable time so a missing username isn't faster than a wrong password.
            Verify(password, DummyHash, string.Empty, out _);
            return null;
        }

        if (!Verify(password, user.PasswordHash, user.PasswordSalt, out var needsUpgrade))
            return null;

        if (needsUpgrade)
        {
            user.PasswordHash = HashPassword(password);
            user.PasswordSalt = string.Empty;
            await db.SaveChangesAsync();
        }
        return user;
    }

    /// <summary>Creates a normal (non-admin) account. Returns an error message on failure.</summary>
    public async Task<(User? user, string? error)> RegisterAsync(string username, string password)
    {
        username = username.Trim();
        if (ValidateUsername(username) is { } uErr) return (null, uErr);
        if (ValidatePassword(password) is { } pErr) return (null, pErr);

        await using var db = new AppDbContext();
        if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
            return (null, "That username is already taken.");

        var user = NewUser(username, password, isAdmin: false);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user, null);
    }

    /// <summary>Changes a user's password after verifying the current one. Returns an
    /// error message on failure.</summary>
    public async Task<string?> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        if (ValidatePassword(newPassword) is { } pErr) return pErr;

        await using var db = new AppDbContext();
        var user = await db.Users.FindAsync(userId);
        if (user is null) return "Account not found.";
        if (!Verify(currentPassword, user.PasswordHash, user.PasswordSalt, out _))
            return "Current password is incorrect.";

        user.PasswordHash = HashPassword(newPassword);
        user.PasswordSalt = string.Empty;
        await db.SaveChangesAsync();
        return null;
    }

    // --- validation ---

    private static string? ValidateUsername(string username)
    {
        if (username.Length is < MinUsernameLength or > MaxUsernameLength)
            return $"Username must be {MinUsernameLength}–{MaxUsernameLength} characters.";
        if (!username.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.'))
            return "Username may only use letters, digits, and _ - .";
        return null;
    }

    private static string? ValidatePassword(string password) =>
        string.IsNullOrEmpty(password) || password.Length < MinPasswordLength
            ? $"Password must be at least {MinPasswordLength} characters."
            : null;

    // --- hashing ---

    private static User NewUser(string username, string password, bool isAdmin) => new()
    {
        Username = username,
        PasswordHash = HashPassword(password),
        PasswordSalt = string.Empty,
        IsAdmin = isAdmin,
    };

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Pbkdf2(password, salt, CurrentIterations);
        return $"{Scheme}${CurrentIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Verifies a password against either the new self-describing format or a
    /// legacy (raw base64 hash + separate salt column, 100k iterations) record, and
    /// reports whether the stored hash should be re-hashed at the current work factor.</summary>
    private static bool Verify(string password, string storedHash, string legacySalt, out bool needsUpgrade)
    {
        byte[] salt, expected;
        int iterations;

        if (storedHash.StartsWith(Scheme + "$", StringComparison.Ordinal))
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out iterations))
            {
                needsUpgrade = false;
                return false;
            }
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        else
        {
            // Legacy record written before the self-describing format.
            iterations = 100_000;
            salt = Convert.FromBase64String(legacySalt);
            expected = Convert.FromBase64String(storedHash);
        }

        var actual = Pbkdf2(password, salt, iterations, expected.Length);
        var ok = CryptographicOperations.FixedTimeEquals(actual, expected);
        needsUpgrade = ok && iterations < CurrentIterations;
        return ok;
    }

    private static byte[] Pbkdf2(string password, byte[] salt, int iterations, int length = HashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, length);
}

/// <summary>Holds the account signed in for the current app session.</summary>
public static class Session
{
    public static User? CurrentUser { get; set; }
    public static bool IsAdmin => CurrentUser?.IsAdmin ?? false;
}
