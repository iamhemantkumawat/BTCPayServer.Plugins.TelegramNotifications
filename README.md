# BTCPay Server Telegram Notifications

Server-wide Telegram alerts for BTCPay Server invoices, with configurable metadata and protected invoice-status actions.

Created and maintained by **Hemant**.

This release targets **BTCPay Server 2.4.7 through 2.4.x**.

## Features

- Notifications for invoice creation, payment receipt, payment confirmation, paid-in-full/overpaid, expiration, partial payment, payment after expiration, settlement, invalidation, confirmation failure, and refunds.
- Amount requested, amount received, remaining or overpaid amount, payment method, transaction ID, destination address, order ID, store, invoice ID, and local time.
- Optional invoice metadata with a key allowlist or all safe fields.
- Optional buyer email, phone, and postal address fields.
- Multiple destination chats and multiple authorized Telegram administrators.
- All-store or selected-store scope.
- Per-event notification switches.
- **Mark Settled** and **Mark Invalid** Telegram buttons with an explicit confirmation step.
- One-time, expiring action tokens, Telegram-user allowlisting, and a persistent audit trail.
- Duplicate-event protection and persistent Telegram update offsets.
- Bot token encryption through BTCPay Server's Data Protection service.
- `/start` and `/status` health replies for configured administrators, with a generic rejection for unauthorized users.

## Install

1. In BTCPay Server, open **Manage Plugins**.
2. Upload `BTCPayServer.Plugins.TelegramNotifications.btcpay`.
3. Restart BTCPay Server when prompted.
4. As a server administrator, open **Server Settings → Telegram Notifications**.

The plugin is disabled by default. Installation alone does not contact Telegram.

## Configure Telegram

1. Create a dedicated Telegram bot with `@BotFather` and copy its new bot token.
2. Open the bot in Telegram and press **Start**. For a group, add the bot to the group.
3. Enter one or more destination chat IDs.
4. Enter the numeric Telegram user IDs allowed to use bot commands and press manual action buttons.
5. Select all stores or specific stores, choose events and metadata, then use **Save and send test**.

Use a newly rotated token if a token has ever appeared in a screenshot, chat, log, or source file. Never commit a bot token to the repository.

After saving, an authorized administrator can send `/start` or `/status` to confirm that the bot is active. Users whose Telegram ID or chat is not configured receive only a generic authorization error and no BTCPay details.

## Manual action safety

Manual action buttons are shown only when BTCPay reports that the invoice can be changed. The first tap displays a confirmation button; only an allowlisted Telegram user can confirm. Tokens are random, bound to the invoice and destination chat, expire, and cannot be reused.

**Mark Invalid does not reverse or refund a Bitcoin or other blockchain payment.** It changes BTCPay's accounting status only. Refunds must be handled separately and should always be verified against the original invoice and transaction.

## Metadata and privacy

Secret-like metadata keys containing terms such as `token`, `password`, `secret`, `seed`, or `private key` are always omitted. Buyer email, phone, and address are opt-in because Telegram chats may be less private than BTCPay Server.

## Build and test

The repository pins the BTCPay Server submodule to `v2.4.7`.

```bash
dotnet build src/BTCPayServer.Plugins.TelegramNotifications/BTCPayServer.Plugins.TelegramNotifications.csproj -c Release
dotnet test tests/BTCPayServer.Plugins.TelegramNotifications.Tests/BTCPayServer.Plugins.TelegramNotifications.Tests.csproj -c Release
```

Package the Release DLL at the root of a ZIP archive and give the archive the `.btcpay` extension.
