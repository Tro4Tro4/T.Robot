using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticLocalScheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AutomaticActorId",
                table: "BotSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutomaticEnabled",
                table: "BotSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_BotSettings_AutomaticActorId",
                table: "BotSettings",
                column: "AutomaticActorId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BotSettings_AutomaticActor",
                table: "BotSettings",
                sql: "AutomaticEnabled = 0 OR AutomaticActorId IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_BotSettings_People_AutomaticActorId",
                table: "BotSettings",
                column: "AutomaticActorId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BotSettings_People_AutomaticActorId",
                table: "BotSettings");

            migrationBuilder.DropIndex(
                name: "IX_BotSettings_AutomaticActorId",
                table: "BotSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BotSettings_AutomaticActor",
                table: "BotSettings");

            migrationBuilder.DropColumn(
                name: "AutomaticActorId",
                table: "BotSettings");

            migrationBuilder.DropColumn(
                name: "AutomaticEnabled",
                table: "BotSettings");
        }
    }
}
