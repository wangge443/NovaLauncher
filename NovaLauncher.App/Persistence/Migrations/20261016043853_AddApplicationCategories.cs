using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaLauncher.App.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.CheckConstraint("CK_Category_Name", "length(Name) BETWEEN 1 AND 24 AND Name = trim(Name)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_CategoryId",
                table: "Applications",
                column: "CategoryId");

            // Ordinary user categories, inserted once when this migration is applied.
            migrationBuilder.InsertData("Categories", new[] { "Id", "Name", "CreatedAt", "SortOrder" },
                new object[,]
                {
                    { new Guid("331d6fc4-9b1a-4583-a913-044c6773f141"), "游戏", new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero), 0L },
                    { new Guid("992c7894-bf73-4a6a-9b9f-40ce00b14535"), "应用", new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero), 1L }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Applications_Categories_CategoryId",
                table: "Applications",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Applications_Categories_CategoryId",
                table: "Applications");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Applications_CategoryId",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Applications");
        }
    }
}
