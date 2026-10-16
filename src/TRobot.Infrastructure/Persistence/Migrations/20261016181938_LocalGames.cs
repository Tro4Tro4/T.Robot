using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocalGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameMemberships_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameMemberships_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Quizzes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CorrectPosition = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PublicationMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quizzes", x => x.Id);
                    table.UniqueConstraint("AK_Quizzes_EventId_Id", x => new { x.EventId, x.Id });
                    table.CheckConstraint("CK_Quizzes_Closed", "(Status = 2 AND ClosedAtUtc IS NOT NULL) OR (Status <> 2 AND ClosedAtUtc IS NULL)");
                    table.CheckConstraint("CK_Quizzes_Correct", "CorrectPosition BETWEEN 1 AND 10");
                    table.CheckConstraint("CK_Quizzes_Publication", "Status NOT IN (1,2) OR PublicationMessageId IS NOT NULL");
                    table.CheckConstraint("CK_Quizzes_Status", "Status IN (0,1,2,3)");
                    table.ForeignKey(
                        name: "FK_Quizzes_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Quizzes_OutboundMessages_EventId_PublicationMessageId",
                        columns: x => new { x.EventId, x.PublicationMessageId },
                        principalTable: "OutboundMessages",
                        principalColumns: new[] { "EventId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuizOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuizId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizOptions", x => x.Id);
                    table.UniqueConstraint("AK_QuizOptions_EventId_QuizId_Id", x => new { x.EventId, x.QuizId, x.Id });
                    table.CheckConstraint("CK_QuizOptions_Position", "Position BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_QuizOptions_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizOptions_Quizzes_EventId_QuizId",
                        columns: x => new { x.EventId, x.QuizId },
                        principalTable: "Quizzes",
                        principalColumns: new[] { "EventId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuizAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuizId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizAnswers_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizAnswers_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizAnswers_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizAnswers_QuizOptions_EventId_QuizId_OptionId",
                        columns: x => new { x.EventId, x.QuizId, x.OptionId },
                        principalTable: "QuizOptions",
                        principalColumns: new[] { "EventId", "QuizId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameMemberships_EventId",
                table: "GameMemberships",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_GameMemberships_EventId_PersonId",
                table: "GameMemberships",
                columns: new[] { "EventId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_ActorId",
                table: "QuizAnswers",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_EventId",
                table: "QuizAnswers",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_EventId_PersonId",
                table: "QuizAnswers",
                columns: new[] { "EventId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_EventId_QuizId_OptionId",
                table: "QuizAnswers",
                columns: new[] { "EventId", "QuizId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_EventId_QuizId_PersonId",
                table: "QuizAnswers",
                columns: new[] { "EventId", "QuizId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizOptions_EventId",
                table: "QuizOptions",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizOptions_EventId_QuizId_Position",
                table: "QuizOptions",
                columns: new[] { "EventId", "QuizId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quizzes_EventId",
                table: "Quizzes",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Quizzes_EventId_PublicationMessageId",
                table: "Quizzes",
                columns: new[] { "EventId", "PublicationMessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameMemberships");

            migrationBuilder.DropTable(
                name: "QuizAnswers");

            migrationBuilder.DropTable(
                name: "QuizOptions");

            migrationBuilder.DropTable(
                name: "Quizzes");
        }
    }
}
