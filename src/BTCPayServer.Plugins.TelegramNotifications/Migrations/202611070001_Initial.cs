using BTCPayServer.Plugins.TelegramNotifications.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Plugins.TelegramNotifications.Migrations;

[DbContext(typeof(TelegramNotificationsDbContext))]
[Migration(Id)]
public sealed class Initial : Migration
{
    public const string Id = "202611070001_Initial";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TelegramNotificationEvents",
            columns: table => new
            {
                EventKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_TelegramNotificationEvents", x => x.EventKey));

        migrationBuilder.CreateTable(
            name: "TelegramNotificationActions",
            columns: table => new
            {
                Token = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                InvoiceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                StoreId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ChatId = table.Column<long>(type: "bigint", nullable: false),
                MessageId = table.Column<int>(type: "integer", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TelegramUserId = table.Column<long>(type: "bigint", nullable: true),
                TelegramUsername = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                PreviousStatus = table.Column<string>(type: "text", nullable: true),
                Result = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_TelegramNotificationActions", x => x.Token));

        migrationBuilder.CreateTable(
            name: "TelegramNotificationState",
            columns: table => new
            {
                Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_TelegramNotificationState", x => x.Key));

        migrationBuilder.CreateIndex("IX_TelegramNotificationEvents_CreatedAt", "TelegramNotificationEvents", "CreatedAt");
        migrationBuilder.CreateIndex("IX_TelegramNotificationActions_ExpiresAt", "TelegramNotificationActions", "ExpiresAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("TelegramNotificationActions");
        migrationBuilder.DropTable("TelegramNotificationEvents");
        migrationBuilder.DropTable("TelegramNotificationState");
    }
}
