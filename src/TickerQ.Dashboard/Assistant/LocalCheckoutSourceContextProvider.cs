using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.Dashboard.Assistant;

/// <summary>
/// Reads bounded source excerpts from an explicitly allow-listed local checkout.
/// This provider is intended for development hosts and deployments that deliberately
/// make their matching source revision available beside the application.
/// </summary>
public sealed class LocalCheckoutSourceContextProvider : IExecutionSourceContextProvider
{
    private readonly string _sourceRoot;
    private readonly string _sourceRootPrefix;
    private readonly LocalSourceContextOptionsBuilder _options;
    private readonly StringComparison _pathComparison;

    public LocalCheckoutSourceContextProvider(
        string sourceRoot,
        Action<LocalSourceContextOptionsBuilder> configure)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
            throw new ArgumentException("A source root is required.", nameof(sourceRoot));
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        _sourceRoot = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(_sourceRoot))
            throw new DirectoryNotFoundException($"The source root does not exist: {_sourceRoot}");

        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        _sourceRootPrefix = _sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;

        _options = new LocalSourceContextOptionsBuilder(
            defaultRepository: Path.GetFileName(_sourceRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)));
        configure(_options);

        if (_options.FunctionSources.Count == 0 && _options.FileSources.Count == 0)
            throw new InvalidOperationException(
                "At least one source file must be allow-listed with MapFunction or MapSourceFile.");

        ValidateMappings(_options.FunctionSources.Values);
        ValidateMappings(_options.FileSources.Values);
    }

    public async Task<ExecutionSourceContext?> GetSourceContextAsync(
        ExecutionSourceContextRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.SourceLine is not { } sourceLine || sourceLine < 1)
            return null;

        string? relativePath = null;
        if (!string.IsNullOrWhiteSpace(request.FunctionName))
            _options.FunctionSources.TryGetValue(request.FunctionName, out relativePath);

        if (relativePath == null && !string.IsNullOrWhiteSpace(request.SourceFile))
            _options.FileSources.TryGetValue(Path.GetFileName(request.SourceFile), out relativePath);

        if (relativePath == null)
            return null;

        var fullPath = ResolveAllowedPath(relativePath);
        if (!File.Exists(fullPath))
            return null;

        var startLine = Math.Max(1, sourceLine - _options.ContextLinesBefore);
        var endLine = sourceLine > int.MaxValue - _options.ContextLinesAfter
            ? int.MaxValue
            : sourceLine + _options.ContextLinesAfter;
        var selected = new List<string>(_options.ContextLinesBefore + _options.ContextLinesAfter + 1);

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var currentLine = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            currentLine++;
            if (currentLine >= startLine)
                selected.Add(line);
            if (currentLine >= endLine)
                break;
        }

        if (selected.Count == 0 || currentLine < sourceLine)
            return null;

        return new ExecutionSourceContext
        {
            Repository = _options.Repository,
            Revision = _options.Revision,
            Path = relativePath.Replace('\\', '/'),
            StartLine = startLine,
            Code = string.Join("\n", selected),
        };
    }

    private void ValidateMappings(IEnumerable<string> paths)
    {
        foreach (var relativePath in paths)
            _ = ResolveAllowedPath(relativePath);
    }

    private string ResolveAllowedPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("Mapped source paths must be non-empty and relative to the source root.");

        var fullPath = Path.GetFullPath(Path.Combine(_sourceRoot, relativePath));
        if (!fullPath.StartsWith(_sourceRootPrefix, _pathComparison))
            throw new ArgumentException("Mapped source paths cannot leave the configured source root.");
        return fullPath;
    }
}

/// <summary>Configures the allow-list used by <see cref="LocalCheckoutSourceContextProvider"/>.</summary>
public sealed class LocalSourceContextOptionsBuilder
{
    internal Dictionary<string, string> FunctionSources { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, string> FileSources { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    internal int ContextLinesBefore { get; private set; } = 20;
    internal int ContextLinesAfter { get; private set; } = 20;
    internal string Repository { get; private set; }
    internal string Revision { get; private set; } = "local-working-tree";

    internal LocalSourceContextOptionsBuilder(string defaultRepository)
        => Repository = string.IsNullOrWhiteSpace(defaultRepository) ? "local-checkout" : defaultRepository;

    /// <summary>Map a registered TickerQ function name to one repository-relative source file.</summary>
    public LocalSourceContextOptionsBuilder MapFunction(string functionName, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            throw new ArgumentException("A function name is required.", nameof(functionName));
        FunctionSources[functionName.Trim()] = RequireRelativePath(relativePath);
        return this;
    }

    /// <summary>
    /// Map a captured source filename to one repository-relative source file. Function mappings
    /// take precedence and should be used when multiple projects contain the same filename.
    /// </summary>
    public LocalSourceContextOptionsBuilder MapSourceFile(string sourceFile, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
            throw new ArgumentException("A source filename is required.", nameof(sourceFile));
        var fileName = sourceFile.Trim();
        if (fileName.Contains('/') || fileName.Contains('\\'))
            throw new ArgumentException("Source file keys must be filenames without directories.", nameof(sourceFile));
        FileSources[fileName] = RequireRelativePath(relativePath);
        return this;
    }

    /// <summary>Set the number of lines returned before and after the captured failure line.</summary>
    public LocalSourceContextOptionsBuilder WithContextLines(int before, int after)
    {
        if (before is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(before), "Context lines must be between 0 and 100.");
        if (after is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(after), "Context lines must be between 0 and 100.");
        ContextLinesBefore = before;
        ContextLinesAfter = after;
        return this;
    }

    /// <summary>Set the friendly repository label included with the source excerpt.</summary>
    public LocalSourceContextOptionsBuilder WithRepository(string repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
            throw new ArgumentException("A repository label is required.", nameof(repository));
        Repository = repository.Trim();
        return this;
    }

    /// <summary>
    /// Set the deployed commit or immutable revision. Use "local-working-tree" only for development.
    /// </summary>
    public LocalSourceContextOptionsBuilder WithRevision(string revision)
    {
        if (string.IsNullOrWhiteSpace(revision))
            throw new ArgumentException("A revision is required.", nameof(revision));
        Revision = revision.Trim();
        return this;
    }

    private static string RequireRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("A repository-relative source path is required.", nameof(relativePath));
        return relativePath.Trim();
    }
}
