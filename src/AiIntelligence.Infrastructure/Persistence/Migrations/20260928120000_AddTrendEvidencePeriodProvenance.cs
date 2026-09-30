using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrendEvidencePeriodProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PeriodProvenance",
                table: "TrendEvidence",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "Unknown");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PeriodProvenance",
                table: "TrendEvidence");
        }
    }
}
