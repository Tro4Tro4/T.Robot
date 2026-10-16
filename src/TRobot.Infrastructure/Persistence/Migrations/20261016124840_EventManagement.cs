using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Events",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "EventAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventAudits", x => x.Id);
                    table.CheckConstraint("CK_EventAudits_Action", "Action IN ('EventCreated', 'EventUpdated', 'ParticipantAdded')");
                    table.CheckConstraint("CK_EventAudits_Person", "(Action = 'ParticipantAdded' AND PersonId IS NOT NULL) OR (Action <> 'ParticipantAdded' AND PersonId IS NULL)");
                    table.ForeignKey(
                        name: "FK_EventAudits_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventAudits_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventAudits_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventAudits_ActorId",
                table: "EventAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_EventAudits_EventId_OccurredAtUtc",
                table: "EventAudits",
                columns: new[] { "EventId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventAudits_PersonId",
                table: "EventAudits",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventAudits");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Events");
        }
    }
}
