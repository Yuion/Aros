using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Aros.Api.Migrations
{
    /// <inheritdoc />
    public partial class Homophones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HomophoneGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Characters = table.Column<string>(type: "text", nullable: false),
                    Reading = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomophoneGroups", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "HomophoneGroups",
                columns: new[] { "Id", "Characters", "CreatedAt", "Reading" },
                values: new object[,]
                {
                    { 1, "他她它", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "tā" },
                    { 2, "你妳", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "nǐ" },
                    { 3, "的得地", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "de" },
                    { 4, "在再", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "zài" },
                    { 5, "是事", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "shì" },
                    { 6, "做作", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "zuò" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_HomophoneGroups_Characters",
                table: "HomophoneGroups",
                column: "Characters",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HomophoneGroups");
        }
    }
}
