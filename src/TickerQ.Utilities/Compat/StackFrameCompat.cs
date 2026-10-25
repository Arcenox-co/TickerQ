using System.Diagnostics;
#if !NET9_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif

namespace TickerQ.Utilities.Compat;

/// <summary>
/// Names the method of a stack frame. .NET 9 adds the trimming-safe <c>DiagnosticMethodInfo</c>; earlier
/// runtimes fall back to reflection, which can return null once metadata is trimmed away.
/// </summary>
internal static class StackFrameCompat
{
#if NET9_0_OR_GREATER
    public static bool TryDescribe(StackFrame frame, out string declaringTypeName, out string methodName)
    {
        var method = DiagnosticMethodInfo.Create(frame);
        declaringTypeName = method?.DeclaringTypeName;
        methodName = method?.Name;
        return method != null;
    }
#else
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Caller names are diagnostic only; trimmed metadata yields null and callers report Unknown.")]
    public static bool TryDescribe(StackFrame frame, out string declaringTypeName, out string methodName)
    {
        var method = frame.GetMethod();
        declaringTypeName = method?.DeclaringType?.FullName;
        methodName = method?.Name;
        return method != null;
    }
#endif
}
