using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLodgingPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LodgingApartments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Directions = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LodgingApartments", x => x.Id);
                    table.UniqueConstraint("AK_LodgingApartments_EventId_Id", x => new { x.EventId, x.Id });
                    table.ForeignKey(
                        name: "FK_LodgingApartments_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LodgingPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    FinalizedFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LodgingPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LodgingPlans_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LodgingRooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApartmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LodgingRooms", x => x.Id);
                    table.UniqueConstraint("AK_LodgingRooms_EventId_Id", x => new { x.EventId, x.Id });
                    table.CheckConstraint("CK_LodgingRooms_Capacity", "Capacity BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "FK_LodgingRooms_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LodgingRooms_LodgingApartments_EventId_ApartmentId",
                        columns: x => new { x.EventId, x.ApartmentId },
                        principalTable: "LodgingApartments",
                        principalColumns: new[] { "EventId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LodgingAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoomId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LodgingAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LodgingAssignments_EventParticipants_EventId_PersonId",
                        columns: x => new { x.EventId, x.PersonId },
                        principalTable: "EventParticipants",
                        principalColumns: new[] { "EventId", "PersonId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LodgingAssignments_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LodgingAssignments_LodgingRooms_EventId_RoomId",
                        columns: x => new { x.EventId, x.RoomId },
                        principalTable: "LodgingRooms",
                        principalColumns: new[] { "EventId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LodgingApartments_EventId",
                table: "LodgingApartments",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_LodgingAssignments_EventId",
                table: "LodgingAssignments",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_LodgingAssignments_EventId_PersonId",
                table: "LodgingAssignments",
                columns: new[] { "EventId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LodgingAssignments_EventId_RoomId",
                table: "LodgingAssignments",
                columns: new[] { "EventId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_LodgingPlans_EventId",
                table: "LodgingPlans",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LodgingRooms_EventId",
                table: "LodgingRooms",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_LodgingRooms_EventId_ApartmentId",
                table: "LodgingRooms",
                columns: new[] { "EventId", "ApartmentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LodgingAssignments");

            migrationBuilder.DropTable(
                name: "LodgingPlans");

            migrationBuilder.DropTable(
                name: "LodgingRooms");

            migrationBuilder.DropTable(
                name: "LodgingApartments");
        }
    }
}
