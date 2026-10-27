using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Pomelo.EntityFrameworkCore.MySql.Metadata;
using TAKAHATA.Infrastructure.Persistence;

#nullable disable

namespace TAKAHATA.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261026120000_AddMachineDailyTimeSummaries")]
    public partial class AddMachineDailyTimeSummaries : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MachineDailyTimeSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation(
                            "MySql:ValueGenerationStrategy",
                            MySqlValueGenerationStrategy.IdentityColumn),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    WorkingTime = table.Column<double>(type: "double", nullable: false),
                    WaitingTime = table.Column<double>(type: "double", nullable: false),
                    SetupTime = table.Column<double>(type: "double", nullable: false),
                    AwaitingFinalPacket = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CutoverCommandSent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreateAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreateBy = table.Column<string>(type: "longtext", nullable: false),
                    UpdateAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdateBy = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineDailyTimeSummaries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MachineDailyTimeSummaries_MachineId_ReportDate",
                table: "MachineDailyTimeSummaries",
                columns: new[] { "MachineId", "ReportDate" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MachineDailyTimeSummaries");
        }
    }
}
