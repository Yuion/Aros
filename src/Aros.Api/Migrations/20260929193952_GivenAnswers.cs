using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class GivenAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Given",
                table: "VocabAnswers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Given",
                table: "ListeningAnswers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Given",
                table: "GrammarAnswers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Given",
                table: "VocabAnswers");

            migrationBuilder.DropColumn(
                name: "Given",
                table: "ListeningAnswers");

            migrationBuilder.DropColumn(
                name: "Given",
                table: "GrammarAnswers");
        }
    }
}
