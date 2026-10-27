using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Pomelo.EntityFrameworkCore.MySql.Metadata;
using TAKAHATA.Infrastructure.Persistence;

#nullable disable

namespace TAKAHATA.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261026160000_AddPartProcessDailyTimeSummaries")]
    public partial class AddPartProcessDailyTimeSummaries : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartProcessDailyTimeSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation(
                            "MySql:ValueGenerationStrategy",
                            MySqlValueGenerationStrategy.IdentityColumn),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    PartProcessId = table.Column<int>(type: "int", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    WorkingTime = table.Column<double>(type: "double", nullable: false),
                    WaitingTime = table.Column<double>(type: "double", nullable: false),
                    SetupTime = table.Column<double>(type: "double", nullable: false),
                    CreateAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreateBy = table.Column<string>(type: "longtext", nullable: false),
                    UpdateAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdateBy = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartProcessDailyTimeSummaries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PartProcessDailyTimeSummaries_MachineId_PartProcessId_ReportDate",
                table: "PartProcessDailyTimeSummaries",
                columns: new[] { "MachineId", "PartProcessId", "ReportDate" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartProcessDailyTimeSummaries");
        }
    }
}
