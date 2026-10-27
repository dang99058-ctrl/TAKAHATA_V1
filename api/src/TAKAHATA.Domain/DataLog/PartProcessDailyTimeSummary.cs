using TAKAHATA.Domain.Common;

namespace TAKAHATA.Domain.DataLog
{
    public class PartProcessDailyTimeSummary : Entity
    {
        public int MachineId { get; set; }
        public int PartProcessId { get; set; }
        public DateOnly ReportDate { get; set; }
        public double WorkingTime { get; set; }
        public double WaitingTime { get; set; }
        public double SetupTime { get; set; }
    }
}
