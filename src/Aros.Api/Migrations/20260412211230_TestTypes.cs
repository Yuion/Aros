using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class TestTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestAnswers_VocabEntries_VocabEntryId",
                table: "TestAnswers");

            migrationBuilder.AddColumn<int>(
                name: "TestType",
                table: "TestSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RomajiCorrect",
                table: "TestAnswers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SelectedVocabEntryId",
                table: "TestAnswers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TestType",
                table: "TestAnswers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TranslationCorrect",
                table: "TestAnswers",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestAnswers_SelectedVocabEntryId",
                table: "TestAnswers",
                column: "SelectedVocabEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_TestAnswers_VocabEntries_SelectedVocabEntryId",
                table: "TestAnswers",
                column: "SelectedVocabEntryId",
                principalTable: "VocabEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TestAnswers_VocabEntries_VocabEntryId",
                table: "TestAnswers",
                column: "VocabEntryId",
                principalTable: "VocabEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestAnswers_VocabEntries_SelectedVocabEntryId",
                table: "TestAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_TestAnswers_VocabEntries_VocabEntryId",
                table: "TestAnswers");

            migrationBuilder.DropIndex(
                name: "IX_TestAnswers_SelectedVocabEntryId",
                table: "TestAnswers");

            migrationBuilder.DropColumn(
                name: "TestType",
                table: "TestSessions");

            migrationBuilder.DropColumn(
                name: "RomajiCorrect",
                table: "TestAnswers");

            migrationBuilder.DropColumn(
                name: "SelectedVocabEntryId",
                table: "TestAnswers");

            migrationBuilder.DropColumn(
                name: "TestType",
                table: "TestAnswers");

            migrationBuilder.DropColumn(
                name: "TranslationCorrect",
                table: "TestAnswers");

            migrationBuilder.AddForeignKey(
                name: "FK_TestAnswers_VocabEntries_VocabEntryId",
                table: "TestAnswers",
                column: "VocabEntryId",
                principalTable: "VocabEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
