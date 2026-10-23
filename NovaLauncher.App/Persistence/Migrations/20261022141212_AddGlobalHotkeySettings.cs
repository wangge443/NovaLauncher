using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaLauncher.App.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalHotkeySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GlobalHotkeyEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "GlobalHotkeyKey",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 32);

            migrationBuilder.AddColumn<int>(
                name: "GlobalHotkeyModifiers",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GlobalHotkeyEnabled",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "GlobalHotkeyKey",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "GlobalHotkeyModifiers",
                table: "AppSettings");
        }
    }
}
