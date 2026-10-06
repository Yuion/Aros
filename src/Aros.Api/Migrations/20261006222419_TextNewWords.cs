using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class TextNewWords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Texts written before this column existed name no new words, and an array column
            // with no default cannot be added to a table that already has rows
            migrationBuilder.AddColumn<List<string>>(
                name: "NewWords",
                table: "TutorTexts",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewWords",
                table: "TutorTexts");
        }
    }
}
