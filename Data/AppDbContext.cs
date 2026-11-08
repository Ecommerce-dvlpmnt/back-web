using EcommerceBackend.Domain;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductRelated> ProductRelated => Set<ProductRelated>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignProduct> CampaignProducts => Set<CampaignProduct>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderShipment> OrderShipments => Set<OrderShipment>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum("order_status", new[]
            { "New", "Confirmed", "Packed", "Shipped", "Delivered", "Cancelled", "Refused", "Returned" });
        modelBuilder.HasPostgresEnum("payment_status", new[] { "Pending", "Paid", "Failed", "Refunded" });
        modelBuilder.HasPostgresEnum("payment_method", new[] { "CashOnDelivery", "Online" });
        modelBuilder.HasPostgresEnum("shipment_status", new[]
            { "Pending", "Requested", "Confirmed", "Shipped", "Problem", "Cancelled" });

        ConfigureSuppliers(modelBuilder);
        ConfigureCategories(modelBuilder);
        ConfigureProducts(modelBuilder);
        ConfigureProductImages(modelBuilder);
        ConfigureProductRelated(modelBuilder);
        ConfigureCampaigns(modelBuilder);
        ConfigureCampaignProducts(modelBuilder);
        ConfigureOrders(modelBuilder);
        ConfigureOrderItems(modelBuilder);
        ConfigureOrderShipments(modelBuilder);
        ConfigureAdmins(modelBuilder);
        ConfigureOrderStatusHistory(modelBuilder);
    }

    private static void ConfigureSuppliers(ModelBuilder b)
    {
        var e = b.Entity<Supplier>();
        e.ToTable("Suppliers");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("Id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.Name).HasColumnName("Name").HasMaxLength(100).IsRequired();
        e.Property(x => x.Contact).HasColumnName("Contact").HasMaxLength(150);
        e.Property(x => x.City).HasColumnName("City").HasMaxLength(100);
        e.Property(x => x.Notes).HasColumnName("Notes");
        e.Property(x => x.IsActive).HasColumnName("IsActive").HasDefaultValue(true);
    }

    private static void ConfigureCategories(ModelBuilder b)
    {
        var e = b.Entity<Category>();
        e.ToTable("Categories");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("Id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.Name).HasColumnName("Name").HasMaxLength(100).IsRequired();
        e.Property(x => x.Slug).HasColumnName("Slug").HasMaxLength(100).IsRequired();
        e.Property(x => x.ParentId).HasColumnName("ParentId");
        e.Property(x => x.SortOrder).HasColumnName("SortOrder").HasDefaultValue(0);
        e.Property(x => x.IsActive).HasColumnName("IsActive").HasDefaultValue(true);
        e.Property(x => x.FilterSchema).HasColumnName("FilterSchema").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        e.Property(x => x.MetaTitle).HasColumnName("MetaTitle").HasMaxLength(255);
        e.Property(x => x.MetaDescription).HasColumnName("MetaDescription");

        e.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("Categories_Slug_key");

        e.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .HasConstraintName("Categories_ParentId_fkey")
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureProducts(ModelBuilder b)
    {
        var e = b.Entity<Product>();
        e.ToTable("Products");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("Id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.CategoryId).HasColumnName("CategoryId");
        e.Property(x => x.SupplierId).HasColumnName("SupplierId");
        e.Property(x => x.Sku).HasColumnName("Sku").HasMaxLength(50).IsRequired();
        e.Property(x => x.Name).HasColumnName("Name").HasMaxLength(200).IsRequired();
        e.Property(x => x.Slug).HasColumnName("Slug").HasMaxLength(200).IsRequired();
        e.Property(x => x.Description).HasColumnName("Description");
        e.Property(x => x.DropPrice).HasColumnName("DropPrice").HasPrecision(10, 2).HasDefaultValue(0m);
        e.Property(x => x.Price).HasColumnName("Price").HasPrecision(10, 2).IsRequired();
        e.Property(x => x.OldPrice).HasColumnName("OldPrice").HasPrecision(10, 2);
        e.Property(x => x.Stock).HasColumnName("Stock").HasDefaultValue(0);
        e.Property(x => x.StockUpdatedAt).HasColumnName("StockUpdatedAt").HasColumnType("timestamptz");
        e.Property(x => x.MainImageUrl).HasColumnName("MainImageUrl");
        e.Property(x => x.Attributes).HasColumnName("Attributes").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        e.Property(x => x.MetaTitle).HasColumnName("MetaTitle").HasMaxLength(255);
        e.Property(x => x.MetaDescription).HasColumnName("MetaDescription");
        e.Property(x => x.WeightKg).HasColumnName("WeightKg").HasPrecision(5, 2);
        e.Property(x => x.IsActive).HasColumnName("IsActive").HasDefaultValue(true);
        e.Property(x => x.CreatedAt).HasColumnName("CreatedAt").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        e.Property(x => x.UpdatedAt).HasColumnName("UpdatedAt").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");

        e.HasIndex(x => x.Sku).IsUnique().HasDatabaseName("Products_Sku_key");
        e.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("Products_Slug_key");

        e.HasOne(x => x.Category)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId)
            .HasConstraintName("Products_CategoryId_fkey")
            .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(x => x.Supplier)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.SupplierId)
            .HasConstraintName("Products_SupplierId_fkey")
            .OnDelete(DeleteBehavior.Restrict);

        e.ToTable(t => t.HasCheckConstraint("Products_Stock_Price_check", "\"Stock\" >= 0 AND \"Price\" >= 0"));

        // GIN index on jsonb Attributes column (product characteristics)
        // jsonb_path_ops is more compact and faster for the @> containment operator
        e.HasIndex(x => x.Attributes)
            .HasDatabaseName("IX_Products_Attributes_gin")
            .HasMethod("gin")
            .HasOperators("jsonb_path_ops");

        // Catalog: filter by category + sort by price ASC (partial: active only)
        e.HasIndex(x => new { x.CategoryId, x.Price })
            .IsDescending(false, false)
            .HasDatabaseName("IX_Products_Catalog_PriceAsc")
            .HasFilter("\"IsActive\" IS TRUE");

        // Catalog: filter by category + sort by price DESC (partial: active only)
        e.HasIndex(x => new { x.CategoryId, x.Price })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Products_Catalog_PriceDesc")
            .HasFilter("\"IsActive\" IS TRUE");

        e.HasIndex(x => x.SupplierId).HasDatabaseName("IX_Products_SupplierId");

        // Catalog: filter by category + sort by date (default date_desc, partial: active only)
        e.HasIndex(x => new { x.CategoryId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Products_Catalog_CreatedAt")
            .HasFilter("\"IsActive\" IS TRUE");

        // Sort by price without category filter (partial: active only)
        e.HasIndex(x => x.Price)
            .HasDatabaseName("IX_Products_Price")
            .HasFilter("\"IsActive\" IS TRUE");

        // ILIKE '%...%' search on Name / Sku (requires pg_trgm extension)
        e.HasIndex(x => x.Name)
            .HasDatabaseName("IX_Products_Name_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");
        e.HasIndex(x => x.Sku)
            .HasDatabaseName("IX_Products_Sku_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");
    }

    private static void ConfigureProductImages(ModelBuilder b)
    {
        var e = b.Entity<ProductImage>();
        e.ToTable("ProductImages");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("Id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.ProductId).HasColumnName("ProductId");
        e.Property(x => x.Url).HasColumnName("Url").IsRequired();
        e.Property(x => x.Alt).HasColumnName("Alt").HasMaxLength(255);
        e.Property(x => x.SortOrder).HasColumnName("SortOrder").HasDefaultValue(0);

        e.HasOne(x => x.Product)
            .WithMany(x => x.Images)
            .HasForeignKey(x => x.ProductId)
            .HasConstraintName("ProductImages_ProductId_fkey")
            .OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(x => x.ProductId).HasDatabaseName("IX_ProductImages_ProductId");
    }

    private static void ConfigureProductRelated(ModelBuilder b)
    {
        var e = b.Entity<ProductRelated>();
        e.ToTable("ProductRelated");
        e.HasKey(x => new { x.ProductId, x.RelatedProductId });
        e.Property(x => x.ProductId).HasColumnName("ProductId");
        e.Property(x => x.RelatedProductId).HasColumnName("RelatedProductId");

        e.HasOne(x => x.Product)
            .WithMany(x => x.RelatedLinks)
            .HasForeignKey(x => x.ProductId)
            .HasConstraintName("ProductRelated_ProductId_fkey")
            .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(x => x.RelatedProduct)
            .WithMany()
            .HasForeignKey(x => x.RelatedProductId)
            .HasConstraintName("ProductRelated_RelatedProductId_fkey")
            .OnDelete(DeleteBehavior.Cascade);

        e.ToTable(t => t.HasCheckConstraint("ProductRelated_NotSelf_check", "\"ProductId\" <> \"RelatedProductId\""));
    }

    private static void ConfigureCampaigns(ModelBuilder b)
    {
        var e = b.Entity<Campaign>();
        e.ToTable("Campaigns");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("Id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.Name).HasColumnName("Name").HasMaxLength(150).IsRequired();
        e.Property(x => x.Slug).HasColumnName("Slug").HasMaxLength(150).IsRequired();
        e.Property(x => x.StartsAt).HasColumnName("StartsAt").HasColumnType("timestamptz");
        e.Property(x => x.EndsAt).HasColumnName("EndsAt").HasColumnType("timestamptz");
        e.Property(x => x.BannerUrl).HasColumnName("BannerUrl").HasMaxLength(500);
        e.Property(x => x.IsActive).HasColumnName("IsActive").HasDefaultValue(true);

        e.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("Campaigns_Slug_key");

        e.ToTable(t => t.HasCheckConstraint("Campaigns_Period_check", "\"EndsAt\" > \"StartsAt\""));
    }

    private static void ConfigureCampaignProducts(ModelBuilder b)
    {
        var e = b.Entity<CampaignProduct>();
        e.ToTable("CampaignProducts");
        e.HasKey(x => new { x.CampaignId, x.ProductId });
        e.Property(x => x.CampaignId).HasColumnName("CampaignId");
        e.Property(x => x.ProductId).HasColumnName("ProductId");
        e.Property(x => x.PromoPrice).HasColumnName("PromoPrice").HasPrecision(10, 2);

        e.HasOne(x => x.Campaign)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.CampaignId)
            .HasConstraintName("CampaignProducts_CampaignId_fkey")
            .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(x => x.Product)
            .WithMany(x => x.CampaignLinks)
            .HasForeignKey(x => x.ProductId)
            .HasConstraintName("CampaignProducts_ProductId_fkey")
            .OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(x => x.ProductId).HasDatabaseName("IX_CampaignProducts_ProductId");
    }

    private static void ConfigureOrders(ModelBuilder b)
    {
        var e = b.Entity<Order>();
        e.ToTable("Orders");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("Id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.OrderNumber).HasColumnName("OrderNumber").UseIdentityAlwaysColumn();
        e.Property(x => x.CustomerFirstName).HasColumnName("CustomerFirstName").HasMaxLength(100).IsRequired();
        e.Property(x => x.CustomerLastName).HasColumnName("CustomerLastName").HasMaxLength(100).IsRequired();
        e.Property(x => x.CustomerPhone).HasColumnName("CustomerPhone").HasMaxLength(20).IsRequired();
        e.Property(x => x.CustomerComment).HasColumnName("CustomerComment");
        e.Property(x => x.DeliveryCityRef).HasColumnName("DeliveryCityRef").HasMaxLength(50);
        e.Property(x => x.DeliveryCityName).HasColumnName("DeliveryCityName").HasMaxLength(150);
        e.Property(x => x.DeliveryWarehouseRef).HasColumnName("DeliveryWarehouseRef").HasMaxLength(50);
        e.Property(x => x.DeliveryWarehouseName).HasColumnName("DeliveryWarehouseName").HasMaxLength(250);
        e.Property(x => x.PaymentMethod).HasColumnName("PaymentMethod").HasColumnType("payment_method").HasDefaultValueSql("'CashOnDelivery'");
        e.Property(x => x.PaymentProvider).HasColumnName("PaymentProvider").HasMaxLength(20);
        e.Property(x => x.PaymentStatus).HasColumnName("PaymentStatus").HasColumnType("payment_status").HasDefaultValueSql("'Pending'");
        e.Property(x => x.OrderStatus).HasColumnName("OrderStatus").HasColumnType("order_status").HasDefaultValueSql("'New'");
        e.Property(x => x.ExternalPaymentId).HasColumnName("ExternalPaymentId").HasMaxLength(100);
        e.Property(x => x.TotalPrice).HasColumnName("TotalPrice").HasPrecision(10, 2).IsRequired();
        e.Property(x => x.TotalProfit).HasColumnName("TotalProfit").HasPrecision(10, 2).IsRequired();
        e.Property(x => x.UtmSource).HasColumnName("UtmSource").HasMaxLength(100);
        e.Property(x => x.UtmMedium).HasColumnName("UtmMedium").HasMaxLength(100);
        e.Property(x => x.UtmCampaign).HasColumnName("UtmCampaign").HasMaxLength(100);
        e.Property(x => x.CampaignId).HasColumnName("CampaignId");
        e.Property(x => x.AdminComment).HasColumnName("AdminComment");
        e.Property(x => x.CreatedAt).HasColumnName("CreatedAt").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        e.Property(x => x.UpdatedAt).HasColumnName("UpdatedAt").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");

        e.HasIndex(x => x.OrderNumber).IsUnique().HasDatabaseName("Orders_OrderNumber_key");
        e.HasIndex(x => x.CustomerPhone).HasDatabaseName("IX_Orders_CustomerPhone");
        e.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_Orders_CreatedAt").IsDescending();
        e.HasIndex(x => x.OrderStatus)
            .HasDatabaseName("IX_Orders_Active")
            .HasFilter("\"OrderStatus\" IN ('New','Confirmed','Packed')");
        e.HasIndex(x => x.CampaignId).HasDatabaseName("IX_Orders_CampaignId");
        e.HasIndex(x => x.ExternalPaymentId)
            .IsUnique()
            .HasDatabaseName("IX_Orders_ExternalPaymentId")
            .HasFilter("\"ExternalPaymentId\" IS NOT NULL");

        e.HasOne(x => x.Campaign)
            .WithMany(x => x.Orders)
            .HasForeignKey(x => x.CampaignId)
            .HasConstraintName("Orders_CampaignId_fkey")
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureOrderItems(ModelBuilder b)
    {
        var e = b.Entity<OrderItem>();
        e.ToTable("OrderItems");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("Id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.OrderId).HasColumnName("OrderId");
        e.Property(x => x.ProductId).HasColumnName("ProductId");
        e.Property(x => x.ProductNameSnapshot).HasColumnName("ProductNameSnapshot").HasMaxLength(200).IsRequired();
        e.Property(x => x.Quantity).HasColumnName("Quantity");
        e.Property(x => x.UnitPrice).HasColumnName("UnitPrice").HasPrecision(10, 2).IsRequired();
        e.Property(x => x.UnitDropPrice).HasColumnName("UnitDropPrice").HasPrecision(10, 2).IsRequired();

        e.HasIndex(x => new { x.OrderId, x.ProductId })
            .IsUnique()
            .HasDatabaseName("OrderItems_OrderId_ProductId_key");

        e.HasOne(x => x.Order)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.OrderId)
            .HasConstraintName("OrderItems_OrderId_fkey")
            .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(x => x.Product)
            .WithMany(x => x.OrderItems)
            .HasForeignKey(x => x.ProductId)
            .HasConstraintName("OrderItems_ProductId_fkey")
            .OnDelete(DeleteBehavior.Restrict);

        e.ToTable(t => t.HasCheckConstraint("OrderItems_Quantity_check", "\"Quantity\" > 0"));

        e.HasIndex(x => x.ProductId).HasDatabaseName("IX_OrderItems_ProductId");
    }

    private static void ConfigureOrderShipments(ModelBuilder b)
    {
        var e = b.Entity<OrderShipment>();
        e.ToTable("OrderShipments");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("Id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.OrderId).HasColumnName("OrderId");
        e.Property(x => x.SupplierId).HasColumnName("SupplierId");
        e.Property(x => x.SupplierOrderRef).HasColumnName("SupplierOrderRef").HasMaxLength(100);
        e.Property(x => x.TrackingNumber).HasColumnName("TrackingNumber").HasMaxLength(20);
        e.Property(x => x.Status).HasColumnName("Status").HasColumnType("shipment_status").HasDefaultValueSql("'Pending'");
        e.Property(x => x.AdminComment).HasColumnName("AdminComment");
        e.Property(x => x.CreatedAt).HasColumnName("CreatedAt").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        e.Property(x => x.UpdatedAt).HasColumnName("UpdatedAt").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");

        e.HasIndex(x => new { x.OrderId, x.SupplierId })
            .IsUnique()
            .HasDatabaseName("OrderShipments_OrderId_SupplierId_key");

        e.HasOne(x => x.Order)
            .WithMany(x => x.Shipments)
            .HasForeignKey(x => x.OrderId)
            .HasConstraintName("OrderShipments_OrderId_fkey")
            .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(x => x.Supplier)
            .WithMany(x => x.Shipments)
            .HasForeignKey(x => x.SupplierId)
            .HasConstraintName("OrderShipments_SupplierId_fkey")
            .OnDelete(DeleteBehavior.Restrict);

        e.HasIndex(x => x.SupplierId).HasDatabaseName("IX_OrderShipments_SupplierId");
    }

    private static void ConfigureAdmins(ModelBuilder b)
    {
        var e = b.Entity<Admin>();
        e.ToTable("Admins");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("Id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.Name).HasColumnName("Name").HasMaxLength(100).IsRequired();
        e.Property(x => x.TelegramId).HasColumnName("TelegramId");
        e.Property(x => x.Role).HasColumnName("Role").HasMaxLength(50).HasDefaultValue("Manager");

        e.HasIndex(x => x.TelegramId).IsUnique().HasDatabaseName("Admins_TelegramId_key");
    }

    private static void ConfigureOrderStatusHistory(ModelBuilder b)
    {
        var e = b.Entity<OrderStatusHistory>();
        e.ToTable("OrderStatusHistory");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("Id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.OrderId).HasColumnName("OrderId");
        e.Property(x => x.OldStatus).HasColumnName("OldStatus").HasColumnType("order_status");
        e.Property(x => x.NewStatus).HasColumnName("NewStatus").HasColumnType("order_status");
        e.Property(x => x.ChangedByAdminId).HasColumnName("ChangedByAdminId");
        e.Property(x => x.Comment).HasColumnName("Comment");
        e.Property(x => x.CreatedAt).HasColumnName("CreatedAt").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");

        e.HasOne(x => x.Order)
            .WithMany(x => x.StatusHistory)
            .HasForeignKey(x => x.OrderId)
            .HasConstraintName("OrderStatusHistory_OrderId_fkey")
            .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(x => x.ChangedByAdmin)
            .WithMany(x => x.StatusChanges)
            .HasForeignKey(x => x.ChangedByAdminId)
            .HasConstraintName("OrderStatusHistory_ChangedByAdminId_fkey")
            .OnDelete(DeleteBehavior.SetNull);

        e.HasIndex(x => x.OrderId).HasDatabaseName("IX_OrderStatusHistory_OrderId");
    }

    public override int SaveChanges()
    {
        StampUpdatedAt();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampUpdatedAt();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampUpdatedAt()
    {
        foreach (var entry in ChangeTracker.Entries<ITrackableEntity>())
            entry.Entity.UpdatedAt = DateTime.UtcNow;
    }
}
