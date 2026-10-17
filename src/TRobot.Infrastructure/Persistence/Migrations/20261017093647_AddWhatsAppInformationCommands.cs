using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppInformationCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppCommandReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CommandHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppCommandReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppCommandReceipts_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppCommandReceipts_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppCommandReceipts_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppListApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    GroupId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ConnectionRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppListApprovals", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppListApprovals_Kind", "Kind IN (0,1)");
                    table.ForeignKey(
                        name: "FK_WhatsAppListApprovals_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppListApprovals_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppCommandReceipts_ActorId",
                table: "WhatsAppCommandReceipts",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppCommandReceipts_EventId",
                table: "WhatsAppCommandReceipts",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppCommandReceipts_EventId_MessageHash",
                table: "WhatsAppCommandReceipts",
                columns: new[] { "EventId", "MessageHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppCommandReceipts_EventId_PersonId",
                table: "WhatsAppCommandReceipts",
                columns: new[] { "EventId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppListApprovals_ActorId",
                table: "WhatsAppListApprovals",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppListApprovals_EventId",
                table: "WhatsAppListApprovals",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppListApprovals_EventId_Kind",
                table: "WhatsAppListApprovals",
                columns: new[] { "EventId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppCommandReceipts");

            migrationBuilder.DropTable(
                name: "WhatsAppListApprovals");
        }
    }
}
