using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaLauncher.App.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackgroundAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BackgroundBlurRadius",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 12.0);

            migrationBuilder.AddColumn<string>(
                name: "BackgroundImagePath",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BackgroundOverlayOpacity",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.71999999999999997);

            migrationBuilder.AddColumn<string>(
                name: "BackgroundStretch",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "UniformToFill");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundBlurRadius",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BackgroundImagePath",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BackgroundOverlayOpacity",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BackgroundStretch",
                table: "AppSettings");
        }
    }
}
