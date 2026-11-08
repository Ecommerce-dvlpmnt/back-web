using EcommerceBackend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcommerceBackend.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261107120000_AddCatalogIndexes")]
    public partial class AddCatalogIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // В Supabase расширения лежат в схеме "extensions"
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA extensions;");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Catalog_CreatedAt",
                table: "Products",
                columns: new[] { "CategoryId", "CreatedAt" },
                descending: new[] { false, true },
                filter: "\"IsActive\" IS TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Price",
                table: "Products",
                column: "Price",
                filter: "\"IsActive\" IS TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name_trgm",
                table: "Products",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku_trgm",
                table: "Products",
                column: "Sku")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Products_Sku_trgm", table: "Products");
            migrationBuilder.DropIndex(name: "IX_Products_Name_trgm", table: "Products");
            migrationBuilder.DropIndex(name: "IX_Products_Price", table: "Products");
            migrationBuilder.DropIndex(name: "IX_Products_Catalog_CreatedAt", table: "Products");
        }
    }
}