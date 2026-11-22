using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Negocia;

public static class Negociacao
{
    public static Proposta? Simular(Divida divida, PropostaInput form, DateOnly hoje, out Dictionary<string, string> erros)
    {
        erros = new();
        if (form.Quantidade is < 1 or > 12)
            erros[nameof(form.Quantidade)] = "Escolha entre 1 e 12 parcelas.";
        if (form.PrimeiroVencimento is not DateOnly primeiro || primeiro < hoje || primeiro > hoje.AddYears(10))
            erros[nameof(form.PrimeiroVencimento)] = "Escolha uma data entre hoje e os próximos 10 anos.";
        var desconto = 0m;
        if (!string.IsNullOrWhiteSpace(form.Desconto) &&
            (!Brasil.LerDecimal(form.Desconto, out desconto) || desconto > 99.99m))
            erros[nameof(form.Desconto)] = "Informe um desconto entre 0 e 99,99%.";
        if (erros.Count > 0)
            return null;

        var proposta = Proposta.Calcular(divida.ValorCentavos, (int)(desconto * 100),
            form.Quantidade, form.PrimeiroVencimento!.Value);
        if (proposta.Parcelas.Count > 0)
            return proposta;

        erros[nameof(form.Quantidade)] = "Cada parcela precisa ter pelo menos R$ 0,01. Reduza o desconto ou o número de parcelas.";
        return null;
    }

    public static async Task<bool> Confirmar(NegociaDb db, int dividaId, Proposta proposta, DateOnly hoje)
    {
        db.Acordos.Add(new Acordo
        {
            DividaId = dividaId,
            CriadoEm = hoje,
            DescontoBase = proposta.DescontoBase,
            Parcelas = proposta.Parcelas
        });
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            return false;
        }
    }

    public static Task<int> DarBaixa(NegociaDb db, int parcelaId, DateOnly hoje)
        => db.Parcelas.Where(p => p.Id == parcelaId && p.PagaEm == null)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.PagaEm, (DateOnly?)hoje));

    public static Task<Divida?> BuscarDivida(NegociaDb db, int id) => db.Dividas.AsNoTracking().AsSplitQuery()
        .Include(d => d.Cliente).Include(d => d.Contatos)
        .Include(d => d.Acordo).ThenInclude(a => a!.Parcelas)
        .SingleOrDefaultAsync(d => d.Id == id);

    public static Task<List<Divida>> Carteira(NegociaDb db) => db.Dividas.AsNoTracking()
        .Include(d => d.Cliente).Include(d => d.Acordo).ThenInclude(a => a!.Parcelas)
        .OrderByDescending(d => d.Id).ToListAsync();
}
