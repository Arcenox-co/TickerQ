using System.Collections.Generic;
using System.Linq;
using System.Text;
using TickerQ.SourceGenerator.Models;

namespace TickerQ.SourceGenerator.Generation
{
    internal static class FactoryGenerator
    {
        internal static string Generate(
            string rootNamespace,
            List<TickerMethodModel> methods,
            List<ConstructorModel> constructors)
        {
            var delegateRegistrations = new StringBuilder();

            foreach (var method in methods)
            {
                delegateRegistrations.AppendLine(BuildDelegateRegistration(method, constructors));
            }

            var constructorMethods = new StringBuilder();
            foreach (var ctor in constructors)
            {
                constructorMethods.AppendLine(BuildConstructorMethod(ctor));
            }

            var requestTypes = BuildRequestTypeRegistrations(methods);

            return Templates.InstanceFactory
                .Replace("{{NAMESPACE}}", rootNamespace)
                .Replace("{{METHOD_COUNT}}", methods.Count.ToString())
                .Replace("{{DELEGATE_REGISTRATIONS}}", delegateRegistrations.ToString().TrimEnd())
                .Replace("{{CONSTRUCTOR_METHODS}}", constructorMethods.ToString().TrimEnd())
                .Replace("{{REQUEST_TYPE_REGISTRATIONS}}", requestTypes);
        }

        private static string BuildDelegateRegistration(TickerMethodModel method, List<ConstructorModel> constructors)
        {
            var cronExpr = string.IsNullOrEmpty(method.CronExpression)
                ? "string.Empty"
                : $"\"{method.CronExpression}\"";

            var isAsync = method.IsAsync || method.UsesGenericContext;
            var asyncKeyword = isAsync ? "async " : "";

            var body = BuildDelegateBody(method, constructors);

            return Templates.DelegateRegistration
                .Replace("{{FUNCTION_NAME}}", method.FunctionName)
                .Replace("{{CRON_EXPRESSION}}", cronExpr)
                .Replace("{{PRIORITY}}", method.TaskPriority.ToString())
                .Replace("{{MAX_CONCURRENCY}}", method.MaxConcurrency.ToString())
                .Replace("{{ASYNC_KEYWORD}}", asyncKeyword)
                .Replace("{{DELEGATE_BODY}}", body);
        }

        private static string BuildDelegateBody(TickerMethodModel method, List<ConstructorModel> constructors)
        {
            var callTarget = method.IsStatic
                ? method.ClassFullName
                : constructors.FirstOrDefault(c => c.ClassFullName == method.ClassFullName)?.FactoryMethodName + "(serviceProvider)";

            if (callTarget == null)
                callTarget = $"new {method.ClassFullName}()";

            var args = BuildMethodArgs(method);
            var methodCall = $"{callTarget}.{method.MethodName}({args})";

            if (method.UsesGenericContext)
            {
                return Templates.DelegateBodyAsyncGenericContext
                    .Replace("{{GENERIC_TYPE}}", method.GenericRequestTypeFullName)
                    .Replace("{{METHOD_CALL}}", methodCall);
            }

            if (method.IsAsync)
            {
                return Templates.DelegateBodyAsync
                    .Replace("{{METHOD_CALL}}", methodCall);
            }

            return Templates.DelegateBodySync
                .Replace("{{METHOD_CALL}}", methodCall);
        }

        private static string BuildMethodArgs(TickerMethodModel method)
        {
            var args = new List<string>();

            if (method.UsesGenericContext)
                args.Add("genericContext");
            else if (method.HasContext)
                args.Add("context");

            if (method.HasCancellationToken)
                args.Add("cancellationToken");

            return string.Join(", ", args);
        }

        private static string BuildConstructorMethod(ConstructorModel ctor)
        {
            if (ctor.Parameters.Count == 0)
            {
                return Templates.ParameterlessConstructorMethod
                    .Replace("{{CLASS_NAME}}", ctor.ClassFullName)
                    .Replace("{{FACTORY_METHOD_NAME}}", ctor.FactoryMethodName);
            }

            var resolutions = new StringBuilder();
            var argNames = new List<string>();

            foreach (var param in ctor.Parameters)
            {
                var line = param.IsKeyed
                    ? Templates.KeyedServiceResolution
                        .Replace("{{PARAM_NAME}}", param.ParamName)
                        .Replace("{{SERVICE_TYPE}}", param.TypeFullName)
                        .Replace("{{SERVICE_KEY}}", param.ServiceKey)
                    : Templates.ServiceResolution
                        .Replace("{{PARAM_NAME}}", param.ParamName)
                        .Replace("{{SERVICE_TYPE}}", param.TypeFullName);

                resolutions.AppendLine(line);
                argNames.Add(param.ParamName);
            }

            return Templates.ConstructorMethod
                .Replace("{{CLASS_NAME}}", ctor.ClassFullName)
                .Replace("{{FACTORY_METHOD_NAME}}", ctor.FactoryMethodName)
                .Replace("{{SERVICE_RESOLUTIONS}}", resolutions.ToString().TrimEnd())
                .Replace("{{CONSTRUCTOR_ARGS}}", string.Join(", ", argNames));
        }

        private static string BuildRequestTypeRegistrations(List<TickerMethodModel> methods)
        {
            var genericMethods = methods.Where(m => m.UsesGenericContext).ToList();
            if (genericMethods.Count == 0)
                return string.Empty;

            var entries = new StringBuilder();
            foreach (var m in genericMethods)
            {
                entries.AppendLine(Templates.RequestTypeEntry
                    .Replace("{{FUNCTION_NAME}}", m.FunctionName)
                    .Replace("{{REQUEST_TYPE}}", m.GenericRequestTypeFullName));
            }

            return Templates.RequestTypeRegistration
                .Replace("{{COUNT}}", genericMethods.Count.ToString())
                .Replace("{{ENTRIES}}", entries.ToString().TrimEnd());
        }

    }
}
