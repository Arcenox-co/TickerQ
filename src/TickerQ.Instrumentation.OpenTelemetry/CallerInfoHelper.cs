using System;
using System.Diagnostics;
using System.IO;

namespace TickerQ.Instrumentation.OpenTelemetry
{
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
                    var method = frame.GetMethod();
                    var fileName = frame.GetFileName();
                    var lineNumber = frame.GetFileLineNumber();
                    
                    if (method != null)
                    {
                        var className = method.DeclaringType?.Name ?? "Unknown";
                        var methodName = method.Name;
                        
                        // Filter out compiler-generated methods
                        if (methodName.Contains("<") || methodName.Contains(">"))
                        {
                            // Try to get the next frame for async methods
                            var nextFrame = stackTrace.GetFrame(skipFrames + 1);
                            if (nextFrame?.GetMethod() is { } nextMethod)
                            {
                                className = nextMethod.DeclaringType?.Name ?? className;
                                methodName = nextMethod.Name;
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
                
                if (frame?.GetMethod() is { } method)
                {
                    var className = method.DeclaringType?.Name ?? "Unknown";
                    var methodName = method.Name;
                    
                    // Filter out compiler-generated methods
                    if (methodName.Contains("<") || methodName.Contains(">"))
                    {
                        var nextFrame = stackTrace.GetFrame(skipFrames + 1);
                        if (nextFrame?.GetMethod() is { } nextMethod)
                        {
                            className = nextMethod.DeclaringType?.Name ?? className;
                            methodName = nextMethod.Name;
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
}
