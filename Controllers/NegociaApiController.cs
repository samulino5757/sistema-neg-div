using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Negocia;

[ApiController]
[Route("api")]
public class NegociaApiController(NegociaDb db) : ControllerBase
{
    [HttpGet("sessao")]
    public IActionResult Sessao([FromServices] IAntiforgery antiforgery)
    {
        var token = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = token.RequestToken });
    }

    [HttpGet("painel")]
    public async Task<IActionResult> Painel()
    {
        var hoje = Brasil.Hoje;
        var dividas = await Negociacao.Carteira(db);
        var pendentes = dividas.SelectMany(d => d.Acordo?.Parcelas ?? []).Where(p => p.PagaEm is null).ToList();
        return Ok(new
        {
            hoje,
            totalClientes = await db.Clientes.CountAsync(),
            saldoCentavos = dividas.Where(d => d.Acordo is null).Sum(d => d.ValorCentavos) + pendentes.Sum(p => p.ValorCentavos),
            dividas = dividas.Select(d => Resumo(d, hoje)),
            vencidas = pendentes.Where(p => p.Vencimento < hoje).OrderBy(p => p.Vencimento)
                .Select(p => new { p.Id, p.Numero, p.ValorCentavos, p.Vencimento, p.Acordo.DividaId, cliente = p.Acordo.Divida.Cliente.Nome })
        });
    }

    [HttpGet("clientes")]
    public async Task<IActionResult> Clientes()
        => Ok(await db.Clientes.AsNoTracking().OrderBy(c => c.Nome).ToListAsync());

    [HttpPost("clientes")]
    public async Task<IActionResult> CriarCliente(ClienteInput form)
    {
        var cliente = new Cliente { Nome = form.Nome.Trim() };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status201Created, cliente);
    }

    [HttpPost("dividas")]
    public async Task<IActionResult> CriarDivida(DividaInput form)
    {
        if (!Brasil.LerDecimal(form.Valor, out var valor) || valor is < 0.01m or > 100_000_000m)
            return BadRequest(new { mensagem = "Informe um valor entre R$ 0,01 e R$ 100 milhões, sem separador de milhar." });
        if (!await db.Clientes.AnyAsync(c => c.Id == form.ClienteId))
            return BadRequest(new { mensagem = "Selecione um cliente cadastrado." });
        var divida = new Divida
        {
            ClienteId = form.ClienteId,
            Descricao = form.Descricao.Trim(),
            ValorCentavos = (long)(valor * 100),
            Vencimento = form.Vencimento!.Value
        };
        db.Dividas.Add(divida);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Ficha), new { id = divida.Id }, new { divida.Id });
    }

    [HttpGet("dividas/{id:int}")]
    public async Task<IActionResult> Ficha(int id)
    {
        var divida = await Negociacao.BuscarDivida(db, id);
        if (divida is null)
            return NotFound(new { mensagem = "Dívida não encontrada." });
        var hoje = Brasil.Hoje;
        var acordo = divida.Acordo;
        return Ok(new
        {
            divida = Resumo(divida, hoje),
            hoje,
            primeiroVencimento = hoje.AddDays(7),
            contatos = divida.Contatos.OrderByDescending(c => c.Data).ThenByDescending(c => c.Id)
                .Select(c => new { c.Id, c.Data, canal = c.Canal == Canal.Email ? "E-mail" : c.Canal.ToString(), resultado = Brasil.Rotulo(c.Resultado), c.Nota }),
            acordo = acordo is null ? null : new
            {
                acordo.CriadoEm,
                desconto = acordo.DescontoBase / 100m,
                saldoCentavos = acordo.Parcelas.Where(p => p.PagaEm is null).Sum(p => p.ValorCentavos),
                totalCentavos = acordo.Parcelas.Sum(p => p.ValorCentavos),
                parcelas = acordo.Parcelas.OrderBy(p => p.Numero)
                    .Select(p => new { p.Id, p.Numero, p.Vencimento, p.ValorCentavos, p.PagaEm, estado = Brasil.Estado(p, hoje) })
            }
        });
    }

    [HttpPost("dividas/{id:int}/contatos")]
    public async Task<IActionResult> RegistrarContato(int id, ContatoInput form)
    {
        if (!await db.Dividas.AnyAsync(d => d.Id == id))
            return NotFound(new { mensagem = "Dívida não encontrada." });
        if (!Enum.IsDefined(form.Canal!.Value) || !Enum.IsDefined(form.Resultado!.Value))
            return BadRequest(new { mensagem = "Escolha um canal e um resultado válidos." });
        db.Contatos.Add(new Contato
        {
            DividaId = id, Data = Brasil.Hoje, Canal = form.Canal.Value, Resultado = form.Resultado.Value, Nota = form.Nota?.Trim()
        });
        await db.SaveChangesAsync();
        return Ok(new { mensagem = "Contato registrado." });
    }

    [HttpPost("dividas/{id:int}/simulacoes")]
    public Task<IActionResult> Simular(int id, PropostaInput form) => ProcessarProposta(id, form, confirmar: false);

    [HttpPost("dividas/{id:int}/acordo")]
    public Task<IActionResult> Confirmar(int id, PropostaInput form) => ProcessarProposta(id, form, confirmar: true);

    [HttpPost("dividas/{id:int}/parcelas/{parcelaId:int}/pagamento")]
    public async Task<IActionResult> Pagar(int id, int parcelaId)
    {
        if (!await db.Parcelas.AnyAsync(p => p.Id == parcelaId && p.Acordo.DividaId == id))
            return NotFound(new { mensagem = "Parcela não encontrada nesta dívida." });
        var alteradas = await Negociacao.DarBaixa(db, parcelaId, Brasil.Hoje);
        return Ok(new { mensagem = alteradas > 0 ? "Pagamento registrado." : "Esta parcela já está paga." });
    }

    private async Task<IActionResult> ProcessarProposta(int id, PropostaInput form, bool confirmar)
    {
        var divida = await Negociacao.BuscarDivida(db, id);
        if (divida is null)
            return NotFound(new { mensagem = "Dívida não encontrada." });
        if (divida.Acordo is not null)
            return Conflict(new { mensagem = "Já existe um acordo para esta dívida." });
        var hoje = Brasil.Hoje;
        var proposta = Negociacao.Simular(divida, form, hoje, out var erros);
        if (proposta is null)
            return BadRequest(new { mensagem = erros.Values.First() });
        if (!confirmar)
            return Ok(new
            {
                proposta.TotalCentavos,
                descontoCentavos = divida.ValorCentavos - proposta.TotalCentavos,
                parcelas = proposta.Parcelas.Select(p => new { p.Numero, p.Vencimento, p.ValorCentavos })
            });

        if (!await Negociacao.Confirmar(db, id, proposta, hoje))
            return Conflict(new { mensagem = "Já existe um acordo para esta dívida." });
        return Ok(new { mensagem = "Acordo confirmado. As parcelas foram geradas." });
    }

    private static object Resumo(Divida d, DateOnly hoje) => new
    {
        d.Id, cliente = d.Cliente.Nome, d.Descricao, d.ValorCentavos, d.Vencimento, estado = Brasil.Estado(d, hoje)
    };
}
