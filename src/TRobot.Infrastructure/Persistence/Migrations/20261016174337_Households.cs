using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Households : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Households",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Households", x => x.Id);
                    table.UniqueConstraint("AK_Households_EventId_Id", x => new { x.EventId, x.Id });
                    table.ForeignKey(
                        name: "FK_Households_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    CompanionOfMemberId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdMembers", x => x.Id);
                    table.UniqueConstraint("AK_HouseholdMembers_EventId_HouseholdId_Id", x => new { x.EventId, x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_HouseholdMembers_Kind", "Kind IN (0,1)");
                    table.CheckConstraint("CK_HouseholdMembers_NotSelf", "CompanionOfMemberId IS NULL OR CompanionOfMemberId <> Id");
                    table.ForeignKey(
                        name: "FK_HouseholdMembers_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseholdMembers_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseholdMembers_HouseholdMembers_EventId_HouseholdId_CompanionOfMemberId",
                        columns: x => new { x.EventId, x.HouseholdId, x.CompanionOfMemberId },
                        principalTable: "HouseholdMembers",
                        principalColumns: new[] { "EventId", "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseholdMembers_Households_EventId_HouseholdId",
                        columns: x => new { x.EventId, x.HouseholdId },
                        principalTable: "Households",
                        principalColumns: new[] { "EventId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdMembers_EventId",
                table: "HouseholdMembers",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdMembers_EventId_HouseholdId_CompanionOfMemberId",
                table: "HouseholdMembers",
                columns: new[] { "EventId", "HouseholdId", "CompanionOfMemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdMembers_EventId_PersonId",
                table: "HouseholdMembers",
                columns: new[] { "EventId", "PersonId" },
                unique: true,
                filter: "\"IsActive\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Households_EventId",
                table: "Households",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HouseholdMembers");

            migrationBuilder.DropTable(
                name: "Households");
        }
    }
}
