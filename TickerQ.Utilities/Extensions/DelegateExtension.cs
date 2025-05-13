using System;

namespace TickerQ.Utilities.Extensions
{
    internal static class DelegateExtension
    {
        public static ProviderOptions InvokeProviderOptions(this Action<ProviderOptions> action)
        {
            var options = new ProviderOptions();
            action?.Invoke(options);
            return options;
        }
    }
}