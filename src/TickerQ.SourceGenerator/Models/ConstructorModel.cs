using System.Collections.Generic;

namespace TickerQ.SourceGenerator.Models
{
    internal sealed class ConstructorModel
    {
        public string ClassName { get; set; }
        public string ClassFullName { get; set; }
        public string FactoryMethodName { get; set; }
        public List<ConstructorParamModel> Parameters { get; set; } = new List<ConstructorParamModel>();
    }
}
