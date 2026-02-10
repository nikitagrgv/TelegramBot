# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A Telegram bot for calorie/food consumption tracking, built with C# / .NET 9.0. Users can log food items with kcal values, view daily/all-time statistics, set calorie limits, and receive scheduled notifications. Supports both English and Russian commands.

## Build & Run Commands

```bash
# Build
dotnet build TelegramBot/TelegramBot.csproj

# Run
dotnet run --project TelegramBot/TelegramBot.csproj

# Docker
docker-compose up --build
```

## Configuration

The bot requires `BOT_TOKEN` and `ADMIN_ID`. These can be set via:
1. Environment variables: `BOT_TOKEN`, `ADMIN_ID`, `BASE_DIR` (defaults to `/app/data`)
2. Config file: `botconf.json` in `BASE_DIR` with `BotToken` and `AdminId` fields

## Architecture

- **Program.cs** — Entry point. Loads config (env vars > botconf.json), initializes database, starts bot with CancellationToken for graceful shutdown.
- **WeightBot.cs** — Core bot logic. Handles Telegram updates, parses commands via `[GeneratedRegex]`, dispatches to handler methods. Runs a background notification loop (every 10 min, sends at 11:00/16:00 user local time with 1.5h cooldown). Uses dependency injection (`IBotDatabase`, `IBotClient`, `TimeProvider`) for testability.
- **IBotDatabase.cs** / **BotDatabase.cs** — SQLite data layer interface and implementation using `System.Data.SQLite`. Manages `users` and `consumed` tables with automatic schema migrations (version tracking). All queries are parameterized and async.
- **IBotClient.cs** / **TelegramBotClientAdapter.cs** — Abstraction over Telegram message sending, allowing mock injection in tests.
- **ConsumedRowInfo.cs** — Record type for consumed item data.
- **Utils.cs** — String chunking utility for Telegram message limits.

## Key Patterns

- All times stored as UTC in SQLite (`"yyyy-MM-dd HH:mm:ss"`), converted to user timezone (integer hour offset) on display
- Commands support Russian aliases (e.g., `add`/`добавить`/`доб`/`д`)
- Telegram messages use `ParseMode.Html`
- Number parsing accepts both comma and dot as decimal separators
- Admin-only commands are gated by `_adminId` comparison
- Database uses foreign key cascade deletes; migrations add columns via `ALTER TABLE`

## Dependencies

- `Telegram.Bot` v22.5.1
- `System.Data.SQLite` v1.0.119

## Testing

```bash
# Run all tests
dotnet test TelegramBot.Tests/TelegramBot.Tests.csproj

# Run a single test
dotnet test TelegramBot.Tests/TelegramBot.Tests.csproj --filter "FullyQualifiedName~TestMethodName"
```

Test project uses xUnit, NSubstitute (mocking), and `Microsoft.Extensions.TimeProvider.Testing` (fake clock). Tests are organized as:
- **UtilsTests** — `SplitStringByChunks` utility
- **CommandParsingTests** — Regex matching and `TryParseDouble`
- **BotDatabaseTests** — Integration tests with real SQLite (temp file per test)
- **WeightBotTests** — Unit tests with mocked `IBotDatabase` and `IBotClient`