using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class ChineseVocab : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DictionaryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Simplified = table.Column<string>(type: "text", nullable: false),
                    Traditional = table.Column<string>(type: "text", nullable: false),
                    Pinyin = table.Column<string>(type: "text", nullable: false),
                    English = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DictionaryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VocabWords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Characters = table.Column<string>(type: "text", nullable: false),
                    Pinyin = table.Column<string>(type: "text", nullable: false),
                    English = table.Column<string>(type: "text", nullable: false),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    NeedsReview = table.Column<bool>(type: "boolean", nullable: false),
                    ReadingAlternatives = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabWords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VocabAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VocabWordId = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Correct = table.Column<bool>(type: "boolean", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VocabAnswers_VocabWords_VocabWordId",
                        column: x => x.VocabWordId,
                        principalTable: "VocabWords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VocabProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VocabWordId = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    WrongCount = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveCorrect = table.Column<int>(type: "integer", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VocabProgress_VocabWords_VocabWordId",
                        column: x => x.VocabWordId,
                        principalTable: "VocabWords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DictionaryEntries_Simplified",
                table: "DictionaryEntries",
                column: "Simplified");

            migrationBuilder.CreateIndex(
                name: "IX_VocabAnswers_AnsweredAt",
                table: "VocabAnswers",
                column: "AnsweredAt");

            migrationBuilder.CreateIndex(
                name: "IX_VocabAnswers_VocabWordId",
                table: "VocabAnswers",
                column: "VocabWordId");

            migrationBuilder.CreateIndex(
                name: "IX_VocabProgress_VocabWordId_Direction",
                table: "VocabProgress",
                columns: new[] { "VocabWordId", "Direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VocabWords_Characters",
                table: "VocabWords",
                column: "Characters");

            migrationBuilder.CreateIndex(
                name: "IX_VocabWords_Characters_Pinyin",
                table: "VocabWords",
                columns: new[] { "Characters", "Pinyin" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DictionaryEntries");

            migrationBuilder.DropTable(
                name: "VocabAnswers");

            migrationBuilder.DropTable(
                name: "VocabProgress");

            migrationBuilder.DropTable(
                name: "VocabWords");
        }
    }
}
