using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizerAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "ParticipationReceipts",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Simulation");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ParticipationReceipts_Origin",
                table: "ParticipationReceipts",
                sql: "Origin IN ('Simulation', 'Organizer')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ParticipationReceipts_Origin",
                table: "ParticipationReceipts");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "ParticipationReceipts");
        }
    }
}
