// Uso: node docs/poc/otimizar-parametros.mjs [pastaDados] [arquivoSimulacao] [avaliacoes]
// Busca parâmetros que melhorem o resultado simulado sem violar as regras de frota (guidelines 6.4):
// R1, equipes usadas <= disponíveis; R2, km total <= km da referência simulada e cada rota <= limite.
// Cada execução parte do melhor resultado já gravado em arquivoSimulacao e tenta superá-lo.
// combinacoes-testadas.json guarda o que já foi simulado, para nenhuma execução repetir combinações.
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { criarSimulador } from "./simulador.mjs";

export const ESPACO = {
  PotenciaPesos: [3, 4, 5, 6],
  OsPrazoImportancia: [1, 2, 3, 4, 5, 6, 7],
  CompensacaoImportancia: [4, 5, 6, 7],
  MetrosLimiteRotaPorEquipeSoft: [1000, 3000, 5000, 10000, 15000, 20000],
  CustoKmEquipe: [5, 10, 15, 20, 30, 40, 60],
  janelaVencidaCorrigida: [false, true],
  horasSoftVencida: [2, 4, 6, 9],
  multaVencidaHora: [1, 10, 50, 100],
};

export const CRITERIO =
  "Só entram candidatos que cumprem R1 e R2. Entre eles, maior pontuação = OS atendidas + vencidas em rota (a vencida conta em dobro). Empate: menos km.";

const fmtNum = (v) => (typeof v === "number" ? v.toLocaleString("pt-BR") : v);
const sinal = (v) => `${v > 0 ? "+" : v < 0 ? "−" : "±"}${fmtNum(Math.abs(Math.round(v)))}`;

// Explica o que a troca de cada parâmetro faz no objetivo do solver.
const MECANISMOS = {
  PotenciaPesos: (de, para) => para > de
    ? `Aumenta a escala do penalty inteiro (base ${de} para ${para}). Pular OS fica mais caro em relação ao custo de km, e o solver atende mais.`
    : `Reduz a escala do penalty inteiro (base ${de} para ${para}). O custo de km passa a pesar mais na decisão de atender ou pular.`,
  OsPrazoImportancia: (de, para, p) => `O fator de prazo (horas vencida ou até vencer) passa de ${p.PotenciaPesos}^${de} para ${p.PotenciaPesos}^${para} no penalty, ${para > de ? "e as OS mais atrasadas sobem na fila" : "e o atraso perde peso diante dos outros fatores"}.`,
  CompensacaoImportancia: (de, para, p) => `A compensação financeira passa de ${p.PotenciaPesos}^${de} para ${p.PotenciaPesos}^${para} no penalty, ${para > de ? "e as OS com compensação alta ganham prioridade" : "o que abre espaço para o prazo pesar mais e evita que poucas OS caras dominem a fila"}.`,
  MetrosLimiteRotaPorEquipeSoft: (de, para) => `A cobrança extra de 30 por km só começa depois de ${fmtNum(para / 1000)} km de rota, e não mais de ${fmtNum(de / 1000)} km. ${para > de ? "Rotas de tamanho normal deixam de pagar o adicional." : "Rotas longas passam a ser mais penalizadas."}`,
  CustoKmEquipe: (de, para) => para < de
    ? `Cada km custa ${fmtNum(para)} em vez de ${fmtNum(de)} no objetivo, e o solver aceita desvios curtos para pegar mais OS. Quem segura o km total é a R2, não esse custo.`
    : `Cada km custa ${fmtNum(para)} em vez de ${fmtNum(de)} no objetivo, e o solver evita desvios, o que reduz o km total.`,
  janelaVencidaCorrigida: (de, para) => para
    ? "A OS vencida passa a ter uma única janela no turno inteiro, sem custo fixo igual à compensação. Atender passa a custar menos que pular."
    : "Volta às duas visitas do builder atual para a vencida (08:15–08:15, ou turno inteiro com custo igual à compensação).",
  horasSoftVencida: (de, para) => `A multa por atraso da vencida começa ${fmtNum(para)} h depois do início do turno, e não ${fmtNum(de)} h, ${para > de ? "o que dá mais folga para encaixá-la na rota" : "o que força o atendimento mais cedo"}.`,
  multaVencidaHora: (de, para) => `A multa por hora de atraso da vencida, depois do soft end, passa de ${fmtNum(de)} para ${fmtNum(para)}, ${para > de ? "e as vencidas vão para o início da rota" : "e o solver ganha liberdade para encaixá-las mais tarde"}.`,
};

const relevante = (k, p) => !((k === "horasSoftVencida" || k === "multaVencidaHora") && !p.janelaVencidaCorrigida);

// Para cada parâmetro trocado, volta só ele ao valor do dia e mede a diferença.
export const justificar = (sim, params, base, resultado) =>
  Object.keys(ESPACO)
    .filter((k) => relevante(k, params) && params[k] !== base[k])
    .map((k) => {
      const semTroca = { ...params, [k]: base[k] };
      const r = sim.simular(paraSimulador(semTroca));
      const ganho = { os: resultado.atendidas - r.atendidas, vencidas: resultado.vencidasAtendidas - r.vencidasAtendidas, km: resultado.kmTotal - r.kmTotal };
      const partes = [`${sinal(ganho.os)} OS`, `${sinal(ganho.vencidas)} vencidas em rota`, `${sinal(ganho.km)} km`];
      const violadas = [!r.regras.r1.ok && "R1", !r.regras.r2.ok && "R2"].filter(Boolean).join(" e ");
      const limites = [
        !r.regras.r1.ok && `${r.regras.r1.equipesUsadas} equipes de ${r.regras.r1.equipesDisponiveis} disponíveis`,
        !r.regras.r2.ok && `${fmtNum(Math.round(r.kmTotal))} km com limite de ${fmtNum(Math.round(r.regras.r2.kmReferencia))} km`,
      ].filter(Boolean).join(", ");
      const neutra = ganho.os === 0 && ganho.vencidas === 0;
      let porque;
      if (!r.regras.ok) porque = `É a troca que mantém a combinação dentro de ${violadas}. Sem ela, seriam ${fmtNum(r.atendidas)} OS, mas com ${limites}. Saldo da troca: ${partes.join(", ")}.`;
      else if (ganho.os + ganho.vencidas < 0) porque = `Sozinha, esta troca piora a combinação: ${partes.join(", ")}. Ela entrou porque a busca avalia a combinação inteira. Vale testar voltar este parâmetro ao valor do dia.`;
      else if (neutra) porque = `Nesta combinação, a troca não mudou OS nem vencidas (${sinal(ganho.km)} km). Ela pode ser revertida sem perda.`;
      else porque = `Comparada com a mesma combinação sem esta troca, ela rende ${partes.join(", ")}.`;
      return {
        parametro: k,
        de: base[k],
        para: params[k],
        mecanismo: MECANISMOS[k](base[k], params[k], params),
        efeito: { ...ganho, semTroca: { atendidas: r.atendidas, vencidasAtendidas: r.vencidasAtendidas, kmTotal: r.kmTotal, regrasOk: r.regras.ok } },
        porque,
      };
    });

const paraSimulador = (p) => ({
  pesos: { PotenciaPesos: p.PotenciaPesos, OsPrazoImportancia: p.OsPrazoImportancia, CompensacaoImportancia: p.CompensacaoImportancia },
  softMaxMetros: p.MetrosLimiteRotaPorEquipeSoft,
  custoKmEquipe: p.CustoKmEquipe,
  corrigirVencida: p.janelaVencidaCorrigida,
  horasSoftVencida: p.horasSoftVencida,
  multaVencidaHora: p.multaVencidaHora,
});

const chave = (p) => Object.keys(ESPACO).map((k) => (k === "horasSoftVencida" || k === "multaVencidaHora") && !p.janelaVencidaCorrigida ? "-" : p[k]).join("|");
const pontuacao = (m) => m.atendidas + m.vencidasAtendidas;
const melhorQue = (a, b) => !b || pontuacao(a) > pontuacao(b) || (pontuacao(a) === pontuacao(b) && a.kmTotal < b.kmTotal);

// Gerador pseudoaleatório com semente, para a busca ser reproduzível.
const mulberry32 = (semente) => () => {
  semente |= 0; semente = (semente + 0x6d2b79f5) | 0;
  let t = Math.imul(semente ^ (semente >>> 15), 1 | semente);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
};

// Muda de 1 a `raio` parâmetros; com raio maior, cada passo também pode pular mais de uma posição.
const vizinho = (p, rnd, raio) => {
  const q = { ...p };
  const dims = Object.keys(ESPACO);
  const mudar = 1 + Math.floor(rnd() * raio);
  for (let i = 0; i < mudar; i++) {
    const d = dims[Math.floor(rnd() * dims.length)];
    const vals = ESPACO[d];
    const atual = Math.max(0, vals.indexOf(q[d]));
    const tamanho = 1 + Math.floor(rnd() * Math.min(raio, vals.length - 1));
    let destino = atual + (rnd() < 0.5 ? -tamanho : tamanho);
    if (destino < 0 || destino >= vals.length) destino = atual + (destino < 0 ? tamanho : -tamanho);
    q[d] = vals[Math.min(vals.length - 1, Math.max(0, destino))];
  }
  return q;
};
const aleatorio = (rnd) => Object.fromEntries(Object.entries(ESPACO).map(([k, v]) => [k, v[Math.floor(rnd() * v.length)]]));

const soVencida = (k) => k === "horasSoftVencida" || k === "multaVencidaHora";
export const TAMANHO_ESPACO = Object.keys(ESPACO)
  .filter((k) => !soVencida(k) && k !== "janelaVencidaCorrigida")
  .reduce((a, k) => a * ESPACO[k].length, 1) * (1 + ESPACO.horasSoftVencida.length * ESPACO.multaVencidaHora.length);

// Memória das combinações já simuladas, por lote: chave -> [atendidas, vencidas, km, equipes, regrasOk].
const lerMemoria = (arquivo) => (fs.existsSync(arquivo) ? JSON.parse(fs.readFileSync(arquivo, "utf8")) : {});
const daChave = (k, base) => {
  const v = k.split("|");
  return Object.fromEntries(Object.keys(ESPACO).map((d, i) => [d, v[i] === "-" ? base[d] : JSON.parse(v[i])]));
};
const daMemoria = (k, v, base) => ({
  parametros: daChave(k, base), atendidas: v[0], vencidasAtendidas: v[1], kmTotal: v[2], veiculosUsados: v[3], regras: { ok: v[4] === 1 },
});

// Acrescenta a execução ao histórico (só agregados), mais recente por último.
const registrarHistorico = (arquivoHistorico, entrada) => {
  const hist = fs.existsSync(arquivoHistorico) ? JSON.parse(fs.readFileSync(arquivoHistorico, "utf8")) : { execucoes: [] };
  hist.execucoes.push(entrada);
  fs.writeFileSync(arquivoHistorico, JSON.stringify(hist, null, 2) + "\n", "utf8");
};

export { paraSimulador };

export const otimizar = async ({
  dataDir, arquivos, arquivoSimulacao, avaliacoes = 120, semente = Date.now() % 1e9, aoProgresso = () => {},
  dia, origem = "linha de comando",
  arquivoHistorico = path.join(path.dirname(arquivoSimulacao), "historico-simulacoes.json"),
  arquivoMemoria = path.join(path.dirname(arquivoSimulacao), "combinacoes-testadas.json"),
}) => {
  const sim = criarSimulador(dataDir, arquivos);
  const doc = JSON.parse(fs.readFileSync(arquivoSimulacao, "utf8"));
  const anterior = doc.otimizacao;
  const base = {
    PotenciaPesos: sim.base.PotenciaPesos,
    OsPrazoImportancia: sim.base.OsPrazoImportancia,
    CompensacaoImportancia: sim.base.CompensacaoImportancia,
    MetrosLimiteRotaPorEquipeSoft: sim.base.MetrosLimiteRotaPorEquipeSoft,
    CustoKmEquipe: sim.base.CustoKmEquipe,
    janelaVencidaCorrigida: false,
    horasSoftVencida: 4,
    multaVencidaHora: sim.base.MultaAtrasoReguladaHora,
  };

  const rnd = mulberry32(semente);
  const memoria = lerMemoria(arquivoMemoria);
  const conhecidos = (memoria[sim.cenario] ??= {});
  const vistos = new Map();
  const avaliar = (p) => {
    const k = chave(p);
    if (!vistos.has(k)) {
      const r = { parametros: p, ...sim.simular(paraSimulador(p)) };
      vistos.set(k, r);
      conhecidos[k] = [r.atendidas, r.vencidasAtendidas, Math.round(r.kmTotal * 10) / 10, r.veiculosUsados, r.regras.ok ? 1 : 0];
    }
    return vistos.get(k);
  };
  const viaveisConhecidos = () => Object.entries(conhecidos)
    .filter(([, v]) => v[4] === 1)
    .map(([k, v]) => daMemoria(k, v, base))
    .sort((a, b) => (melhorQue(a, b) ? -1 : 1));

  const referencia = avaliar(base);
  let melhor = referencia.regras.ok ? referencia : null;
  for (const p of [anterior?.melhor?.parametros, viaveisConhecidos()[0]?.parametros].filter(Boolean)) {
    const r = avaliar(p);
    if (r.regras.ok && melhorQue(r, melhor)) melhor = r;
  }
  const melhorInicial = melhor;
  const inicioRodada = Date.now();
  let melhorDaRodada = null;

  // Quanto mais rodadas seguidas sem melhora, mais longe a busca vai.
  const historicoAnterior = anterior?.historico ?? [];
  let estagnacao = 0;
  for (let i = historicoAnterior.length - 1; i >= 0 && !historicoAnterior[i].melhorou; i--) estagnacao++;
  const raio = Math.min(4, 2 + estagnacao);
  const fracaoAleatoria = Math.min(0.6, 0.3 + 0.1 * estagnacao);
  const partidas = viaveisConhecidos().slice(0, 6).map((r) => r.parametros);
  const escolherPartida = () => (rnd() < 0.5 || partidas.length < 2 ? melhor?.parametros ?? base : partidas[1 + Math.floor(rnd() * (partidas.length - 1))]);
  const novos = new Set();
  const gerarNovo = () => {
    for (let t = 0; t < 300; t++) {
      const c = rnd() < fracaoAleatoria ? aleatorio(rnd) : vizinho(escolherPartida(), rnd, raio);
      if (!(chave(c) in conhecidos)) return c;
    }
    return null;
  };

  for (let i = 0; i < avaliacoes; i++) {
    const candidato = gerarNovo();
    if (!candidato) break;
    const r = avaliar(candidato);
    novos.add(r);
    if (r.regras.ok && melhorQue(r, melhor)) melhor = r;
    if (r.regras.ok && melhorQue(r, melhorDaRodada)) melhorDaRodada = r;
    aoProgresso({ feitas: i + 1, total: avaliacoes, melhor: melhor && { atendidas: melhor.atendidas, vencidas: melhor.vencidasAtendidas, km: melhor.kmTotal } });
    await new Promise((ok) => setImmediate(ok));
  }
  fs.writeFileSync(arquivoMemoria, JSON.stringify(memoria) + "\n", "utf8");
  const estrategia = { estagnacao, raio, fracaoAleatoria: Math.round(fracaoAleatoria * 100) / 100, partidas: Math.max(1, partidas.length) };
  const cobertura = { testadas: Object.keys(conhecidos).length, espaco: TAMANHO_ESPACO };

  const resumo = (r) => ({
    parametros: r.parametros,
    pontuacao: pontuacao(r),
    atendidas: r.atendidas,
    vencidasAtendidas: r.vencidasAtendidas,
    kmTotal: r.kmTotal,
    veiculosUsados: r.veiculosUsados,
    regrasOk: r.regras.ok,
  });
  const todos = [...novos];
  const viaveis = todos.filter((r) => r.regras.ok).sort((a, b) => (melhorQue(a, b) ? -1 : 1));
  const inviaveis = todos.filter((r) => !r.regras.ok).sort((a, b) => pontuacao(b) - pontuacao(a));
  const rankingGeral = viaveisConhecidos();

  const agora = new Date().toISOString();
  const melhorou = !melhorInicial || melhorQue(melhor, melhorInicial);
  const justMelhor = justificar(sim, melhor.parametros, base, melhor);
  const justRodada = melhorDaRodada && (melhorDaRodada === melhor ? justMelhor : justificar(sim, melhorDaRodada.parametros, base, melhorDaRodada));
  doc.otimizacao = {
    executadaEm: agora,
    criterio: CRITERIO,
    espaco: ESPACO,
    semente,
    avaliacoesNestaRodada: novos.size,
    estrategia,
    cobertura,
    rodadas: (anterior?.rodadas ?? 0) + 1,
    referencia: resumo(referencia),
    melhor: { ...resumo(melhor), justificativas: justMelhor },
    melhorou,
    ranking: rankingGeral.slice(0, 8).map(resumo),
    melhoresInviaveis: inviaveis.slice(0, 3).map((r) => ({ ...resumo(r), regras: r.regras })),
    historico: [...historicoAnterior, { executadaEm: agora, semente, avaliacoes: novos.size, pontuacao: pontuacao(melhor), atendidas: melhor.atendidas, vencidasAtendidas: melhor.vencidasAtendidas, kmTotal: melhor.kmTotal, melhorou }],
  };
  const { parametros, ...metricas } = melhor;
  doc.cenarios.otimizado = {
    descricao: "Melhor combinação encontrada pela busca automática que cumpre R1 e R2.",
    parametros,
    ...metricas,
  };
  fs.writeFileSync(arquivoSimulacao, JSON.stringify(doc, null, 2) + "\n", "utf8");

  registrarHistorico(arquivoHistorico, {
    executadaEm: agora,
    dia: dia ?? null,
    cenario: sim.cenario,
    origem,
    rodada: doc.otimizacao.rodadas,
    semente,
    tentativas: avaliacoes,
    combinacoesTestadas: novos.size,
    estrategia,
    cobertura,
    duracaoSegundos: Math.round((Date.now() - inicioRodada) / 100) / 10,
    criterio: CRITERIO,
    melhorou,
    referencia: resumo(referencia),
    melhorDaRodada: melhorDaRodada && { ...resumo(melhorDaRodada), regras: melhorDaRodada.regras, justificativas: justRodada },
    melhorGeral: { ...resumo(melhor), regras: melhor.regras, justificativas: justMelhor },
    viaveis: viaveis.length,
    inviaveis: inviaveis.length,
    ranking: viaveis.slice(0, 5).map(resumo),
  });
  return doc.otimizacao;
};

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const dataDir = process.argv[2] ?? path.join("docs", "Dados de treinamento");
  const arquivoSimulacao = process.argv[3] ?? path.join("docs", "poc", "simulacao.json");
  const avaliacoes = Number(process.argv[4] ?? 120);
  let ultimo = 0;
  const r = await otimizar({
    dataDir, arquivoSimulacao, avaliacoes,
    aoProgresso: (p) => {
      if (p.feitas - ultimo >= 20 || p.feitas === p.total) {
        ultimo = p.feitas;
        console.log(`${p.feitas}/${p.total} · melhor: ${p.melhor ? `${p.melhor.atendidas} OS, ${p.melhor.vencidas} vencidas, ${p.melhor.km} km` : "nenhum viável"}`);
      }
    },
  });
  console.log(`Rodada ${r.rodadas}: ${r.melhorou ? "melhorou" : "não superou o melhor anterior"}.`, JSON.stringify(r.melhor));
}
