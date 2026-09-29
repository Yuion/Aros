using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class ToneAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ToneAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Syllable = table.Column<string>(type: "text", nullable: false),
                    Tone = table.Column<int>(type: "integer", nullable: false),
                    Given = table.Column<int>(type: "integer", nullable: false),
                    Correct = table.Column<bool>(type: "boolean", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToneAnswers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ToneAnswers_At",
                table: "ToneAnswers",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_ToneAnswers_Syllable_Tone",
                table: "ToneAnswers",
                columns: new[] { "Syllable", "Tone" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ToneAnswers");
        }
    }
}
