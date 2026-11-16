using System;
using EcommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcommerceBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddProductReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:order_status", "Cancelled,Confirmed,Delivered,New,Packed,Refused,Returned,Shipped")
                .Annotation("Npgsql:Enum:payment_method", "CashOnDelivery,Online")
                .Annotation("Npgsql:Enum:payment_status", "Failed,Paid,Pending,Refunded")
                .Annotation("Npgsql:Enum:review_status", "Approved,Pending,Rejected")
                .Annotation("Npgsql:Enum:review_status.review_status", "pending,approved,rejected")
                .Annotation("Npgsql:Enum:shipment_status", "Cancelled,Confirmed,Pending,Problem,Requested,Shipped")
                .OldAnnotation("Npgsql:Enum:order_status", "Cancelled,Confirmed,Delivered,New,Packed,Refused,Returned,Shipped")
                .OldAnnotation("Npgsql:Enum:payment_method", "CashOnDelivery,Online")
                .OldAnnotation("Npgsql:Enum:payment_status", "Failed,Paid,Pending,Refunded")
                .OldAnnotation("Npgsql:Enum:shipment_status", "Cancelled,Confirmed,Pending,Problem,Requested,Shipped");

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: false),
                    Pros = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Cons = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<ReviewStatus>(type: "review_status", nullable: false, defaultValueSql: "'Pending'"),
                    AdminComment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                    table.CheckConstraint("Reviews_Rating_check", "\"Rating\" >= 1 AND \"Rating\" <= 5");
                    table.ForeignKey(
                        name: "Reviews_CategoryId_fkey",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "Reviews_ProductId_fkey",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_CategoryId_Approved",
                table: "Reviews",
                columns: new[] { "CategoryId", "CreatedAt" },
                descending: new[] { false, true },
                filter: "\"Status\" = 'Approved' AND \"CategoryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ProductId_Approved",
                table: "Reviews",
                columns: new[] { "ProductId", "CreatedAt" },
                descending: new[] { false, true },
                filter: "\"Status\" = 'Approved'");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_Status_CreatedAt",
                table: "Reviews",
                columns: new[] { "Status", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:order_status", "Cancelled,Confirmed,Delivered,New,Packed,Refused,Returned,Shipped")
                .Annotation("Npgsql:Enum:payment_method", "CashOnDelivery,Online")
                .Annotation("Npgsql:Enum:payment_status", "Failed,Paid,Pending,Refunded")
                .Annotation("Npgsql:Enum:shipment_status", "Cancelled,Confirmed,Pending,Problem,Requested,Shipped")
                .OldAnnotation("Npgsql:Enum:order_status", "Cancelled,Confirmed,Delivered,New,Packed,Refused,Returned,Shipped")
                .OldAnnotation("Npgsql:Enum:payment_method", "CashOnDelivery,Online")
                .OldAnnotation("Npgsql:Enum:payment_status", "Failed,Paid,Pending,Refunded")
                .OldAnnotation("Npgsql:Enum:review_status", "Approved,Pending,Rejected")
                .OldAnnotation("Npgsql:Enum:review_status.review_status", "pending,approved,rejected")
                .OldAnnotation("Npgsql:Enum:shipment_status", "Cancelled,Confirmed,Pending,Problem,Requested,Shipped");
        }
    }
}
