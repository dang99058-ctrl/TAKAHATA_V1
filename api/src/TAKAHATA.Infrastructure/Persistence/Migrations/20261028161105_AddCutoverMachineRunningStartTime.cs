using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TAKAHATA.Infrastructure.Persistence.Migrations
{
    public partial class AddCutoverMachineRunningStartTime : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WasRunningAtCutover",
                table: "MachineDailyTimeSummaries",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstRunningAt",
                table: "MachineDailyTimeSummaries",
                type: "datetime(6)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WasRunningAtCutover",
                table: "MachineDailyTimeSummaries");

            migrationBuilder.DropColumn(
                name: "FirstRunningAt",
                table: "MachineDailyTimeSummaries");
        }
    }
}
