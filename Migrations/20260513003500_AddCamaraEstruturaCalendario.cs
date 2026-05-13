using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cameramg.Migrations
{
    public partial class AddCamaraEstruturaCalendario : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "camara_estruturas_administrativas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    usuario_id = table.Column<long>(type: "bigint", nullable: true),
                    titulo = table.Column<string>(type: "text", nullable: false),
                    conteudo_html = table.Column<string>(type: "text", nullable: true),
                    imagem = table.Column<string>(type: "text", nullable: true),
                    arquivo = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => { table.PrimaryKey("PK_camara_estruturas_administrativas", x => x.id); });

            migrationBuilder.CreateTable(
                name: "camara_calendario_reunioes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    usuario_id = table.Column<long>(type: "bigint", nullable: true),
                    titulo = table.Column<string>(type: "text", nullable: false),
                    resumo = table.Column<string>(type: "text", nullable: true),
                    conteudo_html = table.Column<string>(type: "text", nullable: true),
                    data_reuniao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    local = table.Column<string>(type: "text", nullable: true),
                    arquivo = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => { table.PrimaryKey("PK_camara_calendario_reunioes", x => x.id); });

            migrationBuilder.CreateIndex("IX_camara_estruturas_administrativas_ativo", "camara_estruturas_administrativas", "ativo");
            migrationBuilder.CreateIndex("IX_camara_estruturas_administrativas_status", "camara_estruturas_administrativas", "status");
            migrationBuilder.CreateIndex("IX_camara_calendario_reunioes_ativo", "camara_calendario_reunioes", "ativo");
            migrationBuilder.CreateIndex("IX_camara_calendario_reunioes_status", "camara_calendario_reunioes", "status");
            migrationBuilder.CreateIndex("IX_camara_calendario_reunioes_data_reuniao", "camara_calendario_reunioes", "data_reuniao");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "camara_estruturas_administrativas");
            migrationBuilder.DropTable(name: "camara_calendario_reunioes");
        }
    }
}
