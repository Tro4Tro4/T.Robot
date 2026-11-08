using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceFamilyStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FamilyStatus",
                table: "AttendanceDialogues",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FamilyStatus",
                table: "AttendanceDialogues");
        }
    }
}
