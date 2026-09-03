using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class ListeningModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TtsClipStats_TtsClipId",
                table: "TtsClipStats");

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "TtsClipStats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "English",
                table: "TtsClips",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Pinyin",
                table: "TtsClips",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "ListeningAnswers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TtsClipStats_TtsClipId_Mode",
                table: "TtsClipStats",
                columns: new[] { "TtsClipId", "Mode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TtsClipStats_TtsClipId_Mode",
                table: "TtsClipStats");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "TtsClipStats");

            migrationBuilder.DropColumn(
                name: "English",
                table: "TtsClips");

            migrationBuilder.DropColumn(
                name: "Pinyin",
                table: "TtsClips");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "ListeningAnswers");

            migrationBuilder.CreateIndex(
                name: "IX_TtsClipStats_TtsClipId",
                table: "TtsClipStats",
                column: "TtsClipId",
                unique: true);
        }
    }
}
