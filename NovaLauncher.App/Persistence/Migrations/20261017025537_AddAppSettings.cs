using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaLauncher.App.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    StartupPage = table.Column<string>(type: "TEXT", nullable: false),
                    RememberWindowSize = table.Column<bool>(type: "INTEGER", nullable: false),
                    RememberWindowPosition = table.Column<bool>(type: "INTEGER", nullable: false),
                    WindowWidth = table.Column<double>(type: "REAL", nullable: true),
                    WindowHeight = table.Column<double>(type: "REAL", nullable: true),
                    WindowLeft = table.Column<double>(type: "REAL", nullable: true),
                    WindowTop = table.Column<double>(type: "REAL", nullable: true),
                    BackgroundMode = table.Column<string>(type: "TEXT", nullable: false),
                    ThemeMode = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                    table.CheckConstraint("CK_AppSettings_Singleton", "Id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");
        }
    }
}
