using AiIntelligence.Domain.Enums;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiIntelligence.Infrastructure.Persistence.Migrations
{
    public partial class AddTrendFamily : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TrendFamily",
                table: "TrendEvidence",
                type: "INTEGER",
                maxLength: 50,
                nullable: false,
                defaultValue: (int)TrendFamily.Unknown);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrendFamily",
                table: "TrendEvidence");
        }
    }
}
