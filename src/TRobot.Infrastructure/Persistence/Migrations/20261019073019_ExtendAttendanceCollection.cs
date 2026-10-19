using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendAttendanceCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InitialSnapshotHash",
                table: "AttendanceDialogues",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEdit",
                table: "AttendanceDialogues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PreserveDays",
                table: "AttendanceDialogues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AttendanceCollectionSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceCollectionSettings", x => x.Id);
                    table.CheckConstraint("CK_AttendanceCollectionSettings_Event", "Id = EventId");
                    table.ForeignKey(
                        name: "FK_AttendanceCollectionSettings_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCollectionSettings_EventId",
                table: "AttendanceCollectionSettings",
                column: "EventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceCollectionSettings");

            migrationBuilder.DropColumn(
                name: "InitialSnapshotHash",
                table: "AttendanceDialogues");

            migrationBuilder.DropColumn(
                name: "IsEdit",
                table: "AttendanceDialogues");

            migrationBuilder.DropColumn(
                name: "PreserveDays",
                table: "AttendanceDialogues");
        }
    }
}
