using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaLauncher.App.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    ExecutablePath = table.Column<string>(type: "TEXT", nullable: false),
                    TargetPathKey = table.Column<string>(type: "TEXT", nullable: false),
                    SourcePath = table.Column<string>(type: "TEXT", nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    LaunchCount = table.Column<long>(type: "INTEGER", nullable: false),
                    LastLaunchTime = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DateAdded = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<long>(type: "INTEGER", nullable: false),
                    IconCachePath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                    table.CheckConstraint("CK_Application_Paths", "length(trim(ExecutablePath)) > 0 AND length(trim(SourcePath)) > 0 AND length(trim(TargetPathKey)) > 0");
                    table.CheckConstraint("CK_Application_Values", "LaunchCount >= 0 AND SourceType IN (0, 1)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_TargetPathKey",
                table: "Applications",
                column: "TargetPathKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Applications");
        }
    }
}
