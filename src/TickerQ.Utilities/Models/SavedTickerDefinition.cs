using System;
using System.Security.Cryptography;
using System.Text;

namespace TickerQ.Utilities.Models;

/// <summary>Purpose of a persisted dashboard definition.</summary>
public enum SavedTickerDefinitionKind
{
    Template = 0,
    Draft = 1,
}

/// <summary>Scheduler surface that can consume a persisted definition.</summary>
public enum SavedTickerDefinitionTarget
{
    TimeTicker = 0,
    CronTicker = 1,
}

/// <summary>
/// A reusable template or a non-executable time-ticker draft. The serialized definition contains
/// configuration only; execution state, results, logs and locks never belong here.
/// </summary>
public sealed class SavedTickerDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ApplicationNamespaceKey { get; set; } = string.Empty;
    public SavedTickerDefinitionKind Kind { get; set; }
    public SavedTickerDefinitionTarget Target { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Store-owned identity used to enforce unique template names. Drafts use their id so their
    /// display names remain independent.
    /// </summary>
    public string NameKey { get; set; } = string.Empty;
    public string Description { get; set; }
    public string DefinitionJson { get; set; } = string.Empty;
    public bool IncludesRequestData { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; } = 1;

    // Draft activation is claimed before publishing a real ticker. The root id is selected before
    // publication, allowing a later request to reconcile a process crash without creating a duplicate.
    public Guid? ActivationToken { get; set; }
    public Guid? PendingRootTickerId { get; set; }
    public DateTime? ActivationClaimedAtUtc { get; set; }
    public Guid? ActivatedRootTickerId { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }

    public SavedTickerDefinition Copy() => (SavedTickerDefinition)MemberwiseClone();
}

/// <summary>Canonical naming rules shared by every saved-definition store.</summary>
public static class SavedTickerDefinitionNames
{
    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var collapsed = string.Join(' ', name.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    }

    public static string CreateKey(SavedTickerDefinitionKind kind, SavedTickerDefinitionTarget target,
        string name, Guid id) => kind == SavedTickerDefinitionKind.Template
        ? $"template:{(int)target}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(name))))}"
        : $"draft:{id:N}";

    public static void AssignKey(SavedTickerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.NameKey = CreateKey(definition.Kind, definition.Target, definition.Name, definition.Id);
    }
}

/// <summary>Raised when a template name is already used in the same namespace and target.</summary>
public sealed class DuplicateSavedTickerDefinitionNameException(string name)
    : InvalidOperationException($"A template named ‘{name}’ already exists.")
{
}
