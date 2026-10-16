using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocalAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Module = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocalAudits_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LocalAudits_People_ActorId",
                        column: x => x.ActorId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AssigneeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationTasks", x => x.Id);
                    table.CheckConstraint("CK_OrganizationTasks_Priority", "Priority IN (0,1,2)");
                    table.CheckConstraint("CK_OrganizationTasks_Status", "Status IN (0,1,2,3)");
                    table.ForeignKey(
                        name: "FK_OrganizationTasks_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationTasks_People_AssigneeId",
                        column: x => x.AssigneeId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocalAudits_ActorId",
                table: "LocalAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_LocalAudits_EventId",
                table: "LocalAudits",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationTasks_AssigneeId",
                table: "OrganizationTasks",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationTasks_EventId",
                table: "OrganizationTasks",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocalAudits");

            migrationBuilder.DropTable(
                name: "OrganizationTasks");
        }
    }
}
