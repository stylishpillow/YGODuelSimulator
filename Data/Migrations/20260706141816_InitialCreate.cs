using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YGODuelSimulator.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    FrameType = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Race = table.Column<string>(type: "TEXT", nullable: true),
                    Archetype = table.Column<string>(type: "TEXT", nullable: true),
                    Attribute = table.Column<string>(type: "TEXT", nullable: true),
                    YgoprodeckUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Atk = table.Column<int>(type: "INTEGER", nullable: true),
                    Def = table.Column<int>(type: "INTEGER", nullable: true),
                    Level = table.Column<int>(type: "INTEGER", nullable: true),
                    Scale = table.Column<int>(type: "INTEGER", nullable: true),
                    LinkValue = table.Column<int>(type: "INTEGER", nullable: true),
                    BanTcg = table.Column<string>(type: "TEXT", nullable: true),
                    BanOcg = table.Column<string>(type: "TEXT", nullable: true),
                    BanGoat = table.Column<string>(type: "TEXT", nullable: true),
                    KonamiId = table.Column<long>(type: "INTEGER", nullable: true),
                    TcgDate = table.Column<string>(type: "TEXT", nullable: true),
                    OcgDate = table.Column<string>(type: "TEXT", nullable: true),
                    HasEffect = table.Column<bool>(type: "INTEGER", nullable: true),
                    Views = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardFormats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardId = table.Column<long>(type: "INTEGER", nullable: false),
                    Format = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardFormats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardFormats_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApiImageId = table.Column<long>(type: "INTEGER", nullable: false),
                    CardId = table.Column<long>(type: "INTEGER", nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ImageUrlSmall = table.Column<string>(type: "TEXT", nullable: false),
                    ImageUrlCropped = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardImages_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardLinkMarkers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardId = table.Column<long>(type: "INTEGER", nullable: false),
                    Marker = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardLinkMarkers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardLinkMarkers_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardId = table.Column<long>(type: "INTEGER", nullable: false),
                    CardmarketPrice = table.Column<string>(type: "TEXT", nullable: true),
                    TcgplayerPrice = table.Column<string>(type: "TEXT", nullable: true),
                    EbayPrice = table.Column<string>(type: "TEXT", nullable: true),
                    AmazonPrice = table.Column<string>(type: "TEXT", nullable: true),
                    CoolstuffincPrice = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardPrices_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardId = table.Column<long>(type: "INTEGER", nullable: false),
                    SetName = table.Column<string>(type: "TEXT", nullable: true),
                    SetCode = table.Column<string>(type: "TEXT", nullable: true),
                    SetRarity = table.Column<string>(type: "TEXT", nullable: true),
                    SetRarityCode = table.Column<string>(type: "TEXT", nullable: true),
                    SetPrice = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardSets_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardFormats_CardId",
                table: "CardFormats",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardImages_CardId",
                table: "CardImages",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardLinkMarkers_CardId",
                table: "CardLinkMarkers",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardPrices_CardId",
                table: "CardPrices",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Archetype",
                table: "Cards",
                column: "Archetype");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Name",
                table: "Cards",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Type",
                table: "Cards",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_CardSets_CardId",
                table: "CardSets",
                column: "CardId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardFormats");

            migrationBuilder.DropTable(
                name: "CardImages");

            migrationBuilder.DropTable(
                name: "CardLinkMarkers");

            migrationBuilder.DropTable(
                name: "CardPrices");

            migrationBuilder.DropTable(
                name: "CardSets");

            migrationBuilder.DropTable(
                name: "Cards");
        }
    }
}
