using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reacher.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "brave_usage",
                columns: table => new
                {
                    month = table.Column<string>(type: "text", nullable: false),
                    request_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brave_usage", x => x.month);
                });

            migrationBuilder.CreateTable(
                name: "company",
                columns: table => new
                {
                    orgnr = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sni = table.Column<string>(type: "text", nullable: false),
                    legal_form = table.Column<string>(type: "text", nullable: false),
                    company_status = table.Column<string>(type: "text", nullable: false),
                    employee_class = table.Column<string>(type: "text", nullable: false),
                    revenue_class = table.Column<string>(type: "text", nullable: false),
                    revenue_class_year = table.Column<int>(type: "integer", nullable: false),
                    registered_at = table.Column<DateOnly>(type: "date", nullable: true),
                    ad_block_type = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company", x => x.orgnr);
                    table.CheckConstraint("ck_company_orgnr", "orgnr ~ '^[0-9]{2}[2-9][0-9]{7}$'");
                });

            migrationBuilder.CreateTable(
                name: "coverage",
                columns: table => new
                {
                    sni = table.Column<string>(type: "text", nullable: false),
                    municipality = table.Column<string>(type: "text", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coverage", x => new { x.sni, x.municipality });
                });

            migrationBuilder.CreateTable(
                name: "ingest_job",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sni = table.Column<string>(type: "text", nullable: false),
                    municipalities = table.Column<List<string>>(type: "text[]", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    phase = table.Column<string>(type: "text", nullable: true),
                    done = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingest_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "financial_fact",
                columns: table => new
                {
                    orgnr = table.Column<string>(type: "text", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<decimal>(type: "numeric", nullable: false),
                    document_id = table.Column<string>(type: "text", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_fact", x => new { x.orgnr, x.period_end, x.key });
                    table.ForeignKey(
                        name: "fk_financial_fact_company_orgnr",
                        column: x => x.orgnr,
                        principalTable: "company",
                        principalColumn: "orgnr",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workplace",
                columns: table => new
                {
                    cfar = table.Column<string>(type: "text", nullable: false),
                    orgnr = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    sni = table.Column<string>(type: "text", nullable: false),
                    municipality = table.Column<string>(type: "text", nullable: false),
                    street = table.Column<string>(type: "text", nullable: false),
                    postal_code = table.Column<string>(type: "text", nullable: false),
                    city = table.Column<string>(type: "text", nullable: false),
                    workplace_status = table.Column<string>(type: "text", nullable: false),
                    ad_block_type = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workplace", x => x.cfar);
                    table.ForeignKey(
                        name: "fk_workplace_company_orgnr",
                        column: x => x.orgnr,
                        principalTable: "company",
                        principalColumn: "orgnr",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workplace_municipality_sni",
                table: "workplace",
                columns: new[] { "municipality", "sni" });

            migrationBuilder.CreateIndex(
                name: "ix_workplace_orgnr",
                table: "workplace",
                column: "orgnr");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "brave_usage");

            migrationBuilder.DropTable(
                name: "coverage");

            migrationBuilder.DropTable(
                name: "financial_fact");

            migrationBuilder.DropTable(
                name: "ingest_job");

            migrationBuilder.DropTable(
                name: "workplace");

            migrationBuilder.DropTable(
                name: "company");
        }
    }
}
