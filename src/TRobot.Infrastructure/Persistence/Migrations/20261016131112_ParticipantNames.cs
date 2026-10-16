using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRobot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParticipantNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EventAudits_Action",
                table: "EventAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EventAudits_Person",
                table: "EventAudits");

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "People",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationFingerprint",
                table: "EventAudits",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_EventAudits_Action",
                table: "EventAudits",
                sql: "Action IN ('EventCreated', 'EventUpdated', 'ParticipantAdded', 'ParticipantRenamed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EventAudits_Fingerprint",
                table: "EventAudits",
                sql: "RegistrationFingerprint IS NULL OR (Action = 'ParticipantAdded' AND length(RegistrationFingerprint) = 64 AND RegistrationFingerprint NOT GLOB '*[^0-9A-F]*')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EventAudits_Person",
                table: "EventAudits",
                sql: "(Action IN ('ParticipantAdded', 'ParticipantRenamed') AND PersonId IS NOT NULL) OR (Action NOT IN ('ParticipantAdded', 'ParticipantRenamed') AND PersonId IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EventAudits_Action",
                table: "EventAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EventAudits_Fingerprint",
                table: "EventAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EventAudits_Person",
                table: "EventAudits");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "People");

            migrationBuilder.DropColumn(
                name: "RegistrationFingerprint",
                table: "EventAudits");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EventAudits_Action",
                table: "EventAudits",
                sql: "Action IN ('EventCreated', 'EventUpdated', 'ParticipantAdded')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EventAudits_Person",
                table: "EventAudits",
                sql: "(Action = 'ParticipantAdded' AND PersonId IS NOT NULL) OR (Action <> 'ParticipantAdded' AND PersonId IS NULL)");
        }
    }
}
