using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TelegramBot.Migrations
{
    [DbContext(typeof(Program.AppDbContext))]
    [Migration("20250515185015_RenameMyColumn")]
    public partial class RenameMyColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "timezone",
                table: "users",
                newName: "time_zone");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "time_zone",
                table: "users",
                newName: "timezone");
        }
    }
}
