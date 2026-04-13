namespace TickerQ.SourceGenerator.Models
{
    internal sealed class ConstructorParamModel
    {
        public string ParamName { get; set; }
        public string TypeFullName { get; set; }
        public bool IsKeyed { get; set; }
        public string ServiceKey { get; set; }
    }
}
