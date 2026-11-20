"""Exercise the MVC routes against a fresh demo database."""

import html
import re
import sys
import time
from http.cookiejar import CookieJar
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urlsplit
from urllib.request import HTTPCookieProcessor, build_opener


def main(base):
    browser = build_opener(HTTPCookieProcessor(CookieJar()))

    def get(path):
        with browser.open(base + path, timeout=5) as response:
            return html.unescape(response.read().decode("utf-8"))

    def post(path, page, **fields):
        token = field(page, "__RequestVerificationToken")
        data = urlencode({**fields, "__RequestVerificationToken": token}).encode()
        with browser.open(base + path, data, timeout=5) as response:
            return urlsplit(response.url).path, html.unescape(response.read().decode("utf-8"))

    for attempt in range(30):
        try:
            dashboard = get("/")
            break
        except HTTPError:
            raise
        except URLError:
            if attempt == 29:
                raise
            time.sleep(1)

    assert "Painel de cobrança" in dashboard
    assert "Marina Costa (demo)" in dashboard
    assert "João Lima (demo)" in dashboard
    assert "João Lima (demo)" in get("/?busca=joao")
    assert "Marina Costa (demo)" not in get("/?busca=joao")
    assert "Ver acordo" in dashboard

    _, debt_form = post("/Painel/NovoCliente", get("/Painel/NovoCliente"), Nome="Ana Demo")
    selected = re.search(r'<option value="(\d+)" selected="selected">Ana Demo</option>', debt_form)
    assert selected, "O cliente novo deve sair selecionado no cadastro da dívida"
    path, debt = post("/Painel/NovaDivida", debt_form, **{
        "Form.ClienteId": selected[1],
        "Form.Descricao": "Mensalidade teste",
        "Form.Valor": "1000,00",
        "Form.Vencimento": "2026-01-01",
    })
    assert "Dívida cadastrada" in debt and "Mensalidade teste" in debt
    debt_id = path.rsplit("/", 1)[-1]

    _, debt = post(f"/Painel/Contato/{debt_id}", debt, Canal="WhatsApp",
                   Resultado="PediuParcelamento", Nota="Atendimento de teste")
    assert "Contato registrado" in debt and "Atendimento de teste" in debt

    proposal_url = f"/Painel/Proposta/{debt_id}"
    proposal = {
        "Form.Quantidade": "3",
        "Form.Desconto": "0",
        "Form.PrimeiroVencimento": field(debt, "Form.PrimeiroVencimento"),
    }
    _, invalid = post(proposal_url, debt, etapa="simular", **{**proposal, "Form.Quantidade": "13"})
    assert "Escolha entre 1 e 12 parcelas" in invalid

    _, preview = post(proposal_url, debt, etapa="simular", **proposal)
    assert preview.count("333,33") == 2 and preview.count("333,34") == 1
    assert "Simular acordo" in get(path), "A simulação não deve salvar um acordo"

    proposal["Form.Desconto"] = "10"
    _, preview = post(proposal_url, debt, etapa="simular", **proposal)
    assert "900,00" in preview and preview.count("300,00") == 3
    _, agreement = post(proposal_url, preview, etapa="confirmar", **proposal)
    assert "Acordo confirmado" in agreement
    _, duplicate = post(proposal_url, preview, etapa="confirmar", **proposal)
    assert "Já existe um acordo" in duplicate

    for expected_balance in ("600,00", "300,00", "0,00"):
        parcel_id = field(agreement, "parcelaId")
        _, agreement = post(f"/Painel/Pagar/{debt_id}", agreement, parcelaId=parcel_id)
        assert "Pagamento registrado" in agreement
        assert f'<span>Saldo do acordo</span><strong>R$ {expected_balance}</strong>' in agreement.replace("\xa0", " ")
        _, paid_twice = post(f"/Painel/Pagar/{debt_id}", agreement, parcelaId=parcel_id)
        assert "Esta parcela já está paga" in paid_twice

    assert "Quitado" in get(path)
    print("Painel, busca, cadastros, contato, simulação, desconto, confirmação e pagamentos verificados.")


def field(page, name):
    tag = re.search(rf'<input\b[^>]*\bname="{re.escape(name)}"[^>]*>', page)
    assert tag, f"Campo ausente: {name}"
    value = re.search(r'\bvalue="([^"]*)"', tag[0])
    assert value, f"Campo sem valor: {name}"
    return value[1]


if __name__ == "__main__":
    main(sys.argv[1])
