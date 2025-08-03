using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities
{
    public delegate Task TickerFunctionDelegate(CancellationToken cancellationToken, IServiceProvider serviceProvider,
        TickerFunctionContext context);

    public static class TickerFunctionProvider
    {
        internal static IReadOnlyDictionary<string, (string, Type)> TickerFunctionRequestTypes { get; private set; }

        public static IReadOnlyDictionary<string, (string cronExpression, TickerTaskPriority Priority,
                TickerFunctionDelegate Delegate)>
            TickerFunctions
        { get; private set; }

        public static void RegisterFunctions(
            IDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)> functions)
        {
            TickerFunctions =
                new ReadOnlyDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>(functions);
        }

        public static void RegisterRequestType(IDictionary<string, (string, Type)> requestTypes)
        {
            TickerFunctionRequestTypes = new ReadOnlyDictionary<string, (string, Type)>(requestTypes);
        }

        internal static void MapCronExpressionsFromIConfigurations(IDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)> functions)
        {
            TickerFunctions =
                new ReadOnlyDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>(functions);
        }

        /// <summary>
        /// Registers functions from the specified assemblies by scanning for methods marked with TickerFunctionAttribute.
        /// This method provides runtime assembly scanning functionality for service discovery.
        /// </summary>
        /// <param name="assemblies">The assemblies to scan for TickerFunction methods</param>
        public static void RegisterFunctionsFromAssemblies(Assembly[] assemblies)
        {
            if (assemblies == null || assemblies.Length == 0)
                return;

            var discoveredFunctions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>();
            var discoveredRequestTypes = new Dictionary<string, (string, Type)>();

            // Merge existing functions if any
            if (TickerFunctions != null)
            {
                foreach (var kvp in TickerFunctions)
                {
                    discoveredFunctions[kvp.Key] = kvp.Value;
                }
            }

            // Merge existing request types if any
            if (TickerFunctionRequestTypes != null)
            {
                foreach (var kvp in TickerFunctionRequestTypes)
                {
                    discoveredRequestTypes[kvp.Key] = kvp.Value;
                }
            }

            foreach (var assembly in assemblies)
            {
                try
                {
                    ScanAssemblyForTickerFunctions(assembly, discoveredFunctions, discoveredRequestTypes);
                }
                catch (Exception)
                {
                    // Log error if needed, but continue with other assemblies
                    // For now, silently continue to avoid breaking the application
                    continue;
                }
            }

            // Update the registered functions and request types
            TickerFunctions = new ReadOnlyDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>(discoveredFunctions);
            TickerFunctionRequestTypes = new ReadOnlyDictionary<string, (string, Type)>(discoveredRequestTypes);
        }

        /// <summary>
        /// Scans a single assembly for methods marked with TickerFunctionAttribute and adds them to the collections.
        /// </summary>
        /// <param name="assembly">The assembly to scan</param>
        /// <param name="functions">The collection to add discovered functions to</param>
        /// <param name="requestTypes">The collection to add discovered request types to</param>
        private static void ScanAssemblyForTickerFunctions(
            Assembly assembly,
            Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)> functions,
            Dictionary<string, (string, Type)> requestTypes)
        {
            try
            {
                var types = assembly.GetTypes();

                foreach (var type in types)
                {
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

                    foreach (var method in methods)
                    {
                        var tickerAttribute = method.GetCustomAttribute<TickerFunctionAttribute>();
                        if (tickerAttribute == null)
                            continue;

                        // Extract attribute values from properties
                        var functionName = tickerAttribute.FunctionName;
                        var cronExpression = tickerAttribute.CronExpression ?? string.Empty;
                        var taskPriority = tickerAttribute.TaskPriority;

                        if (string.IsNullOrEmpty(functionName))
                            continue;

                        // Create a delegate for the method
                        var tickerDelegate = CreateTickerFunctionDelegate(method, type);
                        if (tickerDelegate != null)
                        {
                            functions[functionName] = (cronExpression, taskPriority, tickerDelegate);

                            // Check for request type parameter
                            var requestType = GetRequestTypeFromMethod(method);
                            if (requestType != null)
                            {
                                requestTypes[functionName] = (requestType.FullName, requestType);
                            }
                        }
                    }
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // Skip assemblies that can't be loaded
                return;
            }
        }



        /// <summary>
        /// Creates a TickerFunctionDelegate from a MethodInfo.
        /// </summary>
        /// <param name="method">The method to create a delegate for</param>
        /// <param name="declaringType">The type that declares the method</param>
        /// <returns>A TickerFunctionDelegate or null if the method signature is invalid</returns>
        private static TickerFunctionDelegate CreateTickerFunctionDelegate(MethodInfo method, Type declaringType)
        {
            try
            {
                // Validate method signature
                var parameters = method.GetParameters();
                var isValidSignature = ValidateMethodSignature(method, parameters);

                if (!isValidSignature)
                    return null;

                // Create the delegate
                return async (cancellationToken, serviceProvider, context) =>
                {
                    object instance = null;

                    // Create instance if method is not static
                    if (!method.IsStatic)
                    {
                        try
                        {
                            instance = serviceProvider.GetService(declaringType) ?? Activator.CreateInstance(declaringType);
                        }
                        catch
                        {
                            // If we can't create an instance, skip this method
                            return;
                        }
                    }

                    // Prepare method arguments based on signature
                    var args = PrepareMethodArguments(method, parameters, cancellationToken, serviceProvider, context);

                    // Invoke the method
                    var result = method.Invoke(instance, args);

                    // Handle async methods
                    if (result is Task task)
                    {
                        await task;
                    }
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Validates that a method has a compatible signature for TickerFunction.
        /// </summary>
        /// <param name="method">The method to validate</param>
        /// <param name="parameters">The method parameters</param>
        /// <returns>True if the signature is valid</returns>
        private static bool ValidateMethodSignature(MethodInfo method, ParameterInfo[] parameters)
        {
            // Method should return void or Task
            var returnType = method.ReturnType;
            if (returnType != typeof(void) && returnType != typeof(Task) && !returnType.IsSubclassOf(typeof(Task)))
                return false;

            // Check parameter types - should be compatible with TickerFunction signature
            // Common patterns: (), (CancellationToken), (TickerFunctionContext), (CancellationToken, TickerFunctionContext), etc.
            foreach (var param in parameters)
            {
                var paramType = param.ParameterType;
                if (paramType != typeof(CancellationToken) &&
                    paramType != typeof(IServiceProvider) &&
                    paramType != typeof(TickerFunctionContext) &&
                    !paramType.IsGenericType)
                {
                    // Allow other types for flexibility, but they should be resolvable from DI
                    continue;
                }
            }

            return true;
        }

        /// <summary>
        /// Prepares method arguments based on the method signature.
        /// </summary>
        /// <param name="method">The method to prepare arguments for</param>
        /// <param name="parameters">The method parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <param name="serviceProvider">The service provider</param>
        /// <param name="context">The ticker function context</param>
        /// <returns>An array of arguments for the method</returns>
        private static object[] PrepareMethodArguments(MethodInfo method, ParameterInfo[] parameters,
            CancellationToken cancellationToken, IServiceProvider serviceProvider, TickerFunctionContext context)
        {
            var args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;

                if (paramType == typeof(CancellationToken))
                {
                    args[i] = cancellationToken;
                }
                else if (paramType == typeof(IServiceProvider))
                {
                    args[i] = serviceProvider;
                }
                else if (paramType == typeof(TickerFunctionContext))
                {
                    args[i] = context;
                }
                else
                {
                    // Try to resolve from service provider
                    try
                    {
                        args[i] = serviceProvider.GetService(paramType);
                    }
                    catch
                    {
                        args[i] = paramType.IsValueType ? Activator.CreateInstance(paramType) : null;
                    }
                }
            }

            return args;
        }

        /// <summary>
        /// Extracts the request type from a method's generic TickerFunctionContext parameter.
        /// </summary>
        /// <param name="method">The method to analyze</param>
        /// <returns>The request type if found, otherwise null</returns>
        private static Type GetRequestTypeFromMethod(MethodInfo method)
        {
            var parameters = method.GetParameters();

            foreach (var param in parameters)
            {
                var paramType = param.ParameterType;

                // Check if it's a generic TickerFunctionContext<T>
                if (paramType.IsGenericType && paramType.GetGenericTypeDefinition().Name.StartsWith("TickerFunctionContext"))
                {
                    var genericArgs = paramType.GetGenericArguments();
                    if (genericArgs.Length > 0)
                    {
                        return genericArgs[0];
                    }
                }
            }

            return null;
        }
    }

    public static class TickerRequestProvider
    {
        public static async Task<T> GetRequestAsync<T>(IServiceProvider serviceProvider, Guid tickerId,
            TickerType tickerType)
        {
            var internalTickerManager = serviceProvider.GetService<IInternalTickerManager>();
            return await internalTickerManager.GetRequestAsync<T>(tickerId, tickerType);
        }
    }
}