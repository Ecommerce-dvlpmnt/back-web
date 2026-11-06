using System;
using EcommerceBackend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EcommerceBackend.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261102231516_InitialCreate")]
    partial class InitialCreate
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.11")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.HasPostgresEnum(modelBuilder, "order_status", new[] { "New", "Confirmed", "Packed", "Shipped", "Delivered", "Cancelled", "Refused", "Returned" });
            NpgsqlModelBuilderExtensions.HasPostgresEnum(modelBuilder, "payment_method", new[] { "CashOnDelivery", "Online" });
            NpgsqlModelBuilderExtensions.HasPostgresEnum(modelBuilder, "payment_status", new[] { "Pending", "Paid", "Failed", "Refunded" });
            NpgsqlModelBuilderExtensions.HasPostgresEnum(modelBuilder, "shipment_status", new[] { "Pending", "Requested", "Confirmed", "Shipped", "Problem", "Cancelled" });
            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Admin", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("Id")
                        .HasDefaultValueSql("gen_random_uuid()");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("Name");

                    b.Property<string>("Role")
                        .IsRequired()
                        .ValueGeneratedOnAdd()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasDefaultValue("Manager")
                        .HasColumnName("Role");

                    b.Property<long?>("TelegramId")
                        .HasColumnType("bigint")
                        .HasColumnName("TelegramId");

                    b.HasKey("Id");

                    b.HasIndex("TelegramId")
                        .IsUnique()
                        .HasDatabaseName("Admins_TelegramId_key");

                    b.ToTable("Admins", (string)null);
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Campaign", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("Id")
                        .HasDefaultValueSql("gen_random_uuid()");

                    b.Property<string>("BannerUrl")
                        .HasMaxLength(500)
                        .HasColumnType("character varying(500)")
                        .HasColumnName("BannerUrl");

                    b.Property<DateTime>("EndsAt")
                        .HasColumnType("timestamptz")
                        .HasColumnName("EndsAt");

                    b.Property<bool>("IsActive")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("boolean")
                        .HasDefaultValue(true)
                        .HasColumnName("IsActive");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("character varying(150)")
                        .HasColumnName("Name");

                    b.Property<string>("Slug")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("character varying(150)")
                        .HasColumnName("Slug");

                    b.Property<DateTime>("StartsAt")
                        .HasColumnType("timestamptz")
                        .HasColumnName("StartsAt");

                    b.HasKey("Id");

                    b.HasIndex("Slug")
                        .IsUnique()
                        .HasDatabaseName("Campaigns_Slug_key");

                    b.ToTable("Campaigns", null, t =>
                        {
                            t.HasCheckConstraint("Campaigns_Period_check", "\"EndsAt\" > \"StartsAt\"");
                        });
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.CampaignProduct", b =>
                {
                    b.Property<Guid>("CampaignId")
                        .HasColumnType("uuid")
                        .HasColumnName("CampaignId");

                    b.Property<Guid>("ProductId")
                        .HasColumnType("uuid")
                        .HasColumnName("ProductId");

                    b.Property<decimal?>("PromoPrice")
                        .HasPrecision(10, 2)
                        .HasColumnType("numeric(10,2)")
                        .HasColumnName("PromoPrice");

                    b.HasKey("CampaignId", "ProductId");

                    b.HasIndex("ProductId")
                        .HasDatabaseName("IX_CampaignProducts_ProductId");

                    b.ToTable("CampaignProducts", (string)null);
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Category", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("Id")
                        .HasDefaultValueSql("gen_random_uuid()");

                    b.Property<string>("FilterSchema")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("jsonb")
                        .HasColumnName("FilterSchema")
                        .HasDefaultValueSql("'{}'::jsonb");

                    b.Property<bool>("IsActive")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("boolean")
                        .HasDefaultValue(true)
                        .HasColumnName("IsActive");

                    b.Property<string>("MetaDescription")
                        .HasColumnType("text")
                        .HasColumnName("MetaDescription");

                    b.Property<string>("MetaTitle")
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)")
                        .HasColumnName("MetaTitle");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("Name");

                    b.Property<Guid?>("ParentId")
                        .HasColumnType("uuid")
                        .HasColumnName("ParentId");

                    b.Property<string>("Slug")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("Slug");

                    b.Property<int>("SortOrder")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasDefaultValue(0)
                        .HasColumnName("SortOrder");

                    b.HasKey("Id");

                    b.HasIndex("ParentId");

                    b.HasIndex("Slug")
                        .IsUnique()
                        .HasDatabaseName("Categories_Slug_key");

                    b.ToTable("Categories", (string)null);
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Order", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("Id")
                        .HasDefaultValueSql("gen_random_uuid()");

                    b.Property<string>("AdminComment")
                        .HasColumnType("text")
                        .HasColumnName("AdminComment");

                    b.Property<Guid?>("CampaignId")
                        .HasColumnType("uuid")
                        .HasColumnName("CampaignId");

                    b.Property<DateTime>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamptz")
                        .HasColumnName("CreatedAt")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                    b.Property<string>("CustomerComment")
                        .HasColumnType("text")
                        .HasColumnName("CustomerComment");

                    b.Property<string>("CustomerFirstName")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("CustomerFirstName");

                    b.Property<string>("CustomerLastName")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("CustomerLastName");

                    b.Property<string>("CustomerPhone")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("character varying(20)")
                        .HasColumnName("CustomerPhone");

                    b.Property<string>("DeliveryCityName")
                        .HasMaxLength(150)
                        .HasColumnType("character varying(150)")
                        .HasColumnName("DeliveryCityName");

                    b.Property<string>("DeliveryCityRef")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("DeliveryCityRef");

                    b.Property<string>("DeliveryWarehouseName")
                        .HasMaxLength(250)
                        .HasColumnType("character varying(250)")
                        .HasColumnName("DeliveryWarehouseName");

                    b.Property<string>("DeliveryWarehouseRef")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("DeliveryWarehouseRef");

                    b.Property<string>("ExternalPaymentId")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("ExternalPaymentId");

                    b.Property<int>("OrderNumber")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasColumnName("OrderNumber");

                    NpgsqlPropertyBuilderExtensions.UseIdentityAlwaysColumn(b.Property<int>("OrderNumber"));

                    b.Property<int>("OrderStatus")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("order_status")
                        .HasColumnName("OrderStatus")
                        .HasDefaultValueSql("'New'");

                    b.Property<int>("PaymentMethod")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("payment_method")
                        .HasColumnName("PaymentMethod")
                        .HasDefaultValueSql("'CashOnDelivery'");

                    b.Property<string>("PaymentProvider")
                        .HasMaxLength(20)
                        .HasColumnType("character varying(20)")
                        .HasColumnName("PaymentProvider");

                    b.Property<int>("PaymentStatus")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("payment_status")
                        .HasColumnName("PaymentStatus")
                        .HasDefaultValueSql("'Pending'");

                    b.Property<decimal>("TotalPrice")
                        .HasPrecision(10, 2)
                        .HasColumnType("numeric(10,2)")
                        .HasColumnName("TotalPrice");

                    b.Property<decimal>("TotalProfit")
                        .HasPrecision(10, 2)
                        .HasColumnType("numeric(10,2)")
                        .HasColumnName("TotalProfit");

                    b.Property<DateTime>("UpdatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamptz")
                        .HasColumnName("UpdatedAt")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                    b.Property<string>("UtmCampaign")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("UtmCampaign");

                    b.Property<string>("UtmMedium")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("UtmMedium");

                    b.Property<string>("UtmSource")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("UtmSource");

                    b.HasKey("Id");

                    b.HasIndex("CampaignId")
                        .HasDatabaseName("IX_Orders_CampaignId");

                    b.HasIndex("CreatedAt")
                        .IsDescending()
                        .HasDatabaseName("IX_Orders_CreatedAt");

                    b.HasIndex("CustomerPhone")
                        .HasDatabaseName("IX_Orders_CustomerPhone");

                    b.HasIndex("ExternalPaymentId")
                        .IsUnique()
                        .HasDatabaseName("IX_Orders_ExternalPaymentId")
                        .HasFilter("\"ExternalPaymentId\" IS NOT NULL");

                    b.HasIndex("OrderNumber")
                        .IsUnique()
                        .HasDatabaseName("Orders_OrderNumber_key");

                    b.HasIndex("OrderStatus")
                        .HasDatabaseName("IX_Orders_Active")
                        .HasFilter("\"OrderStatus\" IN ('New','Confirmed','Packed')");

                    b.ToTable("Orders", (string)null);
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.OrderItem", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("Id")
                        .HasDefaultValueSql("gen_random_uuid()");

                    b.Property<Guid>("OrderId")
                        .HasColumnType("uuid")
                        .HasColumnName("OrderId");

                    b.Property<Guid>("ProductId")
                        .HasColumnType("uuid")
                        .HasColumnName("ProductId");

                    b.Property<string>("ProductNameSnapshot")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("ProductNameSnapshot");

                    b.Property<int>("Quantity")
                        .HasColumnType("integer")
                        .HasColumnName("Quantity");

                    b.Property<decimal>("UnitDropPrice")
                        .HasPrecision(10, 2)
                        .HasColumnType("numeric(10,2)")
                        .HasColumnName("UnitDropPrice");

                    b.Property<decimal>("UnitPrice")
                        .HasPrecision(10, 2)
                        .HasColumnType("numeric(10,2)")
                        .HasColumnName("UnitPrice");

                    b.HasKey("Id");

                    b.HasIndex("ProductId")
                        .HasDatabaseName("IX_OrderItems_ProductId");

                    b.HasIndex("OrderId", "ProductId")
                        .IsUnique()
                        .HasDatabaseName("OrderItems_OrderId_ProductId_key");

                    b.ToTable("OrderItems", null, t =>
                        {
                            t.HasCheckConstraint("OrderItems_Quantity_check", "\"Quantity\" > 0");
                        });
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.OrderShipment", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("Id")
                        .HasDefaultValueSql("gen_random_uuid()");

                    b.Property<string>("AdminComment")
                        .HasColumnType("text")
                        .HasColumnName("AdminComment");

                    b.Property<DateTime>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamptz")
                        .HasColumnName("CreatedAt")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                    b.Property<Guid>("OrderId")
                        .HasColumnType("uuid")
                        .HasColumnName("OrderId");

                    b.Property<int>("Status")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("shipment_status")
                        .HasColumnName("Status")
                        .HasDefaultValueSql("'Pending'");

                    b.Property<Guid>("SupplierId")
                        .HasColumnType("uuid")
                        .HasColumnName("SupplierId");

                    b.Property<string>("SupplierOrderRef")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("SupplierOrderRef");

                    b.Property<string>("TrackingNumber")
                        .HasMaxLength(20)
                        .HasColumnType("character varying(20)")
                        .HasColumnName("TrackingNumber");

                    b.Property<DateTime>("UpdatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamptz")
                        .HasColumnName("UpdatedAt")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                    b.HasKey("Id");

                    b.HasIndex("SupplierId")
                        .HasDatabaseName("IX_OrderShipments_SupplierId");

                    b.HasIndex("OrderId", "SupplierId")
                        .IsUnique()
                        .HasDatabaseName("OrderShipments_OrderId_SupplierId_key");

                    b.ToTable("OrderShipments", (string)null);
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.OrderStatusHistory", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("Id")
                        .HasDefaultValueSql("gen_random_uuid()");

                    b.Property<Guid?>("ChangedByAdminId")
                        .HasColumnType("uuid")
                        .HasColumnName("ChangedByAdminId");

                    b.Property<string>("Comment")
                        .HasColumnType("text")
                        .HasColumnName("Comment");

                    b.Property<DateTime>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamptz")
                        .HasColumnName("CreatedAt")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                    b.Property<int>("NewStatus")
                        .HasColumnType("order_status")
                        .HasColumnName("NewStatus");

                    b.Property<int?>("OldStatus")
                        .HasColumnType("order_status")
                        .HasColumnName("OldStatus");

                    b.Property<Guid>("OrderId")
                        .HasColumnType("uuid")
                        .HasColumnName("OrderId");

                    b.HasKey("Id");

                    b.HasIndex("ChangedByAdminId");

                    b.HasIndex("OrderId")
                        .HasDatabaseName("IX_OrderStatusHistory_OrderId");

                    b.ToTable("OrderStatusHistory", (string)null);
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Product", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("Id")
                        .HasDefaultValueSql("gen_random_uuid()");

                    b.Property<string>("Attributes")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("jsonb")
                        .HasColumnName("Attributes")
                        .HasDefaultValueSql("'{}'::jsonb");

                    b.Property<Guid>("CategoryId")
                        .HasColumnType("uuid")
                        .HasColumnName("CategoryId");

                    b.Property<DateTime>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamptz")
                        .HasColumnName("CreatedAt")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                    b.Property<string>("Description")
                        .HasColumnType("text")
                        .HasColumnName("Description");

                    b.Property<decimal>("DropPrice")
                        .ValueGeneratedOnAdd()
                        .HasPrecision(10, 2)
                        .HasColumnType("numeric(10,2)")
                        .HasDefaultValue(0m)
                        .HasColumnName("DropPrice");

                    b.Property<bool>("IsActive")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("boolean")
                        .HasDefaultValue(true)
                        .HasColumnName("IsActive");

                    b.Property<string>("MainImageUrl")
                        .HasColumnType("text")
                        .HasColumnName("MainImageUrl");

                    b.Property<string>("MetaDescription")
                        .HasColumnType("text")
                        .HasColumnName("MetaDescription");

                    b.Property<string>("MetaTitle")
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)")
                        .HasColumnName("MetaTitle");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("Name");

                    b.Property<decimal?>("OldPrice")
                        .HasPrecision(10, 2)
                        .HasColumnType("numeric(10,2)")
                        .HasColumnName("OldPrice");

                    b.Property<decimal>("Price")
                        .HasPrecision(10, 2)
                        .HasColumnType("numeric(10,2)")
                        .HasColumnName("Price");

                    b.Property<string>("Sku")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("Sku");

                    b.Property<string>("Slug")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("Slug");

                    b.Property<int>("Stock")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasDefaultValue(0)
                        .HasColumnName("Stock");

                    b.Property<DateTime?>("StockUpdatedAt")
                        .HasColumnType("timestamptz")
                        .HasColumnName("StockUpdatedAt");

                    b.Property<Guid>("SupplierId")
                        .HasColumnType("uuid")
                        .HasColumnName("SupplierId");

                    b.Property<DateTime>("UpdatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamptz")
                        .HasColumnName("UpdatedAt")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                    b.Property<decimal?>("WeightKg")
                        .HasPrecision(5, 2)
                        .HasColumnType("numeric(5,2)")
                        .HasColumnName("WeightKg");

                    b.HasKey("Id");

                    b.HasIndex("Attributes")
                        .HasDatabaseName("IX_Products_Attributes");

                    NpgsqlIndexBuilderExtensions.HasMethod(b.HasIndex("Attributes"), "gin");

                    b.HasIndex("Sku")
                        .IsUnique()
                        .HasDatabaseName("Products_Sku_key");

                    b.HasIndex("Slug")
                        .IsUnique()
                        .HasDatabaseName("Products_Slug_key");

                    b.HasIndex("SupplierId")
                        .HasDatabaseName("IX_Products_SupplierId");

                    b.HasIndex("CategoryId", "Price")
                        .HasDatabaseName("IX_Products_Catalog")
                        .HasFilter("\"IsActive\" IS TRUE");

                    b.ToTable("Products", null, t =>
                        {
                            t.HasCheckConstraint("Products_Stock_Price_check", "\"Stock\" >= 0 AND \"Price\" >= 0");
                        });
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.ProductImage", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("Id")
                        .HasDefaultValueSql("gen_random_uuid()");

                    b.Property<string>("Alt")
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)")
                        .HasColumnName("Alt");

                    b.Property<Guid>("ProductId")
                        .HasColumnType("uuid")
                        .HasColumnName("ProductId");

                    b.Property<int>("SortOrder")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasDefaultValue(0)
                        .HasColumnName("SortOrder");

                    b.Property<string>("Url")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("Url");

                    b.HasKey("Id");

                    b.HasIndex("ProductId")
                        .HasDatabaseName("IX_ProductImages_ProductId");

                    b.ToTable("ProductImages", (string)null);
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.ProductRelated", b =>
                {
                    b.Property<Guid>("ProductId")
                        .HasColumnType("uuid")
                        .HasColumnName("ProductId");

                    b.Property<Guid>("RelatedProductId")
                        .HasColumnType("uuid")
                        .HasColumnName("RelatedProductId");

                    b.HasKey("ProductId", "RelatedProductId");

                    b.HasIndex("RelatedProductId");

                    b.ToTable("ProductRelated", null, t =>
                        {
                            t.HasCheckConstraint("ProductRelated_NotSelf_check", "\"ProductId\" <> \"RelatedProductId\"");
                        });
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Supplier", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("Id")
                        .HasDefaultValueSql("gen_random_uuid()");

                    b.Property<string>("City")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("City");

                    b.Property<string>("Contact")
                        .HasMaxLength(150)
                        .HasColumnType("character varying(150)")
                        .HasColumnName("Contact");

                    b.Property<bool>("IsActive")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("boolean")
                        .HasDefaultValue(true)
                        .HasColumnName("IsActive");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("Name");

                    b.Property<string>("Notes")
                        .HasColumnType("text")
                        .HasColumnName("Notes");

                    b.HasKey("Id");

                    b.ToTable("Suppliers", (string)null);
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.CampaignProduct", b =>
                {
                    b.HasOne("EcommerceBackend.Domain.Entities.Campaign", "Campaign")
                        .WithMany("Products")
                        .HasForeignKey("CampaignId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("CampaignProducts_CampaignId_fkey");

                    b.HasOne("EcommerceBackend.Domain.Entities.Product", "Product")
                        .WithMany("CampaignLinks")
                        .HasForeignKey("ProductId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("CampaignProducts_ProductId_fkey");

                    b.Navigation("Campaign");

                    b.Navigation("Product");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Category", b =>
                {
                    b.HasOne("EcommerceBackend.Domain.Entities.Category", "Parent")
                        .WithMany("Children")
                        .HasForeignKey("ParentId")
                        .OnDelete(DeleteBehavior.SetNull)
                        .HasConstraintName("Categories_ParentId_fkey");

                    b.Navigation("Parent");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Order", b =>
                {
                    b.HasOne("EcommerceBackend.Domain.Entities.Campaign", "Campaign")
                        .WithMany("Orders")
                        .HasForeignKey("CampaignId")
                        .OnDelete(DeleteBehavior.SetNull)
                        .HasConstraintName("Orders_CampaignId_fkey");

                    b.Navigation("Campaign");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.OrderItem", b =>
                {
                    b.HasOne("EcommerceBackend.Domain.Entities.Order", "Order")
                        .WithMany("Items")
                        .HasForeignKey("OrderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("OrderItems_OrderId_fkey");

                    b.HasOne("EcommerceBackend.Domain.Entities.Product", "Product")
                        .WithMany("OrderItems")
                        .HasForeignKey("ProductId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("OrderItems_ProductId_fkey");

                    b.Navigation("Order");

                    b.Navigation("Product");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.OrderShipment", b =>
                {
                    b.HasOne("EcommerceBackend.Domain.Entities.Order", "Order")
                        .WithMany("Shipments")
                        .HasForeignKey("OrderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("OrderShipments_OrderId_fkey");

                    b.HasOne("EcommerceBackend.Domain.Entities.Supplier", "Supplier")
                        .WithMany("Shipments")
                        .HasForeignKey("SupplierId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("OrderShipments_SupplierId_fkey");

                    b.Navigation("Order");

                    b.Navigation("Supplier");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.OrderStatusHistory", b =>
                {
                    b.HasOne("EcommerceBackend.Domain.Entities.Admin", "ChangedByAdmin")
                        .WithMany("StatusChanges")
                        .HasForeignKey("ChangedByAdminId")
                        .OnDelete(DeleteBehavior.SetNull)
                        .HasConstraintName("OrderStatusHistory_ChangedByAdminId_fkey");

                    b.HasOne("EcommerceBackend.Domain.Entities.Order", "Order")
                        .WithMany("StatusHistory")
                        .HasForeignKey("OrderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("OrderStatusHistory_OrderId_fkey");

                    b.Navigation("ChangedByAdmin");

                    b.Navigation("Order");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Product", b =>
                {
                    b.HasOne("EcommerceBackend.Domain.Entities.Category", "Category")
                        .WithMany("Products")
                        .HasForeignKey("CategoryId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("Products_CategoryId_fkey");

                    b.HasOne("EcommerceBackend.Domain.Entities.Supplier", "Supplier")
                        .WithMany("Products")
                        .HasForeignKey("SupplierId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("Products_SupplierId_fkey");

                    b.Navigation("Category");

                    b.Navigation("Supplier");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.ProductImage", b =>
                {
                    b.HasOne("EcommerceBackend.Domain.Entities.Product", "Product")
                        .WithMany("Images")
                        .HasForeignKey("ProductId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("ProductImages_ProductId_fkey");

                    b.Navigation("Product");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.ProductRelated", b =>
                {
                    b.HasOne("EcommerceBackend.Domain.Entities.Product", "Product")
                        .WithMany("RelatedLinks")
                        .HasForeignKey("ProductId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("ProductRelated_ProductId_fkey");

                    b.HasOne("EcommerceBackend.Domain.Entities.Product", "RelatedProduct")
                        .WithMany()
                        .HasForeignKey("RelatedProductId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("ProductRelated_RelatedProductId_fkey");

                    b.Navigation("Product");

                    b.Navigation("RelatedProduct");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Admin", b =>
                {
                    b.Navigation("StatusChanges");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Campaign", b =>
                {
                    b.Navigation("Orders");

                    b.Navigation("Products");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Category", b =>
                {
                    b.Navigation("Children");

                    b.Navigation("Products");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Order", b =>
                {
                    b.Navigation("Items");

                    b.Navigation("Shipments");

                    b.Navigation("StatusHistory");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Product", b =>
                {
                    b.Navigation("CampaignLinks");

                    b.Navigation("Images");

                    b.Navigation("OrderItems");

                    b.Navigation("RelatedLinks");
                });

            modelBuilder.Entity("EcommerceBackend.Domain.Entities.Supplier", b =>
                {
                    b.Navigation("Products");

                    b.Navigation("Shipments");
                });
#pragma warning restore 612, 618
        }
    }
}
