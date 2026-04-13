using TickerQ.SourceGenerator.Models;

namespace TickerQ.SourceGenerator.Analysis
{
    internal static class InterfaceMethodAnalyzer
    {
        internal static TickerMethodModel Analyze(TickerFunctionInterfaceInfo info)
        {
            var globalClassName = Global(info.ClassFullName);
            var globalRequestType = !string.IsNullOrEmpty(info.RequestTypeFullName)
                ? Global(info.RequestTypeFullName)
                : null;

            return new TickerMethodModel
            {
                FunctionName = info.ClassName,
                CronExpression = null,
                TaskPriority = 2, // TickerTaskPriority.Normal
                MaxConcurrency = 0,
                MethodName = "ExecuteAsync",
                ClassName = info.ClassName,
                ClassFullName = globalClassName,
                IsStatic = false,
                IsAsync = true,
                UsesGenericContext = info.HasGenericRequest,
                GenericRequestTypeFullName = globalRequestType,
                GenericRequestTypeName = info.HasGenericRequest
                    ? info.RequestTypeFullName.Contains(".")
                        ? info.RequestTypeFullName.Substring(info.RequestTypeFullName.LastIndexOf('.') + 1)
                        : info.RequestTypeFullName
                    : null,
                HasContext = true,
                HasCancellationToken = true,
                IsInterfaceBased = true
            };
        }

        private static string Global(string fullName) => MethodAnalyzer.Global(fullName);
    }
}
