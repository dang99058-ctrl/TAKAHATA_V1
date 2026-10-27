using TAKAHATA.Domain.Common;

namespace TAKAHATA.Domain.DataLog
{
    public class MachineDailyTimeSummary : Entity
    {
        public int MachineId { get; set; }
        public DateOnly ReportDate { get; set; }
        public double WorkingTime { get; set; }
        public double WaitingTime { get; set; }
        public double SetupTime { get; set; }
        public int PendingFinalPackets { get; set; }
        public bool CutoverCommandSent { get; set; }
        public bool ProcessTimesReset { get; set; }
    }
}
