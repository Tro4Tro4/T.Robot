using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MessageDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboundMessages_Status",
                table: "OutboundMessages");

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "OutboundMessages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAtUtc",
                table: "OutboundMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedByActorId",
                table: "OutboundMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "OutboundMessages",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "DeliveryMode",
                table: "OutboundMessages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAtUtc",
                table: "OutboundMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "OutboundMessages",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutcomeNote",
                table: "OutboundMessages",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetGroupId",
                table: "OutboundMessages",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "OutboundMessages",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Backfill the new timestamps from data that already existed. DeliveryMode stays 0 (simulated):
            // every pre-existing row, including the historical Queued ones, remains excluded from real sends.
            migrationBuilder.Sql("UPDATE OutboundMessages SET CreatedAtUtc = ScheduledAtUtc, " +
                "UpdatedAtUtc = CASE WHEN SentAtUtc IS NOT NULL THEN SentAtUtc ELSE ScheduledAtUtc END;");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessages_ConfirmedByActorId",
                table: "OutboundMessages",
                column: "ConfirmedByActorId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessages_DeliveryMode_Status_ScheduledAtUtc",
                table: "OutboundMessages",
                columns: new[] { "DeliveryMode", "Status", "ScheduledAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboundMessages_Attempts",
                table: "OutboundMessages",
                sql: "Attempts >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboundMessages_Confirmation",
                table: "OutboundMessages",
                sql: "(ConfirmedAtUtc IS NULL AND ConfirmedByActorId IS NULL AND TargetGroupId IS NULL) OR (ConfirmedAtUtc IS NOT NULL AND ConfirmedByActorId IS NOT NULL AND TargetGroupId IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboundMessages_Mode",
                table: "OutboundMessages",
                sql: "DeliveryMode IN (0,1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboundMessages_Status",
                table: "OutboundMessages",
                sql: "Status IN (0,1,2,3,4,5,6)");

            migrationBuilder.AddForeignKey(
                name: "FK_OutboundMessages_People_ConfirmedByActorId",
                table: "OutboundMessages",
                column: "ConfirmedByActorId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous version only understands Draft/Queued/Sent/Cancelled.
            migrationBuilder.Sql("UPDATE OutboundMessages SET Status = 1 WHERE Status = 4");
            migrationBuilder.Sql("UPDATE OutboundMessages SET Status = 3 WHERE Status IN (5, 6)");

            migrationBuilder.DropForeignKey(
                name: "FK_OutboundMessages_People_ConfirmedByActorId",
                table: "OutboundMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboundMessages_ConfirmedByActorId",
                table: "OutboundMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboundMessages_DeliveryMode_Status_ScheduledAtUtc",
                table: "OutboundMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboundMessages_Attempts",
                table: "OutboundMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboundMessages_Confirmation",
                table: "OutboundMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboundMessages_Mode",
                table: "OutboundMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboundMessages_Status",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "ConfirmedAtUtc",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "ConfirmedByActorId",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "DeliveryMode",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "LastAttemptAtUtc",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "OutcomeNote",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "TargetGroupId",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "OutboundMessages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboundMessages_Status",
                table: "OutboundMessages",
                sql: "Status IN (0,1,2,3)");
        }
    }
}
