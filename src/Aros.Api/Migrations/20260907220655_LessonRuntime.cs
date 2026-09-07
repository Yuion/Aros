using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class LessonRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Exercises",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "text", nullable: false),
                    LessonId = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Instructions = table.Column<string>(type: "text", nullable: false),
                    ItemsJson = table.Column<string>(type: "text", nullable: false),
                    Fingerprint = table.Column<string>(type: "text", nullable: false),
                    CharacterBank = table.Column<List<string>>(type: "text[]", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercises", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LessonRuntime",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LessonId = table.Column<string>(type: "text", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    CurrentTopic = table.Column<string>(type: "text", nullable: false),
                    ExerciseKey = table.Column<string>(type: "text", nullable: true),
                    ExerciseAlreadySent = table.Column<bool>(type: "boolean", nullable: false),
                    AwaitingUserAnswer = table.Column<bool>(type: "boolean", nullable: false),
                    AnsweringExerciseKey = table.Column<string>(type: "text", nullable: true),
                    LastCompletedExerciseKey = table.Column<string>(type: "text", nullable: true),
                    ExercisesSentThisLesson = table.Column<List<string>>(type: "text[]", nullable: false),
                    NewVocabularyThisLesson = table.Column<List<string>>(type: "text[]", nullable: false),
                    NewGrammarThisLesson = table.Column<List<string>>(type: "text[]", nullable: false),
                    MinutesRequested = table.Column<int>(type: "integer", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonRuntime", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_Fingerprint",
                table: "Exercises",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_Key",
                table: "Exercises",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Exercises");

            migrationBuilder.DropTable(
                name: "LessonRuntime");
        }
    }
}
