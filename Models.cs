using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Negocia;

public class Cliente
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
}

public class Divida
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;
    public string Descricao { get; set; } = "";
    public long ValorCentavos { get; set; }
    public DateOnly Vencimento { get; set; }
    public Acordo? Acordo { get; set; }
    public List<Contato> Contatos { get; set; } = [];
}

public enum Canal { Telefone, WhatsApp, Email }
public enum Resultado { SemResposta, PediuParcelamento, RetornoAgendado, RecusouProposta }

public class Contato
{
    public int Id { get; set; }
    public int DividaId { get; set; }
    public DateOnly Data { get; set; }
    public Canal Canal { get; set; }
    public Resultado Resultado { get; set; }
    public string? Nota { get; set; }
}

public class Acordo
{
    public int Id { get; set; }
    public int DividaId { get; set; }
    public Divida Divida { get; set; } = null!;
    public int DescontoBase { get; set; }
    public DateOnly CriadoEm { get; set; }
    public List<Parcela> Parcelas { get; set; } = [];
}

public class Parcela
{
    public int Id { get; set; }
    public int AcordoId { get; set; }
    public Acordo Acordo { get; set; } = null!;
    public int Numero { get; set; }
    public long ValorCentavos { get; set; }
    public DateOnly Vencimento { get; set; }
    public DateOnly? PagaEm { get; set; }
}

public class NegociaDb(DbContextOptions<NegociaDb> options) : DbContext(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Divida> Dividas => Set<Divida>();
    public DbSet<Contato> Contatos => Set<Contato>();
    public DbSet<Acordo> Acordos => Set<Acordo>();
    public DbSet<Parcela> Parcelas => Set<Parcela>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Divida>().HasOne(d => d.Acordo).WithOne(a => a.Divida).HasForeignKey<Acordo>(a => a.DividaId);
        model.Entity<Parcela>().HasIndex(p => new { p.AcordoId, p.Numero }).IsUnique();
    }
}

public sealed class ClienteInput
{
    [Required(ErrorMessage = "Informe o nome."), StringLength(100, ErrorMessage = "Use até 100 caracteres.")]
    public string Nome { get; set; } = "";
}

public sealed class DividaInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Selecione um cliente.")]
    public int ClienteId { get; set; }
    [Required(ErrorMessage = "Informe a descrição."), StringLength(120, ErrorMessage = "Use até 120 caracteres.")]
    public string Descricao { get; set; } = "";
    [Required(ErrorMessage = "Informe o valor.")]
    public string Valor { get; set; } = "";
    [Required(ErrorMessage = "Informe o vencimento.")]
    public DateOnly? Vencimento { get; set; }
}

public sealed class ContatoInput
{
    [Required(ErrorMessage = "Selecione o canal.")]
    public Canal? Canal { get; set; }
    [Required(ErrorMessage = "Selecione o resultado.")]
    public Resultado? Resultado { get; set; }
    [StringLength(400, ErrorMessage = "Use até 400 caracteres.")]
    public string? Nota { get; set; }
}

public sealed class PropostaInput
{
    [Range(1, 12, ErrorMessage = "Escolha entre 1 e 12 parcelas.")]
    public int Quantidade { get; set; } = 3;
    public string? Desconto { get; set; }
    [Required(ErrorMessage = "Informe o primeiro vencimento.")]
    public DateOnly? PrimeiroVencimento { get; set; }
}

public record Proposta(int DescontoBase, List<Parcela> Parcelas)
{
    public long TotalCentavos => Parcelas.Sum(p => p.ValorCentavos);

    public static Proposta Calcular(long valor, int descontoBase, int quantidade, DateOnly primeiro)
    {
        var total = (long)decimal.Round(valor * (10000 - descontoBase) / 10000m, 0, MidpointRounding.AwayFromZero);
        var valorBase = total / quantidade;
        if (valorBase == 0)
            return new Proposta(descontoBase, []);

        var parcelas = new List<Parcela>();
        for (var i = 0; i < quantidade; i++)
        {
            parcelas.Add(new Parcela
            {
                Numero = i + 1,
                Vencimento = primeiro.AddMonths(i),
                ValorCentavos = i == quantidade - 1 ? total - valorBase * i : valorBase
            });
        }
        return new Proposta(descontoBase, parcelas);
    }
}

public static class Brasil
{
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    public static DateOnly Hoje => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Fuso).DateTime);
    public static string Dinheiro(long centavos) => (centavos / 100m).ToString("C");
    public static string Iso(DateOnly? data) => data?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    public static bool LerDecimal(string? texto, out decimal valor)
    {
        valor = 0;
        texto = texto?.Trim();
        return texto is { Length: <= 20 }
            && Regex.IsMatch(texto, @"^[0-9]+([.,][0-9]{1,2})?$")
            && decimal.TryParse(texto.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valor);
    }

    public static string Estado(Divida divida, DateOnly hoje)
    {
        if (divida.Acordo is null)
            return divida.Vencimento < hoje ? "Dívida vencida" : "Em aberto";
        if (divida.Acordo.Parcelas.All(p => p.PagaEm.HasValue))
            return "Quitado";
        return divida.Acordo.Parcelas.Any(p => p.PagaEm is null && p.Vencimento < hoje) ? "Acordo em atraso" : "Em acordo";
    }

    public static string Estado(Parcela parcela, DateOnly hoje)
        => parcela.PagaEm.HasValue ? "Paga" : parcela.Vencimento < hoje ? "Vencida" : "Pendente";

    public static string Rotulo(Resultado resultado) => resultado switch
    {
        Resultado.SemResposta => "Sem resposta",
        Resultado.PediuParcelamento => "Pediu parcelamento",
        Resultado.RetornoAgendado => "Retorno agendado",
        _ => "Recusou a proposta"
    };
}

public record PainelVm(
    List<Divida> Dividas, List<Parcela> Vencidas, int TotalDividas,
    int TotalClientes, long SaldoCentavos, DateOnly Hoje, string? Busca);

public record NovaDividaVm(List<Cliente> Clientes, DividaInput Form);

public record FichaVm(Divida Divida, DateOnly Hoje, PropostaInput Form, Proposta? Proposta = null);

public static class DemoData
{
    public static async Task Seed(NegociaDb db, DateOnly hoje, CancellationToken cancellationToken)
    {
        var marina = new Cliente { Nome = "Marina Costa (demo)" };
        var joao = new Cliente { Nome = "João Lima (demo)" };
        var aberta = new Divida
        {
            Cliente = marina,
            Descricao = "Mensalidade em atraso",
            ValorCentavos = 100_000,
            Vencimento = hoje.AddDays(-15),
            Contatos = [new Contato { Data = hoje.AddDays(-2), Canal = Canal.WhatsApp, Resultado = Resultado.PediuParcelamento }]
        };
        var antiga = new Divida
        {
            Cliente = joao,
            Descricao = "Compra parcelada",
            ValorCentavos = 72_000,
            Vencimento = hoje.AddMonths(-3),
            Acordo = new Acordo
            {
                CriadoEm = hoje.AddMonths(-2),
                Parcelas = Proposta.Calcular(72_000, 0, 3, hoje.AddDays(-12)).Parcelas
            }
        };
        db.Dividas.AddRange(aberta, antiga);
        await db.SaveChangesAsync(cancellationToken);
    }
}
