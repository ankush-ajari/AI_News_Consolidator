using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRawSourceItemSourceClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceClass",
                table: "RawSourceItems",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceClass",
                table: "RawSourceItems");
        }
    }
}
