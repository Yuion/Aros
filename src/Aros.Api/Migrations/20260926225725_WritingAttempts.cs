using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class WritingAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WritingAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WordId = table.Column<int>(type: "integer", nullable: false),
                    Character = table.Column<string>(type: "text", nullable: false),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    Mistakes = table.Column<int>(type: "integer", nullable: false),
                    Backwards = table.Column<bool>(type: "boolean", nullable: false),
                    Strokes = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WritingAttempts_VocabWords_WordId",
                        column: x => x.WordId,
                        principalTable: "VocabWords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttempts_At",
                table: "WritingAttempts",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttempts_WordId_Mode",
                table: "WritingAttempts",
                columns: new[] { "WordId", "Mode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WritingAttempts");
        }
    }
}
