using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class VocabTrainer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VocabLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SourceLanguage = table.Column<string>(type: "text", nullable: false),
                    TargetLanguage = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VocabListId = table.Column<int>(type: "integer", nullable: false),
                    TakenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestSessions_VocabLists_VocabListId",
                        column: x => x.VocabListId,
                        principalTable: "VocabLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VocabEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VocabListId = table.Column<int>(type: "integer", nullable: false),
                    SourceWord = table.Column<string>(type: "text", nullable: false),
                    TargetWord = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VocabEntries_VocabLists_VocabListId",
                        column: x => x.VocabListId,
                        principalTable: "VocabLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TestSessionId = table.Column<int>(type: "integer", nullable: false),
                    VocabEntryId = table.Column<int>(type: "integer", nullable: false),
                    Correct = table.Column<bool>(type: "boolean", nullable: false),
                    TimeTakenMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestAnswers_TestSessions_TestSessionId",
                        column: x => x.TestSessionId,
                        principalTable: "TestSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestAnswers_VocabEntries_VocabEntryId",
                        column: x => x.VocabEntryId,
                        principalTable: "VocabEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestAnswers_TestSessionId",
                table: "TestAnswers",
                column: "TestSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TestAnswers_VocabEntryId",
                table: "TestAnswers",
                column: "VocabEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TestSessions_VocabListId",
                table: "TestSessions",
                column: "VocabListId");

            migrationBuilder.CreateIndex(
                name: "IX_VocabEntries_VocabListId",
                table: "VocabEntries",
                column: "VocabListId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestAnswers");

            migrationBuilder.DropTable(
                name: "TestSessions");

            migrationBuilder.DropTable(
                name: "VocabEntries");

            migrationBuilder.DropTable(
                name: "VocabLists");
        }
    }
}
