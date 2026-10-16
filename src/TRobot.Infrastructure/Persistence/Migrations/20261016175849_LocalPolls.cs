using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocalPolls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_OutboundMessages_EventId_Id",
                table: "OutboundMessages",
                columns: new[] { "EventId", "Id" });

            migrationBuilder.CreateTable(
                name: "PollOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PollId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PollOptions", x => x.Id);
                    table.UniqueConstraint("AK_PollOptions_EventId_PollId_Id", x => new { x.EventId, x.PollId, x.Id });
                    table.CheckConstraint("CK_PollOptions_Position", "Position BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_PollOptions_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Polls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PublicationMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResultMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecisionOptionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Polls", x => x.Id);
                    table.UniqueConstraint("AK_Polls_EventId_Id", x => new { x.EventId, x.Id });
                    table.CheckConstraint("CK_Polls_Closed", "(Status = 2 AND ClosedAtUtc IS NOT NULL) OR (Status <> 2 AND ClosedAtUtc IS NULL)");
                    table.CheckConstraint("CK_Polls_Publication", "Status NOT IN (1,2) OR PublicationMessageId IS NOT NULL");
                    table.CheckConstraint("CK_Polls_Result", "(ResultMessageId IS NULL AND DecisionOptionId IS NULL) OR Status = 2");
                    table.CheckConstraint("CK_Polls_Status", "Status IN (0,1,2,3)");
                    table.ForeignKey(
                        name: "FK_Polls_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Polls_OutboundMessages_EventId_PublicationMessageId",
                        columns: x => new { x.EventId, x.PublicationMessageId },
                        principalTable: "OutboundMessages",
                        principalColumns: new[] { "EventId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Polls_OutboundMessages_EventId_ResultMessageId",
                        columns: x => new { x.EventId, x.ResultMessageId },
                        principalTable: "OutboundMessages",
                        principalColumns: new[] { "EventId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Polls_PollOptions_EventId_Id_DecisionOptionId",
                        columns: x => new { x.EventId, x.Id, x.DecisionOptionId },
                        principalTable: "PollOptions",
                        principalColumns: new[] { "EventId", "PollId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PollVoteReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PollId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PollVoteReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PollVoteReceipts_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PollVoteReceipts_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PollVoteReceipts_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PollVoteReceipts_PollOptions_EventId_PollId_OptionId",
                        columns: x => new { x.EventId, x.PollId, x.OptionId },
                        principalTable: "PollOptions",
                        principalColumns: new[] { "EventId", "PollId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PollVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PollId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PollVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PollVotes_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PollVotes_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PollVotes_PollOptions_EventId_PollId_OptionId",
                        columns: x => new { x.EventId, x.PollId, x.OptionId },
                        principalTable: "PollOptions",
                        principalColumns: new[] { "EventId", "PollId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PollOptions_EventId",
                table: "PollOptions",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_PollOptions_EventId_PollId_Position",
                table: "PollOptions",
                columns: new[] { "EventId", "PollId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Polls_EventId",
                table: "Polls",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Polls_EventId_Id_DecisionOptionId",
                table: "Polls",
                columns: new[] { "EventId", "Id", "DecisionOptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Polls_EventId_PublicationMessageId",
                table: "Polls",
                columns: new[] { "EventId", "PublicationMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Polls_EventId_ResultMessageId",
                table: "Polls",
                columns: new[] { "EventId", "ResultMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_PollVoteReceipts_ActorId",
                table: "PollVoteReceipts",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_PollVoteReceipts_EventId",
                table: "PollVoteReceipts",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_PollVoteReceipts_EventId_PersonId",
                table: "PollVoteReceipts",
                columns: new[] { "EventId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_PollVoteReceipts_EventId_PollId_OptionId",
                table: "PollVoteReceipts",
                columns: new[] { "EventId", "PollId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PollVotes_EventId",
                table: "PollVotes",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_PollVotes_EventId_PersonId",
                table: "PollVotes",
                columns: new[] { "EventId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_PollVotes_EventId_PollId_OptionId",
                table: "PollVotes",
                columns: new[] { "EventId", "PollId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PollVotes_EventId_PollId_PersonId",
                table: "PollVotes",
                columns: new[] { "EventId", "PollId", "PersonId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PollOptions_Polls_EventId_PollId",
                table: "PollOptions",
                columns: new[] { "EventId", "PollId" },
                principalTable: "Polls",
                principalColumns: new[] { "EventId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PollOptions_Polls_EventId_PollId",
                table: "PollOptions");

            migrationBuilder.DropTable(
                name: "PollVoteReceipts");

            migrationBuilder.DropTable(
                name: "PollVotes");

            migrationBuilder.DropTable(
                name: "Polls");

            migrationBuilder.DropTable(
                name: "PollOptions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OutboundMessages_EventId_Id",
                table: "OutboundMessages");
        }
    }
}
