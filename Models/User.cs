namespace YGODuelSimulator.Models;

/// <summary>A local application account. Passwords are stored as a PBKDF2 hash
/// with a per-user salt — never in plain text.</summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
