using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizerAccessLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AccountAudits_Action",
                table: "AccountAudits");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "EventOrganizers",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "AccountAudits",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountAudits_EventId",
                table: "AccountAudits",
                column: "EventId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccountAudits_Action",
                table: "AccountAudits",
                sql: "Action IN ('AdministratorInitialized', 'OrganizerCreated', 'PasswordChanged', 'EventGranted', 'EventRevoked', 'AccountEnabled', 'AccountDisabled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccountAudits_Event",
                table: "AccountAudits",
                sql: "Action NOT IN ('EventGranted', 'EventRevoked') OR EventId IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountAudits_Events_EventId",
                table: "AccountAudits",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccountAudits_Events_EventId",
                table: "AccountAudits");

            migrationBuilder.DropIndex(
                name: "IX_AccountAudits_EventId",
                table: "AccountAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccountAudits_Action",
                table: "AccountAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccountAudits_Event",
                table: "AccountAudits");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "EventOrganizers");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "AccountAudits");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccountAudits_Action",
                table: "AccountAudits",
                sql: "Action IN ('AdministratorInitialized', 'OrganizerCreated', 'PasswordChanged')");
        }
    }
}
