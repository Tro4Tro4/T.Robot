using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMealPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_MealProposals_EventId_Id",
                table: "MealProposals",
                columns: new[] { "EventId", "Id" });

            migrationBuilder.CreateTable(
                name: "MealPreferenceSessions",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ConnectionRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    IdentityFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PersonOptions = table.Column<string>(type: "TEXT", nullable: false),
                    CycleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CycleRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Meal = table.Column<int>(type: "INTEGER", nullable: true),
                    SelectedIds = table.Column<string>(type: "TEXT", nullable: false),
                    BallotRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    EligibilityFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Page = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewedPages = table.Column<string>(type: "TEXT", nullable: false),
                    Step = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealPreferenceSessions", x => new { x.EventId, x.Id });
                    table.CheckConstraint("CK_MealPreferenceSessions_Meal", "Meal IS NULL OR Meal IN (0,1,2)");
                    table.CheckConstraint("CK_MealPreferenceSessions_Page", "Page >= 1");
                    table.CheckConstraint("CK_MealPreferenceSessions_Step", "Step BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_MealPreferenceSessions_EventParticipants_EventId_Id",
                        columns: x => new { x.EventId, x.Id },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealPreferenceSessions_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealPreferenceSessions_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MealVotingCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedSnapshotJson = table.Column<string>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealVotingCycles", x => x.Id);
                    table.UniqueConstraint("AK_MealVotingCycles_EventId_Id", x => new { x.EventId, x.Id });
                    table.CheckConstraint("CK_MealVotingCycles_State", "State BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_MealVotingCycles_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MealPreferenceBallots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CycleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Meal = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectionJson = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealPreferenceBallots", x => x.Id);
                    table.CheckConstraint("CK_MealPreferenceBallots_Meal", "Meal IN (0,1,2)");
                    table.ForeignKey(
                        name: "FK_MealPreferenceBallots_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealPreferenceBallots_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealPreferenceBallots_MealVotingCycles_EventId_CycleId",
                        columns: x => new { x.EventId, x.CycleId },
                        principalTable: "MealVotingCycles",
                        principalColumns: new[] { "EventId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MealPreferenceChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CycleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Meal = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreviousJson = table.Column<string>(type: "TEXT", nullable: false),
                    SelectionJson = table.Column<string>(type: "TEXT", nullable: false),
                    BallotRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealPreferenceChanges", x => x.Id);
                    table.CheckConstraint("CK_MealPreferenceChanges_Meal", "Meal IN (0,1,2)");
                    table.ForeignKey(
                        name: "FK_MealPreferenceChanges_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealPreferenceChanges_EventParticipants_EventId_VoterId",
                        columns: x => new { x.EventId, x.VoterId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealPreferenceChanges_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealPreferenceChanges_MealVotingCycles_EventId_CycleId",
                        columns: x => new { x.EventId, x.CycleId },
                        principalTable: "MealVotingCycles",
                        principalColumns: new[] { "EventId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealPreferenceChanges_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MealVotingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CycleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Meal = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsExcluded = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealVotingEntries", x => x.Id);
                    table.UniqueConstraint("AK_MealVotingEntries_EventId_CycleId_Id", x => new { x.EventId, x.CycleId, x.Id });
                    table.CheckConstraint("CK_MealVotingEntries_Meal", "Meal IN (0,1,2)");
                    table.ForeignKey(
                        name: "FK_MealVotingEntries_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealVotingEntries_MealVotingCycles_EventId_CycleId",
                        columns: x => new { x.EventId, x.CycleId },
                        principalTable: "MealVotingCycles",
                        principalColumns: new[] { "EventId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MealVotingSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CycleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealVotingSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealVotingSources_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealVotingSources_MealProposals_EventId_ProposalId",
                        columns: x => new { x.EventId, x.ProposalId },
                        principalTable: "MealProposals",
                        principalColumns: new[] { "EventId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealVotingSources_MealVotingEntries_EventId_CycleId_EntryId",
                        columns: x => new { x.EventId, x.CycleId, x.EntryId },
                        principalTable: "MealVotingEntries",
                        principalColumns: new[] { "EventId", "CycleId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealPreferenceBallots_EventId",
                table: "MealPreferenceBallots",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_MealPreferenceBallots_EventId_CycleId_PersonId_Day_Meal",
                table: "MealPreferenceBallots",
                columns: new[] { "EventId", "CycleId", "PersonId", "Day", "Meal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealPreferenceBallots_EventId_PersonId",
                table: "MealPreferenceBallots",
                columns: new[] { "EventId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_MealPreferenceChanges_ActorId",
                table: "MealPreferenceChanges",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_MealPreferenceChanges_EventId",
                table: "MealPreferenceChanges",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_MealPreferenceChanges_EventId_CycleId",
                table: "MealPreferenceChanges",
                columns: new[] { "EventId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_MealPreferenceChanges_EventId_PersonId",
                table: "MealPreferenceChanges",
                columns: new[] { "EventId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_MealPreferenceChanges_EventId_VoterId",
                table: "MealPreferenceChanges",
                columns: new[] { "EventId", "VoterId" });

            migrationBuilder.CreateIndex(
                name: "IX_MealPreferenceSessions_ActorId",
                table: "MealPreferenceSessions",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_MealPreferenceSessions_EventId",
                table: "MealPreferenceSessions",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_MealVotingCycles_EventId",
                table: "MealVotingCycles",
                column: "EventId",
                unique: true,
                filter: "State <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_MealVotingCycles_EventId_Number",
                table: "MealVotingCycles",
                columns: new[] { "EventId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealVotingEntries_EventId",
                table: "MealVotingEntries",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_MealVotingSources_EventId",
                table: "MealVotingSources",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_MealVotingSources_EventId_CycleId_EntryId",
                table: "MealVotingSources",
                columns: new[] { "EventId", "CycleId", "EntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_MealVotingSources_EventId_CycleId_ProposalId",
                table: "MealVotingSources",
                columns: new[] { "EventId", "CycleId", "ProposalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealVotingSources_EventId_ProposalId",
                table: "MealVotingSources",
                columns: new[] { "EventId", "ProposalId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealPreferenceBallots");

            migrationBuilder.DropTable(
                name: "MealPreferenceChanges");

            migrationBuilder.DropTable(
                name: "MealPreferenceSessions");

            migrationBuilder.DropTable(
                name: "MealVotingSources");

            migrationBuilder.DropTable(
                name: "MealVotingEntries");

            migrationBuilder.DropTable(
                name: "MealVotingCycles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MealProposals_EventId_Id",
                table: "MealProposals");
        }
    }
}
