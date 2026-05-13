using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cameramg.Migrations
{
    public partial class AddAtividadeParlamentarTabelasSeparadas : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            CriarTabela(migrationBuilder, "atas_reunioes");
            CriarTabela(migrationBuilder, "portarias");
            CriarTabela(migrationBuilder, "requerimentos");
            CriarTabela(migrationBuilder, "convocacoes");
            CriarTabela(migrationBuilder, "indicacoes");
            CriarTabela(migrationBuilder, "mocoes");
            CriarTabela(migrationBuilder, "resolucoes");
            CriarTabela(migrationBuilder, "projetos_resolucoes");
            CriarTabela(migrationBuilder, "diplomas");
            CriarTabela(migrationBuilder, "decretos");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "decretos");
            migrationBuilder.DropTable(name: "diplomas");
            migrationBuilder.DropTable(name: "projetos_resolucoes");
            migrationBuilder.DropTable(name: "resolucoes");
            migrationBuilder.DropTable(name: "mocoes");
            migrationBuilder.DropTable(name: "indicacoes");
            migrationBuilder.DropTable(name: "convocacoes");
            migrationBuilder.DropTable(name: "requerimentos");
            migrationBuilder.DropTable(name: "portarias");
            migrationBuilder.DropTable(name: "atas_reunioes");
        }

        private static void CriarTabela(MigrationBuilder migrationBuilder, string nome)
        {
            migrationBuilder.CreateTable(
                name: nome,
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    usuario_id = table.Column<long>(type: "bigint", nullable: true),
                    titulo = table.Column<string>(type: "text", nullable: false),
                    resumo = table.Column<string>(type: "text", nullable: true),
                    conteudo = table.Column<string>(type: "text", nullable: true),
                    arquivo = table.Column<string>(type: "text", nullable: true),
                    numero = table.Column<string>(type: "text", nullable: true),
                    data_criacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "Publicado"),
                    ativo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey($"pk_{nome}", x => x.id);
                });

            migrationBuilder.CreateIndex(name: $"ix_{nome}_ativo", table: nome, column: "ativo");
            migrationBuilder.CreateIndex(name: $"ix_{nome}_status", table: nome, column: "status");
            migrationBuilder.CreateIndex(name: $"ix_{nome}_data_criacao", table: nome, column: "data_criacao");
        }
    }
}
