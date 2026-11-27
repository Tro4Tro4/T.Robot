using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceStayModeAndQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "QuestionId",
                table: "AttendanceDialogues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AskStayDates",
                table: "AttendanceCollectionSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "EventDays",
                table: "AttendanceCollectionSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "AttendanceQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceQuestions_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceQuestionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceQuestionOptions_AttendanceQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "AttendanceQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceQuestionOptions_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SenderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnsweredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceAnswers_AttendanceQuestionOptions_OptionId",
                        column: x => x.OptionId,
                        principalTable: "AttendanceQuestionOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceAnswers_AttendanceQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "AttendanceQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceAnswers_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceAnswers_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceAnswers_People_SenderId",
                        column: x => x.SenderId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAnswers_EventId",
                table: "AttendanceAnswers",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAnswers_EventId_PersonId_QuestionId",
                table: "AttendanceAnswers",
                columns: new[] { "EventId", "PersonId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAnswers_OptionId",
                table: "AttendanceAnswers",
                column: "OptionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAnswers_QuestionId",
                table: "AttendanceAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAnswers_SenderId",
                table: "AttendanceAnswers",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceQuestionOptions_EventId",
                table: "AttendanceQuestionOptions",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceQuestionOptions_QuestionId_Position",
                table: "AttendanceQuestionOptions",
                columns: new[] { "QuestionId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceQuestions_EventId",
                table: "AttendanceQuestions",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceQuestions_EventId_Position",
                table: "AttendanceQuestions",
                columns: new[] { "EventId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceAnswers");

            migrationBuilder.DropTable(
                name: "AttendanceQuestionOptions");

            migrationBuilder.DropTable(
                name: "AttendanceQuestions");

            migrationBuilder.DropColumn(
                name: "QuestionId",
                table: "AttendanceDialogues");

            migrationBuilder.DropColumn(
                name: "AskStayDates",
                table: "AttendanceCollectionSettings");

            migrationBuilder.DropColumn(
                name: "EventDays",
                table: "AttendanceCollectionSettings");
        }
    }
}
