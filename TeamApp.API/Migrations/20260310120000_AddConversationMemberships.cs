using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamApp.API.Migrations
{
    public partial class AddConversationMemberships : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConversationType",
                table: "Channels",
                type: "text",
                nullable: false,
                defaultValue: "Group");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Channels",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConversationMembers",
                columns: table => new
                {
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMembers", x => new { x.ChannelId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ConversationMembers_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConversationMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Channels_CreatedByUserId",
                table: "Channels",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMembers_UserId",
                table: "ConversationMembers",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Channels_Users_CreatedByUserId",
                table: "Channels",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(@"
                UPDATE ""Channels""
                SET ""ConversationType"" = CASE
                    WHEN ""Name"" LIKE 'DM\_%\_%' ESCAPE '\' THEN 'Direct'
                    ELSE 'Group'
                END;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""ConversationMembers"" (""ChannelId"", ""UserId"", ""JoinedAt"")
                SELECT c.""Id"", u.""Id"", NOW()
                FROM ""Channels"" c
                JOIN ""Users"" u
                    ON u.""AdUpn"" = split_part(c.""Name"", '_', 2)
                    OR u.""AdUpn"" = split_part(c.""Name"", '_', 3)
                WHERE c.""ConversationType"" = 'Direct'
                ON CONFLICT DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""ConversationMembers"" (""ChannelId"", ""UserId"", ""JoinedAt"")
                SELECT c.""Id"", u.""Id"", NOW()
                FROM ""Channels"" c
                CROSS JOIN ""Users"" u
                WHERE c.""ConversationType"" = 'Group'
                ON CONFLICT DO NOTHING;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Channels_Users_CreatedByUserId",
                table: "Channels");

            migrationBuilder.DropTable(
                name: "ConversationMembers");

            migrationBuilder.DropIndex(
                name: "IX_Channels_CreatedByUserId",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "ConversationType",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Channels");
        }
    }
}
