using System;

namespace TickerQ.Utilities.Extensions
{
    internal static class DelegateExtension
    {
        public static TickerProviderOptions InvokeProviderOptions(this Action<TickerProviderOptions> action)
        {
            var options = new TickerProviderOptions();
            action?.Invoke(options);
            return options;
        }
    }
}