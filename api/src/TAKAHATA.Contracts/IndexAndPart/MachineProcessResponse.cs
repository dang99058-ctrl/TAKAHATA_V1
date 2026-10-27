using TAKAHATA.Contracts.Common;
using TAKAHATA.Domain.Common.Enum;
using TAKAHATA.Domain.MasterData;

namespace TAKAHATA.Contracts.IndexAndPart
{
    public class MachineProcessResponse : Response
    {
        public string IndexCode { get; set; } = string.Empty;
        public int PartId { get; set; }
        public string PartCode { get; set; } = string.Empty;
        public DateOnly? PlantDate { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double? WorkingTime { get; set; }
        public int Quantity { get; set; }
        public ProcessStatus Status { get; set; }
        public string? Note { get; set; }
        public MachineProcessType Type { get; set; }
        public bool Prioritize { get; set; } = false;
        public bool IsClosed { get; set; }
        public double? EstimatedTime { get; set; }
        public PartType? PartType { get; set; }
        public int ProcessedQuantity { get; set; }
        public int TotalProcessedQuantity { get; set; }

        public bool IsOverdue { get; set; }
        public bool IsCutoverCompleted { get; set; }
        public int? ReferenceId { get; set; }
        public int DonePreviouslyQuantity { get; set; }
    }
}
