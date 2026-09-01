using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class ListeningAnswerHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ListeningAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TtsClipId = table.Column<int>(type: "integer", nullable: false),
                    Correct = table.Column<bool>(type: "boolean", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningAnswers_TtsClips_TtsClipId",
                        column: x => x.TtsClipId,
                        principalTable: "TtsClips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningAnswers_AnsweredAt",
                table: "ListeningAnswers",
                column: "AnsweredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningAnswers_TtsClipId",
                table: "ListeningAnswers",
                column: "TtsClipId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListeningAnswers");
        }
    }
}
