using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace TickerQ.Utilities.Infrastructure;

/// <summary>
/// Compiles expression trees to IL when dynamic code is available and uses the expression
/// interpreter under Native AOT. This preserves the managed/JIT fast path without making
/// provider query execution depend on runtime code generation.
/// </summary>
public static class ExpressionCompiler
{
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Compile() is reached only when RuntimeFeature confirms dynamic code support; Native AOT uses the interpreter.")]
    public static TDelegate CompileAotSafe<TDelegate>(this Expression<TDelegate> expression)
        where TDelegate : Delegate
    {
        ArgumentNullException.ThrowIfNull(expression);
        return RuntimeFeature.IsDynamicCodeSupported
            ? expression.Compile()
            : expression.Compile(preferInterpretation: true);
    }
}
