using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddJapaneseFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetReading",
                table: "VocabEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetRomaji",
                table: "VocabEntries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetReading",
                table: "VocabEntries");

            migrationBuilder.DropColumn(
                name: "TargetRomaji",
                table: "VocabEntries");
        }
    }
}
