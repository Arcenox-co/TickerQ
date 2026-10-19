namespace TickerQ.Dashboard.Authentication.Endpoints;

/// <summary>Body of <c>POST /api/auth/login</c>.</summary>
public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>Successful response from <c>POST /api/auth/login</c> and <c>POST /api/auth/refresh</c>.</summary>
public sealed class LoginResponse
{
    /// <summary>The JWT access token. Send as <c>Authorization: Bearer …</c>.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Token type — always <c>Bearer</c>.</summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>Lifetime of the access token in seconds.</summary>
    public int ExpiresIn { get; set; }

    /// <summary>Authenticated username (echoed back so the SPA can render it).</summary>
    public string Username { get; set; } = string.Empty;
}
