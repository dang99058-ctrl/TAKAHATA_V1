using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Pomelo.EntityFrameworkCore.MySql.Metadata;
using TAKAHATA.Infrastructure.Persistence;

#nullable disable

namespace TAKAHATA.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261026143000_TrackRepeatedDailyCutovers")]
    public partial class TrackRepeatedDailyCutovers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PendingFinalPackets",
                table: "MachineDailyTimeSummaries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ProcessTimesReset",
                table: "MachineDailyTimeSummaries",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE `MachineDailyTimeSummaries` " +
                "SET `PendingFinalPackets` = CASE WHEN `AwaitingFinalPacket` THEN 1 ELSE 0 END");

            migrationBuilder.DropColumn(
                name: "AwaitingFinalPacket",
                table: "MachineDailyTimeSummaries");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AwaitingFinalPacket",
                table: "MachineDailyTimeSummaries",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE `MachineDailyTimeSummaries` " +
                "SET `AwaitingFinalPacket` = `PendingFinalPackets` > 0");

            migrationBuilder.DropColumn(
                name: "PendingFinalPackets",
                table: "MachineDailyTimeSummaries");

            migrationBuilder.DropColumn(
                name: "ProcessTimesReset",
                table: "MachineDailyTimeSummaries");
        }
    }
}
