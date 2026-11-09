using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reacher.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyOutreach : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_outreach",
                columns: table => new
                {
                    orgnr = table.Column<string>(type: "text", nullable: false),
                    called_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    willing = table.Column<bool>(type: "boolean", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_outreach", x => x.orgnr);
                    table.ForeignKey(
                        name: "fk_company_outreach_company_orgnr",
                        column: x => x.orgnr,
                        principalTable: "company",
                        principalColumn: "orgnr",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_outreach");
        }
    }
}
