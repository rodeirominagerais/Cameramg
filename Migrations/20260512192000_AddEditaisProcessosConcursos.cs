using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cameramg.Migrations
{
    public partial class AddEditaisProcessosConcursos : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "concursos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    usuario_id = table.Column<long>(type: "bigint", nullable: true),
                    titulo = table.Column<string>(type: "text", nullable: false),
                    resumo = table.Column<string>(type: "text", nullable: true),
                    conteudo = table.Column<string>(type: "text", nullable: true),
                    numero = table.Column<string>(type: "text", nullable: true),
                    data_publicacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_fim = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => { table.PrimaryKey("PK_concursos", x => x.id); });

            migrationBuilder.CreateTable(
                name: "processos_seletivos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    usuario_id = table.Column<long>(type: "bigint", nullable: true),
                    titulo = table.Column<string>(type: "text", nullable: false),
                    resumo = table.Column<string>(type: "text", nullable: true),
                    conteudo = table.Column<string>(type: "text", nullable: true),
                    numero = table.Column<string>(type: "text", nullable: true),
                    data_publicacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_fim = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => { table.PrimaryKey("PK_processos_seletivos", x => x.id); });

            migrationBuilder.CreateTable(
                name: "concursos_arquivos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    descricao = table.Column<string>(type: "text", nullable: false),
                    data_arquivo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    caminho_relativo = table.Column<string>(type: "text", nullable: false),
                    nome_arquivo = table.Column<string>(type: "text", nullable: true),
                    extensao = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    concurso_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_concursos_arquivos", x => x.id);
                    table.ForeignKey("FK_concursos_arquivos_concursos_concurso_id", x => x.concurso_id, "concursos", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "processos_seletivos_arquivos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    descricao = table.Column<string>(type: "text", nullable: false),
                    data_arquivo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    caminho_relativo = table.Column<string>(type: "text", nullable: false),
                    nome_arquivo = table.Column<string>(type: "text", nullable: true),
                    extensao = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processo_seletivo_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processos_seletivos_arquivos", x => x.id);
                    table.ForeignKey("FK_processos_seletivos_arquivos_processos_seletivos_processo_seletivo_id", x => x.processo_seletivo_id, "processos_seletivos", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_concursos_ativo", "concursos", "ativo");
            migrationBuilder.CreateIndex("IX_concursos_data_publicacao", "concursos", "data_publicacao");
            migrationBuilder.CreateIndex("IX_concursos_status", "concursos", "status");
            migrationBuilder.CreateIndex("IX_concursos_arquivos_concurso_id", "concursos_arquivos", "concurso_id");
            migrationBuilder.CreateIndex("IX_processos_seletivos_ativo", "processos_seletivos", "ativo");
            migrationBuilder.CreateIndex("IX_processos_seletivos_data_publicacao", "processos_seletivos", "data_publicacao");
            migrationBuilder.CreateIndex("IX_processos_seletivos_status", "processos_seletivos", "status");
            migrationBuilder.CreateIndex("IX_processos_seletivos_arquivos_processo_seletivo_id", "processos_seletivos_arquivos", "processo_seletivo_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "concursos_arquivos");
            migrationBuilder.DropTable(name: "processos_seletivos_arquivos");
            migrationBuilder.DropTable(name: "concursos");
            migrationBuilder.DropTable(name: "processos_seletivos");
        }
    }
}
