using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TAKAHATA.Domain.DataLog;

namespace TAKAHATA.Infrastructure.Persistence.Configurations
{
    public sealed class PartProcessDailyTimeSummaryConfiguration
        : IEntityTypeConfiguration<PartProcessDailyTimeSummary>
    {
        public void Configure(EntityTypeBuilder<PartProcessDailyTimeSummary> builder)
        {
            builder.HasIndex(summary => new
                {
                    summary.MachineId,
                    summary.PartProcessId,
                    summary.ReportDate
                })
                .IsUnique();
        }
    }
}
