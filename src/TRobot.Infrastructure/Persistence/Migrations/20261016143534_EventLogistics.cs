using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventLogistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Events",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Program",
                table: "Events",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_Location",
                table: "Events",
                sql: "Location IS NULL OR length(trim(Location)) BETWEEN 1 AND 300");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_Program",
                table: "Events",
                sql: "Program IS NULL OR length(trim(Program)) BETWEEN 1 AND 4000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_Location",
                table: "Events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_Program",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Program",
                table: "Events");
        }
    }
}
