namespace TAKAHATA.Domain.MasterData
{
    public class PartProcess : Process
    {
        public int PartId { get; set; }
        public int Sequence { get; set; }
        public DateTime? PendingEndTime { get; set; }
        public double? WorkingTime { get; set; }
        public double? SetupTime { get; set; }
        public double? WaitingTime { get; set; }
        public double? ProgrammingTime { get; set; }
        public double? AssemblyTime { get; set; }
        public double? MeasurementTime { get; set; }
        public int ProcessedQuantity { get; set; }
        public Part? Part { get; set; }
        public string WorkerNote { get; set; } = string.Empty;
        public bool IsOverdue { get; set; } = false;
        public int? ReferenceId { get; set; }
        public bool IsSettingUp { get; set; }
    }
}
