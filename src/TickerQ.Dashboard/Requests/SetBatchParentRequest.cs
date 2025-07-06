using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Dashboard.Requests
{
    public class SetBatchParentRequest
    {
        public Guid TargetId { get; set; }
        public Guid ParentId { get; set; }
        public BatchRunCondition? BatchRunCondition { get; set; }
    }
}