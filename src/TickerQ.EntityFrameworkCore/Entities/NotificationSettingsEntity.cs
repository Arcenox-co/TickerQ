using System;

namespace TickerQ.EntityFrameworkCore.Entities;

public sealed class NotificationSettingsEntity
{
    public string ApplicationNamespaceKey { get; set; }
    public Guid Version { get; set; }
    public string ProtectedConfiguration { get; set; }
}
