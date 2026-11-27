using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiEventWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppConnections_Enabled",
                table: "WhatsAppConnections");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppConnections_EventId",
                table: "WhatsAppConnections");

            migrationBuilder.AddColumn<bool>(
                name: "FeatureAttendance",
                table: "WhatsAppConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FeatureFunReplies",
                table: "WhatsAppConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FeatureInformation",
                table: "WhatsAppConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FeatureMealPreferences",
                table: "WhatsAppConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FeatureMealProposals",
                table: "WhatsAppConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FeatureOperations",
                table: "WhatsAppConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FeaturesRevision",
                table: "WhatsAppConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Existing connections keep the full set of commands they had before M36.
            migrationBuilder.Sql("UPDATE \"WhatsAppConnections\" SET \"FeatureAttendance\" = 1, " +
                "\"FeatureInformation\" = 1, \"FeatureOperations\" = 1, " +
                "\"FeatureMealProposals\" = 1, \"FeatureMealPreferences\" = 1, " +
                "\"FeatureFunReplies\" = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConnections_Enabled",
                table: "WhatsAppConnections",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConnections_EventId",
                table: "WhatsAppConnections",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConnections_GroupId",
                table: "WhatsAppConnections",
                column: "GroupId",
                unique: true,
                filter: "\"Enabled\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppConnections_Enabled",
                table: "WhatsAppConnections");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppConnections_EventId",
                table: "WhatsAppConnections");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppConnections_GroupId",
                table: "WhatsAppConnections");

            migrationBuilder.DropColumn(
                name: "FeatureAttendance",
                table: "WhatsAppConnections");

            migrationBuilder.DropColumn(
                name: "FeatureFunReplies",
                table: "WhatsAppConnections");

            migrationBuilder.DropColumn(
                name: "FeatureInformation",
                table: "WhatsAppConnections");

            migrationBuilder.DropColumn(
                name: "FeatureMealPreferences",
                table: "WhatsAppConnections");

            migrationBuilder.DropColumn(
                name: "FeatureMealProposals",
                table: "WhatsAppConnections");

            migrationBuilder.DropColumn(
                name: "FeatureOperations",
                table: "WhatsAppConnections");

            migrationBuilder.DropColumn(
                name: "FeaturesRevision",
                table: "WhatsAppConnections");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConnections_Enabled",
                table: "WhatsAppConnections",
                column: "Enabled",
                unique: true,
                filter: "Enabled = 1");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConnections_EventId",
                table: "WhatsAppConnections",
                column: "EventId");
        }
    }
}
