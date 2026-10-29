using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TAKAHATA.Infrastructure.Persistence;

#nullable disable

namespace TAKAHATA.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261029170000_DeferProcessEndTimeForPlcDrain")]
    public partial class DeferProcessEndTimeForPlcDrain : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PendingEndTime",
                table: "PartProcesses",
                type: "datetime(6)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingEndTime",
                table: "PartProcesses");
        }
    }
}
