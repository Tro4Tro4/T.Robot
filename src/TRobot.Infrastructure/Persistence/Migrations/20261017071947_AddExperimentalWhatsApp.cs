using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExperimentalWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppConnections", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppConnections_Active", "Enabled = 0 OR (GroupId IS NOT NULL AND ActorId IS NOT NULL AND ActivatedAtUtc IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_WhatsAppConnections_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppConnections_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppIdentityAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SenderHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppIdentityAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppIdentityAliases_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppIdentityAliases_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppIncomingReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreviousStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppIncomingReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppIncomingReceipts_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppIncomingReceipts_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppIncomingReceipts_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppLiveMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ConnectionRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OutcomeAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppLiveMessages", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppLiveMessages_Status", "Status IN (0,1,2,3)");
                    table.ForeignKey(
                        name: "FK_WhatsAppLiveMessages_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppLiveMessages_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConnections_ActorId",
                table: "WhatsAppConnections",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConnections_Enabled",
                table: "WhatsAppConnections",
                column: "Enabled",
                unique: true,
                filter: "Enabled = 1");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConnections_EventId",
                table: "WhatsAppConnections",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppIdentityAliases_EventId",
                table: "WhatsAppIdentityAliases",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppIdentityAliases_EventId_PersonId",
                table: "WhatsAppIdentityAliases",
                columns: new[] { "EventId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppIdentityAliases_EventId_SenderHash",
                table: "WhatsAppIdentityAliases",
                columns: new[] { "EventId", "SenderHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppIncomingReceipts_ActorId",
                table: "WhatsAppIncomingReceipts",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppIncomingReceipts_EventId",
                table: "WhatsAppIncomingReceipts",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppIncomingReceipts_EventId_MessageHash",
                table: "WhatsAppIncomingReceipts",
                columns: new[] { "EventId", "MessageHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppIncomingReceipts_EventId_PersonId",
                table: "WhatsAppIncomingReceipts",
                columns: new[] { "EventId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppLiveMessages_ActorId",
                table: "WhatsAppLiveMessages",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppLiveMessages_EventId",
                table: "WhatsAppLiveMessages",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppConnections");

            migrationBuilder.DropTable(
                name: "WhatsAppIdentityAliases");

            migrationBuilder.DropTable(
                name: "WhatsAppIncomingReceipts");

            migrationBuilder.DropTable(
                name: "WhatsAppLiveMessages");
        }
    }
}
