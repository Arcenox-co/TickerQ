using System.Collections.Generic;

namespace TickerQ.SourceGenerator.Models
{
    /// <summary>
    /// Contains information about method parameters for TickerFunction methods.
    /// </summary>
    internal class MethodParameterInfo
    {
        public bool UsesGenericContext { get; set; }
        public string GenericTypeName { get; set; } = string.Empty;
        public List<string> ParametersList { get; set; } = new List<string>();
    }
}
