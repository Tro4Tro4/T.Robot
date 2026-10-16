using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocalAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    ChildWeight = table.Column<decimal>(type: "TEXT", precision: 5, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingSettings_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdvancedByPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeclaredByActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.CheckConstraint("CK_Expenses_Status", "Status IN (0,1,2,3)");
                    table.ForeignKey(
                        name: "FK_Expenses_EventParticipants_EventId_AdvancedByPersonId",
                        columns: x => new { x.EventId, x.AdvancedByPersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_People_DeclaredByActorId",
                        column: x => x.DeclaredByActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ParticipantWeights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Weight = table.Column<decimal>(type: "TEXT", precision: 5, scale: 3, nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipantWeights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParticipantWeights_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ParticipantWeights_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeclaredByActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_DistinctPeople", "FromPersonId <> ToPersonId");
                    table.CheckConstraint("CK_Payments_Status", "Status IN (0,1,2)");
                    table.ForeignKey(
                        name: "FK_Payments_EventParticipants_EventId_FromPersonId",
                        columns: x => new { x.EventId, x.FromPersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_EventParticipants_EventId_ToPersonId",
                        columns: x => new { x.EventId, x.ToPersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_People_DeclaredByActorId",
                        column: x => x.DeclaredByActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingSettings_EventId",
                table: "AccountingSettings",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_DeclaredByActorId",
                table: "Expenses",
                column: "DeclaredByActorId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_EventId",
                table: "Expenses",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_EventId_AdvancedByPersonId",
                table: "Expenses",
                columns: new[] { "EventId", "AdvancedByPersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantWeights_EventId",
                table: "ParticipantWeights",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantWeights_EventId_PersonId",
                table: "ParticipantWeights",
                columns: new[] { "EventId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_DeclaredByActorId",
                table: "Payments",
                column: "DeclaredByActorId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_EventId",
                table: "Payments",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_EventId_FromPersonId",
                table: "Payments",
                columns: new[] { "EventId", "FromPersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_EventId_ToPersonId",
                table: "Payments",
                columns: new[] { "EventId", "ToPersonId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountingSettings");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "ParticipantWeights");

            migrationBuilder.DropTable(
                name: "Payments");
        }
    }
}
