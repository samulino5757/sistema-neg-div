import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { api } from './api';
import '../../wwwroot/css/site.css';
import './react.css';

const moeda = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
const dinheiro = centavos => moeda.format(centavos / 100);
const data = dia => dia?.split('-').reverse().join('/');
const ficha = id => '#/dividas/' + id;
const normalizar = texto => texto.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();

function Situacao({ estado }) {
  return <span className="pill" data-state={estado}>{estado}</span>;
}

function Aviso({ texto, erro = false }) {
  return texto && <p role={erro ? 'alert' : 'status'} className={'notice ' + (erro ? 'error' : 'success')}>{texto}</p>;
}

function useDados(caminho) {
  const [resultado, setResultado] = useState({ dados: null, erro: '', carregando: true });
  const [versao, setVersao] = useState(0);
  useEffect(() => {
    if (!caminho) return;
    const controller = new AbortController();
    api(caminho, undefined, controller.signal).then(
      dados => setResultado({ dados, erro: '', carregando: false }),
      erro => {
        if (erro.name !== 'AbortError') setResultado({ dados: null, erro: erro.message, carregando: false });
      },
    );
    return () => controller.abort();
  }, [caminho, versao]);
  return { ...resultado, recarregar: () => setVersao(v => v + 1) };
}

function useAcao() {
  const [ocupado, setOcupado] = useState(false);
  const [erro, setErro] = useState('');
  async function executar(acao) {
    setOcupado(true);
    setErro('');
    try {
      await acao();
    } catch (falha) {
      setErro(falha.message);
    } finally {
      setOcupado(false);
    }
  }
  return { ocupado, erro, executar };
}

function Carregar({ consulta, children }) {
  if (consulta.carregando) return <p role="status">Carregando…</p>;
  if (consulta.erro) return <><Aviso texto={consulta.erro} erro /><button className="btn secondary" onClick={consulta.recarregar}>Tentar novamente</button></>;
  return children(consulta.dados);
}

function Cabecalho({ titulo, descricao, children }) {
  return <div className="page-heading"><div><h1>{titulo}</h1>{descricao && <p>{descricao}</p>}</div>{children}</div>;
}

function Metricas({ itens, detalhe }) {
  return <div className={detalhe ? 'summary detail' : 'summary'}>
    {itens.map(([titulo, valor, classe]) => <div className={'metric ' + (classe ?? '')} key={titulo}><span>{titulo}</span><strong>{valor}</strong></div>)}
  </div>;
}

function Tabela({ colunas, children }) {
  return <div className="table-wrap"><table>
    <thead><tr>{colunas.map(titulo => <th scope="col" key={titulo}>{titulo}</th>)}</tr></thead>
    <tbody>{children}</tbody>
  </table></div>;
}

function Painel() {
  const consulta = useDados('/painel');
  const [busca, setBusca] = useState('');
  const [situacao, setSituacao] = useState('');
  return <>
    <Cabecalho titulo="Painel de cobrança" descricao="Consulte dívidas, negocie acordos e acompanhe os pagamentos.">
      <div className="actions"><a className="btn secondary" href="#/clientes/novo">Novo cliente</a><a className="btn" href="#/dividas/nova">+ Nova dívida</a></div>
    </Cabecalho>
    <Carregar consulta={consulta}>{painel => {
      const termo = normalizar(busca.trim());
      const dividas = painel.dividas.filter(d => (!situacao || d.estado === situacao) && normalizar(d.cliente + ' ' + d.descricao).includes(termo));
      return <>
        <Metricas itens={[
          ['Saldo a receber', dinheiro(painel.saldoCentavos)],
          ['Dívidas cadastradas', painel.dividas.length],
          ['Clientes cadastrados', painel.totalClientes],
          ['Parcelas vencidas', painel.vencidas.length, 'warning'],
        ]} />
        <section className="card" aria-labelledby="carteira">
          <div className="section-heading"><div><h2 id="carteira">Carteira de dívidas</h2><p>{dividas.length} de {painel.dividas.length} registros</p></div>
            <div className="filters">
              <label>Buscar dívida<input type="search" placeholder="Cliente ou descrição" value={busca} onChange={evento => setBusca(evento.target.value)} /></label>
              <label>Situação<select value={situacao} onChange={evento => setSituacao(evento.target.value)}>
                <option value="">Todas</option>
                {['Em aberto', 'Dívida vencida', 'Em acordo', 'Acordo em atraso', 'Quitado'].map(estado => <option key={estado}>{estado}</option>)}
              </select></label>
            </div>
          </div>
          {dividas.length ? <Tabela colunas={['Cliente / dívida', 'Valor original', 'Vencimento', 'Situação', 'Ficha']}>
            {dividas.map(divida => <tr key={divida.id}>
              <td><strong>{divida.cliente}</strong><small>{divida.descricao}</small></td>
              <td className="money">{dinheiro(divida.valorCentavos)}</td><td>{data(divida.vencimento)}</td>
              <td><Situacao estado={divida.estado} /></td><td><a href={ficha(divida.id)} aria-label={'Abrir ficha de ' + divida.cliente}>Abrir ficha →</a></td>
            </tr>)}
          </Tabela> : <p className="empty">Nenhuma dívida encontrada.</p>}
        </section>
        <section className="card" aria-labelledby="atrasos">
          <div className="section-heading"><h2 id="atrasos">Parcelas vencidas</h2></div>
          {painel.vencidas.length ? <Tabela colunas={['Cliente', 'Parcela', 'Vencimento', 'Valor', 'Acordo']}>
            {painel.vencidas.map(p => <tr key={p.id}><td><strong>{p.cliente}</strong></td><td>{p.numero}</td><td>{data(p.vencimento)}</td><td className="money">{dinheiro(p.valorCentavos)}</td><td><a href={ficha(p.dividaId)}>Ver acordo</a></td></tr>)}
          </Tabela> : <p className="empty">Nenhuma parcela vencida.</p>}
        </section>
      </>;
    }}</Carregar>
  </>;
}

function Cadastro({ cliente, clienteId }) {
  const acao = useAcao();
  const consulta = useDados(cliente ? null : '/clientes');
  const titulo = cliente ? 'Novo cliente' : 'Nova dívida';
  function salvar(evento) {
    evento.preventDefault();
    const dados = Object.fromEntries(new FormData(evento.currentTarget));
    acao.executar(async () => {
      if (cliente) {
        const novo = await api('/clientes', dados);
        window.location.hash = '#/dividas/nova?cliente=' + novo.id;
      } else {
        dados.clienteId = Number(dados.clienteId);
        const nova = await api('/dividas', dados);
        window.location.hash = ficha(nova.id);
      }
    });
  }
  const formulario = campos => <form className="stack" onSubmit={salvar}>
    <Aviso texto={acao.erro} erro />
    {campos}
    <div className="actions"><button className="btn" disabled={acao.ocupado}>{cliente ? 'Salvar cliente' : 'Salvar dívida'}</button><a className="btn secondary" href="#/">Cancelar</a></div>
  </form>;
  return <>
    <a className="back" href="#/">← Painel</a>
    <Cabecalho titulo={titulo} descricao={cliente ? 'Use um nome fictício para testar o cadastro.' : 'Vincule a dívida a um cliente cadastrado.'} />
    <section className="card narrow">
      {cliente ? formulario(<label>Nome do cliente<input name="nome" required maxLength="100" /></label>)
        : <Carregar consulta={consulta}>{clientes => clientes.length ? formulario(<>
          <label>Cliente<select name="clienteId" defaultValue={clienteId ?? ''} required>
            <option value="">Selecione um cliente</option>
            {clientes.map(c => <option value={c.id} key={c.id}>{c.nome}</option>)}
          </select></label>
          <label>Descrição<input name="descricao" required maxLength="120" /></label>
          <div className="fields two"><label>Valor original (R$)<input name="valor" inputMode="decimal" placeholder="1000,00" maxLength="20" required /></label>
            <label>Vencimento<input name="vencimento" type="date" required /></label></div>
        </>) : <a className="btn" href="#/clientes/novo">Cadastrar cliente</a>}</Carregar>}
    </section>
  </>;
}

function Simulador({ registro, atualizar }) {
  const acao = useAcao();
  const [simulacao, setSimulacao] = useState(null);
  const caminho = '/dividas/' + registro.divida.id;
  function simular(evento) {
    evento.preventDefault();
    const campos = Object.fromEntries(new FormData(evento.currentTarget));
    campos.quantidade = Number(campos.quantidade);
    acao.executar(async () => {
      setSimulacao(null);
      setSimulacao({ campos, resultado: await api(caminho + '/simulacoes', campos) });
    });
  }
  function confirmar() {
    acao.executar(async () => {
      const resposta = await api(caminho + '/acordo', simulacao.campos);
      atualizar(resposta.mensagem);
      setSimulacao(null);
    });
  }
  return <section className="card" aria-labelledby="simulador">
    <div className="section-heading"><div><h2 id="simulador">Simular acordo</h2><p>Parcelamento mensal de 1 a 12 vezes, sem juros.</p></div></div>
    <Aviso texto={acao.erro} erro />
    <form className="stack" onSubmit={simular} onChange={() => setSimulacao(null)}>
      <fieldset className="stack" disabled={acao.ocupado}>
        <div className="fields two">
          <label>Parcelas<input name="quantidade" type="number" min="1" max="12" defaultValue="3" required /></label>
          <label>Desconto (%) <span className="optional">Opcional</span><input name="desconto" inputMode="decimal" placeholder="0" maxLength="20" /></label>
        </div>
        <label>Primeiro vencimento<input name="primeiroVencimento" type="date" min={registro.hoje} defaultValue={registro.primeiroVencimento} required /></label>
        <div><button className="btn">Calcular proposta</button></div>
      </fieldset>
    </form>
    {simulacao && <div className="proposal" aria-live="polite">
      <div className="section-heading"><div><h3>Proposta simulada</h3><p>Desconto de {dinheiro(simulacao.resultado.descontoCentavos)}</p></div>
        <strong className="money">{dinheiro(simulacao.resultado.totalCentavos)}</strong></div>
      <Tabela colunas={['Parcela', 'Vencimento', 'Valor']}>{simulacao.resultado.parcelas.map(p => <tr key={p.numero}><td>{p.numero}</td><td>{data(p.vencimento)}</td><td className="money">{dinheiro(p.valorCentavos)}</td></tr>)}</Tabela>
      <div className="confirmation"><button className="btn" disabled={acao.ocupado} onClick={confirmar}>Confirmar acordo</button></div>
    </div>}
  </section>;
}

function Acordo({ acordo, dividaId, atualizar }) {
  const acao = useAcao();
  function pagar(parcelaId) {
    acao.executar(async () => {
      const resposta = await api('/dividas/' + dividaId + '/parcelas/' + parcelaId + '/pagamento', {});
      atualizar(resposta.mensagem);
    });
  }
  return <section className="card" aria-labelledby="acordo">
    <div className="section-heading"><div><h2 id="acordo">Parcelas do acordo</h2><p>Confirmado em {data(acordo.criadoEm)} · Desconto de {acordo.desconto.toLocaleString('pt-BR')}%</p></div><strong className="money">{dinheiro(acordo.totalCentavos)}</strong></div>
    <Aviso texto={acao.erro} erro />
    <Tabela colunas={['Parcela', 'Vencimento', 'Valor', 'Situação', 'Pagamento']}>
      {acordo.parcelas.map(p => <tr key={p.id}><td>{p.numero}</td><td>{data(p.vencimento)}</td><td className="money">{dinheiro(p.valorCentavos)}</td><td><Situacao estado={p.estado} /></td>
        <td>{p.pagaEm ? <small>{data(p.pagaEm)}</small> : <button className="btn small secondary" aria-label={'Dar baixa na parcela ' + p.numero} disabled={acao.ocupado} onClick={() => pagar(p.id)}>Dar baixa</button>}</td></tr>)}
    </Tabela>
  </section>;
}

function Contatos({ registro, atualizar }) {
  const acao = useAcao();
  function registrar(evento) {
    evento.preventDefault();
    const form = evento.currentTarget;
    const campos = Object.fromEntries(new FormData(form));
    campos.canal = Number(campos.canal);
    campos.resultado = Number(campos.resultado);
    acao.executar(async () => {
      const resposta = await api('/dividas/' + registro.divida.id + '/contatos', campos);
      form.reset();
      atualizar(resposta.mensagem);
    });
  }
  return <section className="card" aria-labelledby="historico">
    <div className="section-heading"><h2 id="historico">Histórico de contatos</h2></div>
    <Aviso texto={acao.erro} erro />
    <form className="stack" onSubmit={registrar}>
      <div className="fields two">
        <label>Canal<select name="canal" required defaultValue="">
          <option value="">Selecione</option><option value="0">Telefone</option><option value="1">WhatsApp</option><option value="2">E-mail</option>
        </select></label>
        <label>Resultado<select name="resultado" required defaultValue="">
          <option value="">Selecione</option><option value="0">Sem resposta</option><option value="1">Pediu parcelamento</option><option value="2">Retorno agendado</option><option value="3">Recusou a proposta</option>
        </select></label>
      </div>
      <label>Observação <span className="optional">Opcional</span><textarea name="nota" maxLength="400" rows="2" /></label>
      <div><button className="btn secondary" disabled={acao.ocupado}>Registrar contato</button></div>
    </form>
    {registro.contatos.length ? <ul className="history">{registro.contatos.map(c => <li key={c.id}>
      <strong>{c.resultado}</strong><small>{data(c.data)} · {c.canal}</small>{c.nota && <p>{c.nota}</p>}
    </li>)}</ul> : <p className="empty">Ainda não há contatos para esta dívida.</p>}
  </section>;
}

function Ficha({ id }) {
  const consulta = useDados('/dividas/' + id);
  const [mensagem, setMensagem] = useState('');
  function atualizar(texto) {
    setMensagem(texto);
    consulta.recarregar();
  }
  return <>
    <a className="back" href="#/">← Painel</a>
    <Aviso texto={mensagem} />
    <Carregar consulta={consulta}>{registro => {
      const { divida, acordo } = registro;
      const itens = [['Valor original', dinheiro(divida.valorCentavos)], ['Vencimento original', data(divida.vencimento)]];
      if (acordo) itens.push(['Saldo do acordo', dinheiro(acordo.saldoCentavos)]);
      return <>
        <Cabecalho titulo={divida.cliente} descricao={divida.descricao}><Situacao estado={divida.estado} /></Cabecalho>
        <Metricas itens={itens} detalhe />
        <div className="detail-grid">
          {acordo ? <Acordo acordo={acordo} dividaId={id} atualizar={atualizar} /> : <Simulador registro={registro} atualizar={atualizar} />}
          <Contatos registro={registro} atualizar={atualizar} />
        </div>
      </>;
    }}</Carregar>
  </>;
}

function App() {
  const [rota, setRota] = useState(() => window.location.hash.slice(1) || '/');
  useEffect(() => {
    const navegar = () => {
      if (window.location.hash === '#conteudo') return;
      setRota(window.location.hash.slice(1) || '/');
      window.scrollTo(0, 0);
    };
    window.addEventListener('hashchange', navegar);
    return () => window.removeEventListener('hashchange', navegar);
  }, []);
  const [pagina, busca] = rota.split('?');
  const dividaId = pagina.match(/^\/dividas\/(\d+)$/)?.[1];
  let tela;
  if (pagina === '/') tela = <Painel />;
  else if (pagina === '/clientes/novo') tela = <Cadastro cliente />;
  else if (pagina === '/dividas/nova') tela = <Cadastro clienteId={new URLSearchParams(busca).get('cliente')} />;
  else if (dividaId) tela = <Ficha id={dividaId} />;
  else tela = <><Cabecalho titulo="Página não encontrada" /><a href="#/">Voltar ao painel</a></>;
  return <>
    <a className="skip" href="#conteudo" onClick={evento => { evento.preventDefault(); document.getElementById('conteudo').focus(); }}>Ir para o conteúdo</a>
    <header className="top"><nav className="wrap navigation" aria-label="Principal">
      <a className="brand" href="#/"><span className="symbol" aria-hidden="true">N</span> Negocia</a>
      <a href="#/">Painel</a><span className="demo">Modo demonstração</span>
    </nav></header>
    <main id="conteudo" className="wrap main" tabIndex="-1" key={rota}>{tela}</main>
  </>;
}

createRoot(document.getElementById('root')).render(<App />);
