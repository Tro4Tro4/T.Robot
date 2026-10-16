using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceSimulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "EventParticipants",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ParticipationReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizerPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PreviousStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipationReceipts", x => x.Id);
                    table.CheckConstraint("CK_ParticipationReceipts_MessageId", "length(trim(MessageId)) BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_ParticipationReceipts_PreviousStatus", "PreviousStatus IN ('NoResponse', 'Confirmed', 'Undecided', 'Absent')");
                    table.CheckConstraint("CK_ParticipationReceipts_Status", "Status IN ('NoResponse', 'Confirmed', 'Undecided', 'Absent')");
                    table.ForeignKey(
                        name: "FK_ParticipationReceipts_EventOrganizers_EventId_OrganizerPersonId",
                        columns: x => new { x.EventId, x.OrganizerPersonId },
                        principalTable: "EventOrganizers",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ParticipationReceipts_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParticipationReceipts_EventId_MessageId",
                table: "ParticipationReceipts",
                columns: new[] { "EventId", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParticipationReceipts_EventId_OrganizerPersonId",
                table: "ParticipationReceipts",
                columns: new[] { "EventId", "OrganizerPersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_ParticipationReceipts_EventId_PersonId",
                table: "ParticipationReceipts",
                columns: new[] { "EventId", "PersonId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParticipationReceipts");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "EventParticipants");
        }
    }
}
