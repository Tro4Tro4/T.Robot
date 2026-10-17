using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceDialoguesAndStays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceDialogues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ConnectionRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Step = table.Column<int>(type: "INTEGER", nullable: false),
                    Arrival = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Days = table.Column<int>(type: "INTEGER", nullable: true),
                    HouseholdId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SnapshotHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDialogues", x => new { x.EventId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceDialogues_EventParticipants_EventId_Id",
                        columns: x => new { x.EventId, x.Id },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceDialogues_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceDialogues_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceStayChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SenderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreviousArrival = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PreviousDays = table.Column<int>(type: "INTEGER", nullable: true),
                    PreviousStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Arrival = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Days = table.Column<int>(type: "INTEGER", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceStayChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceStayChanges_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceStayChanges_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceStayChanges_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceStayChanges_People_SenderId",
                        column: x => x.SenderId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceStays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Arrival = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Days = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceStays", x => new { x.EventId, x.Id });
                    table.CheckConstraint("CK_AttendanceStays_Days", "Days BETWEEN 1 AND 31");
                    table.ForeignKey(
                        name: "FK_AttendanceStays_EventParticipants_EventId_Id",
                        columns: x => new { x.EventId, x.Id },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceStays_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDialogues_ActorId",
                table: "AttendanceDialogues",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDialogues_EventId",
                table: "AttendanceDialogues",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceStayChanges_ActorId",
                table: "AttendanceStayChanges",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceStayChanges_EventId",
                table: "AttendanceStayChanges",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceStayChanges_EventId_PersonId",
                table: "AttendanceStayChanges",
                columns: new[] { "EventId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceStayChanges_SenderId",
                table: "AttendanceStayChanges",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceStays_EventId",
                table: "AttendanceStays",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceDialogues");

            migrationBuilder.DropTable(
                name: "AttendanceStayChanges");

            migrationBuilder.DropTable(
                name: "AttendanceStays");
        }
    }
}
