using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MenuPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MenuItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ResponsiblePersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Availability = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItems", x => x.Id);
                    table.CheckConstraint("CK_MenuItems_Availability", "Availability IN (0, 1, 2, 3)");
                    table.CheckConstraint("CK_MenuItems_Category", "Category IN (0, 1, 2)");
                    table.CheckConstraint("CK_MenuItems_Responsible", "Availability <> 1 OR ResponsiblePersonId IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_MenuItems_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MenuItems_People_ResponsiblePersonId",
                        column: x => x.ResponsiblePersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_EventId",
                table: "MenuItems",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_ResponsiblePersonId",
                table: "MenuItems",
                column: "ResponsiblePersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MenuItems");
        }
    }
}
