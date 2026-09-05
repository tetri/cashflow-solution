using CashFlow.Transactions.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Transactions.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuracao de mapeamento objeto-relacional (ORM) da entidade Transaction para o PostgreSQL via Fluent API.
/// Define explicitamente os nomes de tabelas e colunas no padrao snake_case, restricoes de nulabilidade,
/// tipos de dados de precisao monetaria e indices estrategicos para performance e idempotencia.
/// </summary>
public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    /// <summary>
    /// Configura os metadados relacionais e indices da tabela 'transactions'.
    /// </summary>
    /// <param name="builder">Construtor de metadados da entidade Transaction.</param>
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        // Mapeamento explicito para a tabela 'transactions' em minusculo
        builder.ToTable("transactions");

        // Chave primaria identificadora (UUID gerado no dominio)
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .IsRequired();

        // Identificador do comerciante para particionamento logico e consultas
        builder.Property(t => t.MerchantId)
            .HasColumnName("merchant_id")
            .HasMaxLength(50)
            .IsRequired();

        // Valor financeiro com precisao decimal exata de 18 digitos e 2 casas decimais
        builder.Property(t => t.Amount)
            .HasColumnName("amount")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        // Conversao bidirecional do enum TransactionType para string legivel ('Credit' ou 'Debit')
        builder.Property(t => t.Type)
            .HasColumnName("type")
            .HasMaxLength(10)
            .HasConversion(
                tipoEnum => tipoEnum.ToString(),
                tipoString => Enum.Parse<TransactionType>(tipoString)
            )
            .IsRequired();

        // Descricao historica do lancamento comercial
        builder.Property(t => t.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();

        // Carimbo de data/hora com fuso horario UTC para consistencia cronologica
        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Chave de idempotencia opcional normalizada para prevencao de duplicatas
        builder.Property(t => t.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128)
            .IsRequired(false);

        // Indice B-Tree composto para acelerar filtros combinados por comerciante e intervalo de datas
        builder.HasIndex(t => new { t.MerchantId, t.CreatedAt })
            .HasDatabaseName("idx_transactions_merchant_date");

        // Indice unico parcial no PostgreSQL: assegura que se idempotency_key for informada,
        // nao haja duas linhas com o mesmo valor, permitindo multiplos valores nulos
        builder.HasIndex(t => t.IdempotencyKey)
            .HasDatabaseName("idx_transactions_idempotency_key")
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL");
    }
}
