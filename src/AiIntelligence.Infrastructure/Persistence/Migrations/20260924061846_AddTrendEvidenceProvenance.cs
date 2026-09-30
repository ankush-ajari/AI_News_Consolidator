using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrendEvidenceProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicationName",
                table: "TrendEvidence",
                type: "TEXT",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QuantitativeEvidence",
                table: "TrendEvidence",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_TrendEvidence_SourceItemId",
                table: "TrendEvidence",
                column: "SourceItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_TrendEvidence_RawSourceItems_SourceItemId",
                table: "TrendEvidence",
                column: "SourceItemId",
                principalTable: "RawSourceItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrendEvidence_RawSourceItems_SourceItemId",
                table: "TrendEvidence");

            migrationBuilder.DropIndex(
                name: "IX_TrendEvidence_SourceItemId",
                table: "TrendEvidence");

            migrationBuilder.DropColumn(
                name: "PublicationName",
                table: "TrendEvidence");

            migrationBuilder.DropColumn(
                name: "QuantitativeEvidence",
                table: "TrendEvidence");
        }
    }
}
