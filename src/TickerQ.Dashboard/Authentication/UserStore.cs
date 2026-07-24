using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace TickerQ.Dashboard.Authentication;

/// <summary>
/// Source of username/password credentials for credential-based auth schemes
/// (JWT login, cookie login, Basic). Implementations should be thread-safe
/// and constant-time on comparison.
/// </summary>
public interface IUserStore
{
    /// <summary>True when <paramref name="username"/> + <paramref name="password"/> match a registered user.</summary>
    bool Validate(string username, string password);
}

/// <summary>
/// In-memory user store. Passwords are hashed once at registration time with
/// PBKDF2 + per-user salt and never stored in plain text. Comparison uses
/// <see cref="CryptographicOperations.FixedTimeEquals"/>.
/// </summary>
public sealed class InMemoryUserStore : IUserStore
{
    // Industry-standard ballpark for PBKDF2-SHA256 as of 2025; cheap enough at
    // login time (~10ms) and expensive enough to make offline cracking painful.
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private readonly Dictionary<string, HashedCredential> _users = new(StringComparer.Ordinal);

    /// <summary>Register a user. Password is hashed immediately and not retained.</summary>
    public void AddUser(string username, string password)
    {
        if (string.IsNullOrEmpty(username)) throw new ArgumentException("Username required.", nameof(username));
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password required.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Pbkdf2(password, salt);
        _users[username] = new HashedCredential(salt, hash);
    }

    public bool Validate(string username, string password)
    {
        if (!_users.TryGetValue(username, out var cred)) return false;
        var candidate = Pbkdf2(password, cred.Salt);
        return CryptographicOperations.FixedTimeEquals(candidate, cred.Hash);
    }

    private static byte[] Pbkdf2(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);

    private readonly record struct HashedCredential(byte[] Salt, byte[] Hash);
}
