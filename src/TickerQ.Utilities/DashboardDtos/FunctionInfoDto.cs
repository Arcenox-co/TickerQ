using System.Collections.Generic;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public class FunctionInfoDto
    {
        public string FunctionName { get; set; }

        /// <summary>Canonical request-contract version. Always positive, including request-less functions.</summary>
        public int ContractVersion { get; set; }

        /// <summary>Canonical request contract, or null when the function accepts no request payload.</summary>
        public FunctionRequestContractDto RequestContract { get; set; }

        /// <summary>Legacy mirror of <see cref="RequestContract"/> type name.</summary>
        public string RequestType { get; set; }

        /// <summary>Legacy mirror of the first canonical example value.</summary>
        public string RequestExample { get; set; }

        public TickerTaskPriority Priority { get; set; }
        public string CronExpression { get; set; }
    }

    public class FunctionRequestContractDto
    {
        public string TypeName { get; set; }
        public string MediaType { get; set; }
        public bool Required { get; set; }
        public string SchemaDialect { get; set; }
        public string SchemaJson { get; set; }
        public string Fingerprint { get; set; }
        public IList<FunctionRequestExampleDto> Examples { get; set; } = new List<FunctionRequestExampleDto>();
    }

    public class FunctionRequestExampleDto
    {
        public string Key { get; set; }
        public string Summary { get; set; }
        public string ValueJson { get; set; }
    }
}
