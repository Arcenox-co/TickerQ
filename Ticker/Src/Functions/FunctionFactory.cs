using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NCrontab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Functions.Delegates;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Functions
{
    internal class FunctionFactory
    {
        public static (IDictionary<string, (TickerFunctionDelegate, TickerTaskPriority)>, IDictionary<string, string>) CollectFunctions(IConfiguration configuration, Assembly[] assemblies)
        {
            var methods = assemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => !type.IsAbstract &&
                               !type.IsInterface &&
                               !type.IsSealed &&
                                type.IsPublic &&
                                type.IsSubclassOf(typeof(TickerController)))
                .SelectMany(type => type.GetMethods())
                .Select(method => new
                {
                    Attribute = method.GetCustomAttribute<TickerFunctionAttribute>(),
                    Delegate = (TickerFunctionDelegate)(async (serviceProvider, tickerId, tickerType, baseCancellationToken, isDue) =>
                    {
                        using (var scope = serviceProvider.CreateScope())
                        {
                            var cancellationTokenSouce = baseCancellationToken != default
                               ? CancellationTokenSource.CreateLinkedTokenSource(baseCancellationToken)
                               : new CancellationTokenSource();

                            var parameters = method.GetParameters().ToArray();

                            var instance = Unsafe.As<TickerController>(ActivatorUtilities.CreateInstance(scope.ServiceProvider, method.DeclaringType));

                            var functionContext = CreateFunctionContext(instance, parameters, tickerId, tickerType, cancellationTokenSouce, isDue);

                            instance.ServiceProvider = scope.ServiceProvider;

                            var methodParameters = ParamsToInclude(parameters, functionContext, cancellationTokenSouce.Token);

                            try
                            {
                                var result = method.Invoke(instance, methodParameters);

                                if (IsReturnType<Task>(method))
                                    await Unsafe.As<Task>(result);

                                else if (IsReturnType<ValueTask>(method))
                                    await ((ValueTask)result).AsTask();

                                if (tickerId != Guid.Empty)
                                    await TickerHelper.SetTickerFinalStatus(scope, tickerId, tickerType, isDue ? TickerStatus.DueDone : TickerStatus.Done);
                            }
                            catch (Exception e)
                            {
                                if (tickerId != Guid.Empty)
                                    await TickerHelper.SetTickerFinalStatus(scope, tickerId, tickerType, TickerStatus.Failed);

                                var exceptionHandler = scope.ServiceProvider.GetService<ITickerExceptionHandler>();

                                await exceptionHandler?.HandleExceptionAsync(e, tickerId, tickerType);
                            }
                            finally
                            {
                                cancellationTokenSouce.Dispose();
                            }
                        };
                    })
                })
                .Where(x => x.Attribute != null)
                .ToList();

            var functionsCronExpression = methods
                .Where(method => !string.IsNullOrWhiteSpace(method.Attribute?.FunctionName) && !string.IsNullOrWhiteSpace(method.Attribute?.CronExpression))
                .ToDictionary(method => method.Attribute.FunctionName, method => ParseCronExpression(configuration, method.Attribute.CronExpression, method.Attribute.FunctionName));

            var functionsDelegate = methods
                .Where(method => !string.IsNullOrWhiteSpace(method.Attribute?.FunctionName))
                .ToDictionary(method => method.Attribute.FunctionName, method => (method.Delegate, method.Attribute.TaskPriority));

            return (functionsDelegate, functionsCronExpression);
        }

        private static object CreateFunctionContext(TickerController tickerController, ParameterInfo[] parameters, Guid tickerId, TickerType tickerType, CancellationTokenSource cancellationTokenSouce, bool isDue)
        {
            var tickerFunctionContext = parameters
                                        .Select(x => x.ParameterType)
                                        .FirstOrDefault(x =>
                                                x.IsGenericType && typeof(TickerFunctionContext<>).IsAssignableFrom(x.GetGenericTypeDefinition()) ||
                                                typeof(TickerFunctionContext) == x);

            if (tickerFunctionContext == default)
                return default;


            if (tickerFunctionContext.IsGenericType)
                return BuildGenericContext(tickerController, tickerId, tickerType, cancellationTokenSouce, tickerFunctionContext, isDue);

            return new TickerFunctionContext
            {
                CancellationTokenSource = cancellationTokenSouce,
                TickerId = tickerId
            };
        }

        private static object BuildGenericContext(TickerController tickerController, Guid tickerId, TickerType tickerType, CancellationTokenSource cancellationTokenSouce, Type tickerFunctionContext, bool isDue)
        {
            var genericType = tickerFunctionContext.GetGenericArguments()[0];

            var closedGenericType = typeof(TickerFunctionContext<>).MakeGenericType(genericType);

            var functionIdProp = closedGenericType.GetProperty(nameof(TickerFunctionContext.TickerId));

            var requestIdProp = closedGenericType.GetProperty(nameof(TickerFunctionContext<object>.Request));

            var isDueProp = closedGenericType.GetProperty(nameof(TickerFunctionContext<object>.IsDue));

            var tickerTypeProp = closedGenericType.GetProperty(nameof(TickerFunctionContext<object>.TickerType));

            var cancellationTokenSourceProp = closedGenericType.GetProperty(nameof(TickerFunctionContext.CancellationTokenSource));

            var tickerFunctionContextInstance = Activator.CreateInstance(closedGenericType);

            var instance = CreateLazyExpression(tickerController, genericType, new object[] { tickerId, tickerType });

            functionIdProp.SetValue(tickerFunctionContextInstance, tickerId);

            requestIdProp.SetValue(tickerFunctionContextInstance, instance);

            isDueProp.SetValue(tickerFunctionContextInstance, isDue);

            tickerTypeProp.SetValue(tickerFunctionContextInstance, tickerType);

            cancellationTokenSourceProp.SetValue(tickerFunctionContextInstance, cancellationTokenSouce);

            return tickerFunctionContextInstance;
        }

        private static object CreateLazyExpression(TickerController tickerController, Type genericType, object[] parameters)
        {
            var lazyType = typeof(Lazy<>).MakeGenericType(typeof(Task<>).MakeGenericType(genericType));

            var methodInfo = typeof(TickerController).GetMethod(nameof(TickerController.GetRequestValueAsync), BindingFlags.Public | BindingFlags.Instance);

            var genericMethodInfo = methodInfo.MakeGenericMethod(genericType);

            // Create an expression that calls the GetValueFactoryAsync<T> method and returns the result as an object
            var callMethodExpr = Expression.Call(Expression.Constant(tickerController), genericMethodInfo, parameters.Select(p => Expression.Constant(p)).ToArray());

            var castToObjectExpr = Expression.Convert(callMethodExpr, typeof(Task<>).MakeGenericType(genericType));

            // Create a lambda expression that returns the result of calling the method as an object
            var valueFactory = Expression.Lambda(castToObjectExpr).Compile();

            // Create a new instance of Lazy<Task<object>> with the lambda expression as the initialization logic
            return Activator.CreateInstance(lazyType, valueFactory);
        }

        private static string ParseCronExpression(IConfiguration configuration, string expression, string functionName)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new Exception("Cannot parse empty expression!");

            else if (!string.IsNullOrWhiteSpace(expression) && expression.StartsWith('%') && expression.EndsWith('%'))
                expression = configuration[expression.Trim('%')];

            if (CrontabSchedule.TryParse(expression) == default)
                throw new Exception($"Cannot parse expression [{expression}] at TickerFunction: {functionName}");

            return expression;
        }

        private static object[] ParamsToInclude(IEnumerable<ParameterInfo> parameters, object functionContext, CancellationToken cancellationToken)
            => parameters.Select(p => (p.ParameterType == typeof(TickerFunctionContext) || (p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(TickerFunctionContext<>))) ? functionContext :
                                (p.ParameterType == typeof(CancellationToken) ? cancellationToken :
                                default(object)))
                         .ToArray();

        private static bool IsReturnType<T>(MethodInfo method)
            => (method.ReturnType == typeof(T) || method.ReturnType.BaseType == typeof(T));
    }
}
