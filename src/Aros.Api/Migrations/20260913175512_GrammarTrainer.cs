using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class GrammarTrainer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GrammarAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GrammarPointId = table.Column<int>(type: "integer", nullable: false),
                    GrammarItemId = table.Column<int>(type: "integer", nullable: false),
                    Correct = table.Column<bool>(type: "boolean", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrammarAnswers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GrammarItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GrammarPointId = table.Column<int>(type: "integer", nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    Answer = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrammarItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrammarItems_GrammarPoints_GrammarPointId",
                        column: x => x.GrammarPointId,
                        principalTable: "GrammarPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrammarProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GrammarPointId = table.Column<int>(type: "integer", nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    WrongCount = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveCorrect = table.Column<int>(type: "integer", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrammarProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrammarProgress_GrammarPoints_GrammarPointId",
                        column: x => x.GrammarPointId,
                        principalTable: "GrammarPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GrammarAnswers_AnsweredAt",
                table: "GrammarAnswers",
                column: "AnsweredAt");

            migrationBuilder.CreateIndex(
                name: "IX_GrammarItems_GrammarPointId_Answer",
                table: "GrammarItems",
                columns: new[] { "GrammarPointId", "Answer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrammarProgress_GrammarPointId",
                table: "GrammarProgress",
                column: "GrammarPointId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GrammarAnswers");

            migrationBuilder.DropTable(
                name: "GrammarItems");

            migrationBuilder.DropTable(
                name: "GrammarProgress");
        }
    }
}
