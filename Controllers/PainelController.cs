using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Negocia;

public class PainelController(NegociaDb db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? busca)
    {
        var hoje = Brasil.Hoje;
        var todas = await Negociacao.Carteira(db);

        busca = busca?.Trim();
        var exibidas = todas;
        if (!string.IsNullOrEmpty(busca))
        {
            var compare = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
            const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            exibidas = todas.Where(d => compare.IndexOf(d.Cliente.Nome, busca, options) >= 0
                                     || compare.IndexOf(d.Descricao, busca, options) >= 0).ToList();
        }

        var pendentes = todas.SelectMany(d => d.Acordo?.Parcelas ?? []).Where(p => p.PagaEm is null).ToList();
        var vencidas = pendentes.Where(p => p.Vencimento < hoje).OrderBy(p => p.Vencimento).ToList();
        var saldo = todas.Where(d => d.Acordo is null).Sum(d => d.ValorCentavos) + pendentes.Sum(p => p.ValorCentavos);
        return View(new PainelVm(exibidas, vencidas, todas.Count, await db.Clientes.CountAsync(), saldo, hoje, busca));
    }

    [HttpGet]
    public IActionResult NovoCliente() => View(new ClienteInput());

    [HttpPost]
    public async Task<IActionResult> NovoCliente(ClienteInput form)
    {
        if (!ModelState.IsValid)
            return View(form);

        var cliente = new Cliente { Nome = form.Nome.Trim() };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        TempData["Confirmacao"] = "Cliente cadastrado. Agora tu pode registrar a dívida.";
        return RedirectToAction(nameof(NovaDivida), new { clienteId = cliente.Id });
    }

    [HttpGet]
    public async Task<IActionResult> NovaDivida(int clienteId)
        => View(await FormularioDivida(new DividaInput { ClienteId = clienteId, Vencimento = Brasil.Hoje }));

    [HttpPost]
    public async Task<IActionResult> NovaDivida([Bind(Prefix = "Form")] DividaInput form)
    {
        if (!Brasil.LerDecimal(form.Valor, out var valor) || valor is < 0.01m or > 100_000_000m)
            ModelState.AddModelError("Form.Valor", "Informe um valor entre R$ 0,01 e R$ 100 milhões, sem separador de milhar.");
        if (!await db.Clientes.AnyAsync(c => c.Id == form.ClienteId))
            ModelState.AddModelError("Form.ClienteId", "Selecione um cliente cadastrado.");
        if (!ModelState.IsValid)
            return View(await FormularioDivida(form));

        var divida = new Divida
        {
            ClienteId = form.ClienteId,
            Descricao = form.Descricao.Trim(),
            ValorCentavos = (long)(valor * 100),
            Vencimento = form.Vencimento!.Value
        };
        db.Dividas.Add(divida);
        await db.SaveChangesAsync();
        TempData["Confirmacao"] = "Dívida cadastrada.";
        return RedirectToAction(nameof(Ficha), new { id = divida.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Ficha(int id)
    {
        var divida = await Negociacao.BuscarDivida(db, id);
        var hoje = Brasil.Hoje;
        return divida is null ? NotFound() : View(new FichaVm(divida, hoje, new PropostaInput
        {
            PrimeiroVencimento = hoje.AddDays(7)
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Contato(int id, ContatoInput form)
    {
        if (!await db.Dividas.AnyAsync(d => d.Id == id))
            return NotFound();

        if (!ModelState.IsValid || !Enum.IsDefined(form.Canal!.Value) || !Enum.IsDefined(form.Resultado!.Value))
        {
            TempData["Erro"] = "Escolha um canal e um resultado válidos. A observação pode ter até 400 caracteres.";
            return RedirectToAction(nameof(Ficha), "Painel", new { id }, "contatos");
        }

        db.Contatos.Add(new Contato
        {
            DividaId = id,
            Data = Brasil.Hoje,
            Canal = form.Canal.Value,
            Resultado = form.Resultado.Value,
            Nota = form.Nota?.Trim()
        });
        await db.SaveChangesAsync();
        TempData["Confirmacao"] = "Contato registrado.";
        return RedirectToAction(nameof(Ficha), "Painel", new { id }, "contatos");
    }

    [HttpPost]
    public async Task<IActionResult> Proposta(int id, [Bind(Prefix = "Form")] PropostaInput form, string etapa)
    {
        var divida = await Negociacao.BuscarDivida(db, id);
        if (divida is null)
            return NotFound();
        if (divida.Acordo is not null)
        {
            TempData["Erro"] = "Já existe um acordo para esta dívida.";
            return RedirectToAction(nameof(Ficha), new { id });
        }

        var hoje = Brasil.Hoje;
        if (!ModelState.IsValid)
            return View(nameof(Ficha), new FichaVm(divida, hoje, form));

        var proposta = Negociacao.Simular(divida, form, hoje, out var erros);
        if (proposta is null)
        {
            foreach (var (campo, erro) in erros)
                ModelState.AddModelError($"Form.{campo}", erro);
            return View(nameof(Ficha), new FichaVm(divida, hoje, form));
        }

        if (etapa != "confirmar")
            return View(nameof(Ficha), new FichaVm(divida, hoje, form, proposta));

        if (await Negociacao.Confirmar(db, divida.Id, proposta, hoje))
            TempData["Confirmacao"] = "Acordo confirmado. As parcelas foram geradas.";
        else
            TempData["Erro"] = "Já existe um acordo para esta dívida.";
        return RedirectToAction(nameof(Ficha), "Painel", new { id }, "parcelas");
    }

    [HttpPost]
    public async Task<IActionResult> Pagar(int id, int parcelaId)
    {
        var parcela = await db.Parcelas.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == parcelaId && p.Acordo.DividaId == id);
        if (parcela is null)
            return NotFound();

        var hoje = Brasil.Hoje;
        var atualizadas = await Negociacao.DarBaixa(db, parcela.Id, hoje);
        TempData["Confirmacao"] = atualizadas > 0 ? "Pagamento registrado." : "Esta parcela já está paga.";
        return RedirectToAction(nameof(Ficha), "Painel", new { id }, "parcelas");
    }

    private async Task<NovaDividaVm> FormularioDivida(DividaInput form)
        => new(await db.Clientes.AsNoTracking().OrderBy(c => c.Nome).ToListAsync(), form);

}
