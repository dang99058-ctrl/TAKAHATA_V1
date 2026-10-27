using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TAKAHATA.Domain.DataLog;

namespace TAKAHATA.Infrastructure.Persistence.Configurations
{
    public sealed class MachineDailyTimeSummaryConfiguration
        : IEntityTypeConfiguration<MachineDailyTimeSummary>
    {
        public void Configure(EntityTypeBuilder<MachineDailyTimeSummary> builder)
        {
            builder.HasIndex(summary => new { summary.MachineId, summary.ReportDate })
                .IsUnique();
        }
    }
}
