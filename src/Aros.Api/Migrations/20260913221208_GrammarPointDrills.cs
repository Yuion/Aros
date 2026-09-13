using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class GrammarPointDrills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "Drills",
                table: "GrammarPoints",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");   // the points already held have no drills yet
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Drills",
                table: "GrammarPoints");
        }
    }
}
