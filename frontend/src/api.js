let sessao;

export async function api(caminho, dados, signal) {
  const opcoes = { signal, cache: 'no-store' };
  if (dados !== undefined) {
    sessao ??= api('/sessao').catch(erro => {
      sessao = undefined;
      throw erro;
    });
    const { token } = await sessao;
    Object.assign(opcoes, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
      body: JSON.stringify(dados),
    });
  }
  const resposta = await fetch('/api' + caminho, opcoes);
  const corpo = resposta.headers.get('content-type')?.includes('json') ? await resposta.json() : null;
  if (!resposta.ok) {
    if (resposta.status === 400 && corpo === null) sessao = undefined;
    const erroDeCampo = Object.values(corpo?.errors ?? {}).flat()[0];
    throw new Error(corpo?.mensagem ?? erroDeCampo ?? 'Não foi possível concluir. Tente novamente.');
  }
  return corpo;
}
