using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaLauncher.App.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCloseToTraySetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CloseToTray",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloseToTray",
                table: "AppSettings");
        }
    }
}
