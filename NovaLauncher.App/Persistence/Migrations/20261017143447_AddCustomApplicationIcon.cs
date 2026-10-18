using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaLauncher.App.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomApplicationIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomIconPath",
                table: "Applications",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomIconPath",
                table: "Applications");
        }
    }
}
