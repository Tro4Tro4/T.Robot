using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimulatedOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BotSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Paused = table.Column<bool>(type: "INTEGER", nullable: false),
                    QuietStartHour = table.Column<int>(type: "INTEGER", nullable: false),
                    QuietEndHour = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxSpontaneousPerDay = table.Column<int>(type: "INTEGER", nullable: false),
                    Sarcasm = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSummaryAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSummaryFingerprint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotSettings", x => x.Id);
                    table.CheckConstraint("CK_BotSettings_Ranges", "QuietStartHour BETWEEN 0 AND 23 AND QuietEndHour BETWEEN 0 AND 23 AND MaxSpontaneousPerDay BETWEEN 0 AND 3 AND Sarcasm BETWEEN 0 AND 10");
                    table.ForeignKey(
                        name: "FK_BotSettings_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboundMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SummaryFingerprint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundMessages", x => x.Id);
                    table.CheckConstraint("CK_OutboundMessages_Kind", "Kind IN (0,1,2)");
                    table.CheckConstraint("CK_OutboundMessages_Outcome", "(Status = 2 AND SentAtUtc IS NOT NULL) OR (Status <> 2 AND SentAtUtc IS NULL)");
                    table.CheckConstraint("CK_OutboundMessages_Status", "Status IN (0,1,2,3)");
                    table.ForeignKey(
                        name: "FK_OutboundMessages_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BotSettings_EventId",
                table: "BotSettings",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessages_EventId",
                table: "OutboundMessages",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BotSettings");

            migrationBuilder.DropTable(
                name: "OutboundMessages");
        }
    }
}
