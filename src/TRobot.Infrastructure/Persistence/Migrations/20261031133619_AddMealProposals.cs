using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMealProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MealProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Meal = table.Column<int>(type: "INTEGER", nullable: false),
                    DishName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OffersToCook = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WithdrawnAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealProposals", x => x.Id);
                    table.CheckConstraint("CK_MealProposals_Meal", "Meal IN (0,1,2)");
                    table.ForeignKey(
                        name: "FK_MealProposals_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealProposals_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MealProposalSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ConnectionRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    IdentityFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Step = table.Column<int>(type: "INTEGER", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Meal = table.Column<int>(type: "INTEGER", nullable: true),
                    DishName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OffersToCook = table.Column<bool>(type: "INTEGER", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealProposalSessions", x => new { x.EventId, x.Id });
                    table.CheckConstraint("CK_MealProposalSessions_Meal", "Meal IS NULL OR Meal IN (0,1,2)");
                    table.CheckConstraint("CK_MealProposalSessions_Step", "Step BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_MealProposalSessions_EventParticipants_EventId_Id",
                        columns: x => new { x.EventId, x.Id },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealProposalSessions_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealProposalSessions_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealProposals_EventId",
                table: "MealProposals",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_MealProposals_EventId_Day_Meal",
                table: "MealProposals",
                columns: new[] { "EventId", "Day", "Meal" });

            migrationBuilder.CreateIndex(
                name: "IX_MealProposals_EventId_PersonId",
                table: "MealProposals",
                columns: new[] { "EventId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_MealProposalSessions_ActorId",
                table: "MealProposalSessions",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_MealProposalSessions_EventId",
                table: "MealProposalSessions",
                column: "EventId");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealProposals");

            migrationBuilder.DropTable(
                name: "MealProposalSessions");
        }
    }
}
