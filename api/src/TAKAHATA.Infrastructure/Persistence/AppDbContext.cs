using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TAKAHATA.Domain.DataLog;
using TAKAHATA.Domain.MasterData;
using TAKAHATA.Domain.User;
using Index = TAKAHATA.Domain.MasterData.Index;

namespace TAKAHATA.Infrastructure.Persistence
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        public DbSet<Machine> Machines { get; set; }
        public DbSet<Index> Indexes { get; set; }
        public DbSet<Part> Parts { get; set; }
        public DbSet<PartProcess> PartProcesses { get; set; }
        public DbSet<MachineMaintenanceProcess> MachineMaintenanceProcesses { get; set; }
        public DbSet<MachineDailyTimeSummary> MachineDailyTimeSummaries { get; set; }
        public DbSet<PartProcessDailyTimeSummary> PartProcessDailyTimeSummaries { get; set; }
        public DbSet<SystemConfig> SystemConfigs { get; set; }
        public DbSet<UserMachinePermission> UserMachinePermissions { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options)
           : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
            base.OnModelCreating(builder);
            // Customize the identity model if needed
        }
    }
}
