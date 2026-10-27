using TAKAHATA.Domain.Common.Enum;
using TAKAHATA.Domain.MasterData;

namespace TAKAHATA.Contracts.Report
{
    public class PartProcessKPIResponse
    {
        public int MachineId { get; set; }
        public string? MachineName { get; set; }
        public string? MachineCode { get; set; }
        public int IndexId { get; set; }
        public string? IndexCode { get; set; }
        public int PartId { get; set; }
        public string PartCode { get; set; } = string.Empty;
        public int PartProcessId { get; set; }
        public int? ReferenceId { get; set; }
        public int Quantity { get; set; } = 1;
        public int ProcessedQuantity { get; set; }
        public DateOnly? ReportDate { get; set; }
        public bool IsMultiDay { get; set; }
        public double? DailyMachineTotalTime { get; set; }
        public double? WorkingTime { get; set; }
        public double? SetupTime { get; set; }
        public double? WaitingTime { get; set; }
        public double? ProgrammingTime { get; set; }
        public double? TotalTime { get; set; }
        public DateOnly? PlantDate { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double? EstimatedTime { get; set; }
        public PartType PartType { get; set; }
        public PartNewRepair? NewRepair { get; set; }
        public ProcessStatus Status { get; set; } = ProcessStatus.Planned;
    }
}
