using TAKAHATA.Contracts.Common;
using TAKAHATA.Domain.MasterData;

namespace TAKAHATA.Contracts.IndexAndPart
{
    public class ManufacturingProcessResponse : Response
    {
        public int IndexId { get; set; }
        public string IndexCode { get; set; } = string.Empty;
        public int PartId { get; set; }
        public string PartCode { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int ProcessedQuantity { get; set; }
        public int TotalProcessedQuantity { get; set; }
        public int? ReferenceId { get; set; }
        public ProcessStatus Status { get; set; }
        public string Note { get; set; } = string.Empty;
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double? EstimatedTime { get; set; }
        public double? SetupTime { get; set; }
        public double? WaitingTime { get; set; }
        public double? WorkingTime { get; set; }
        public double? TotalTime { get; set; }
        public string? PerformedBy { get; set; } = string.Empty;
        public string WorkerNote { get; set; } = string.Empty;
        public double? ProgrammingTime { get; set; }
        public bool Prioritize { get; set; } = false;
        public bool IsOverdue { get; set; } = false;
        public bool IsCutoverCompleted { get; set; }
        public bool IsSettingUp { get; set; }
        public ManufacturingProcessType ProcessType { get; set; }
    }
}
