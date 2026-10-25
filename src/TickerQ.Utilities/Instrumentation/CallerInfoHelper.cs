using System.Diagnostics;
using System.IO;
using TickerQ.Utilities.Compat;

namespace TickerQ.Utilities.Instrumentation;

internal static class CallerInfoHelper
{
  /// <summary>
  /// Gets caller information by analyzing the stack trace
  /// </summary>
  /// <param name="skipFrames">Number of frames to skip from the current method</param>
  /// <returns>Formatted caller information</returns>
  public static string GetCallerInfo(int skipFrames = 4)
  {
    try
    {
      var stackTrace = new StackTrace(true);
      var frame = stackTrace.GetFrame(skipFrames);

      if (frame != null)
      {
        var fileName = frame.GetFileName();
        var lineNumber = frame.GetFileLineNumber();

        if (StackFrameCompat.TryDescribe(frame, out var declaringTypeName, out var methodName))
        {
          var className = declaringTypeName ?? "Unknown";

          // Filter out compiler-generated methods
          if (methodName.Contains("<") || methodName.Contains(">"))
          {
            // Try to get the next frame for async methods
            var nextFrame = stackTrace.GetFrame(skipFrames + 1);
            if (nextFrame != null && StackFrameCompat.TryDescribe(nextFrame, out var nextType, out var nextName))
            {
              className = nextType ?? className;
              methodName = nextName;
              fileName = nextFrame.GetFileName() ?? fileName;
              lineNumber = nextFrame.GetFileLineNumber();
            }
          }

          if (!string.IsNullOrEmpty(fileName))
          {
            var shortFileName = Path.GetFileName(fileName);
            return $"{className}.{methodName} ({shortFileName}:{lineNumber})";
          }

          return $"{className}.{methodName}";
        }
      }

      return "Unknown";
    }
    catch
    {
      return "Unknown";
    }
  }

  /// <summary>
  /// Gets a simple caller name without file information
  /// </summary>
  /// <param name="skipFrames">Number of frames to skip from the current method</param>
  /// <returns>Simple caller name</returns>
  public static string GetSimpleCallerInfo(int skipFrames = 4)
  {
    try
    {
      var stackTrace = new StackTrace(false);
      var frame = stackTrace.GetFrame(skipFrames);

      if (frame != null && StackFrameCompat.TryDescribe(frame, out var declaringTypeName, out var methodName))
      {
        var className = declaringTypeName ?? "Unknown";

        // Filter out compiler-generated methods
        if (methodName.Contains("<") || methodName.Contains(">"))
        {
          var nextFrame = stackTrace.GetFrame(skipFrames + 1);
          if (nextFrame != null && StackFrameCompat.TryDescribe(nextFrame, out var nextType, out var nextName))
          {
            className = nextType ?? className;
            methodName = nextName;
          }
        }

        return $"{className}.{methodName}";
      }

      return "Unknown";
    }
    catch
    {
      return "Unknown";
    }
  }
}
