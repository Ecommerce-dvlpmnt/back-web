using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcommerceBackend.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeCatalogIndexesAdvanced : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Attributes",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Catalog",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Attributes_gin",
                table: "Products",
                column: "Attributes")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Catalog_PriceDesc",
                table: "Products",
                columns: new[] { "CategoryId", "Price" },
                descending: new[] { false, true },
                filter: "\"IsActive\" IS TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Attributes_gin",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Catalog_PriceDesc",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Attributes",
                table: "Products",
                column: "Attributes")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Catalog",
                table: "Products",
                columns: new[] { "CategoryId", "Price" },
                filter: "\"IsActive\" IS TRUE");
        }
    }
}
