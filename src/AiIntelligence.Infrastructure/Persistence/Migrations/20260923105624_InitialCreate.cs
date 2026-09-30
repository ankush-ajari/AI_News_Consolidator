using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntelligenceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Vendor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductOrFramework = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Capabilities = table.Column<string>(type: "TEXT", nullable: false),
                    Limitations = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseStage = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SourceClass = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntelligenceItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Vendor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SourceClass = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludeTopicHints = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrendEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Period = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Finding = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceSummary = table.Column<string>(type: "TEXT", nullable: false),
                    Confidence = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrendEvidence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawSourceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    CanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RawContent = table.Column<string>(type: "TEXT", nullable: false),
                    EnrichedContent = table.Column<string>(type: "TEXT", nullable: false),
                    ContentSourceUrls = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawSourceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawSourceItems_SourceDefinitions_SourceDefinitionId",
                        column: x => x.SourceDefinitionId,
                        principalTable: "SourceDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawSourceItems_CanonicalUrl_ContentHash",
                table: "RawSourceItems",
                columns: new[] { "CanonicalUrl", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawSourceItems_SourceDefinitionId",
                table: "RawSourceItems",
                column: "SourceDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceDefinitions_Name",
                table: "SourceDefinitions",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntelligenceItems");

            migrationBuilder.DropTable(
                name: "RawSourceItems");

            migrationBuilder.DropTable(
                name: "TrendEvidence");

            migrationBuilder.DropTable(
                name: "SourceDefinitions");
        }
    }
}
